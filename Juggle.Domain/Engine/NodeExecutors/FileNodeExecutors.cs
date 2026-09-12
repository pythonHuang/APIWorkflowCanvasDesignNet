using System.Text;
using System.Text.Json;
using ClosedXML.Excel;

namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>文件解析节点：解析文本/JSON/XML/CSV/图片 文件内容（支持 data URL 或 base64 输入，图片用视觉模型识别）。</summary>
public class FileParseNodeExecutor : INodeExecutor
{
    private readonly Func<AiChatRequest, Task<AiChatResult>>? _chat;

    public FileParseNodeExecutor(Func<AiChatRequest, Task<AiChatResult>>? chat = null) => _chat = chat;

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.FileParseConfig
            ?? throw new InvalidOperationException($"文件解析节点 [{node.Key}] 未配置 fileParseConfig。");

        var raw = string.IsNullOrWhiteSpace(cfg.Input) ? "" : context.GetVariable(cfg.Input)?.ToString() ?? "";
        var fileType = (cfg.FileType ?? "auto").ToLower();

        object? result;
        if (fileType == "image")
        {
            // 图片：视觉模型识别（未接入大模型时报错）
            if (_chat == null)
                throw new InvalidOperationException("流程引擎未接入大模型，无法识别图片（请先在系统设置 → 大模型设置中配置供应商）");
            var imageUrl = raw.Trim();
            if (!imageUrl.StartsWith("data:image") && !imageUrl.StartsWith("http"))
                imageUrl = "data:image/png;base64," + imageUrl;
            result = (await _chat(new AiChatRequest(
                "你是一个专业的图片识别助手，请详细、准确地描述图片内容（文字、对象、场景等）。",
                "请识别这张图片", new List<string> { imageUrl }, 0, null))).Text;
        }
        else
        {
            var content = DecodeContent(raw);
            result = fileType switch
            {
                "json" => ParseJson(content),
                "csv" => ParseCsv(content),
                "text" => content,
                "xml" => content,
                _ => TryParseJson(content, out var obj) ? obj : content
            };
        }

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, result);

        return node.Outgoings.FirstOrDefault();
    }

    /// <summary>解析 JSON 为对象/数组（失败返回原文）。</summary>
    internal static object? ParseJson(string content)
        => TryParseJson(content, out var obj) ? obj : content;

    internal static bool TryParseJson(string content, out object? obj)
    {
        obj = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            obj = CloneToNative(doc.RootElement);
            return true;
        }
        catch { return false; }
    }

    /// <summary>解析 CSV：首行为表头，返回行字典数组。</summary>
    internal static object ParseCsv(string content)
    {
        var rows = new List<Dictionary<string, object?>>();
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var headers = new List<string>();
        var first = true;
        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine)) continue;
            var cells = SplitCsvLine(rawLine);
            if (first)
            {
                headers = cells;
                first = false;
                continue;
            }
            var row = new Dictionary<string, object?>();
            for (int i = 0; i < cells.Count; i++)
                row[headers.Count > i ? headers[i] : $"column{i + 1}"] = cells[i];
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        var inQuote = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (ch == ',' && !inQuote) { cells.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(ch);
        }
        cells.Add(sb.ToString().Trim());
        return cells;
    }

    /// <summary>解码输入：data URL → 提取 base64 解码；纯 base64 → 解码；否则原文本。</summary>
    internal static string DecodeContent(string raw)
    {
        try
        {
            var text = raw.Trim();
            if (text.StartsWith("data:"))
            {
                var comma = text.IndexOf(',');
                if (comma > 0)
                {
                    var payload = text[(comma + 1)..];
                    return Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                }
            }
            // 尝试作为纯 base64 解码（可解码且无非法 JSON/XML 时）
            if (text.Length > 8 && !text.StartsWith("{") && !text.StartsWith("[") && !text.StartsWith("<") && !text.Contains(' ') && !text.Contains('\n'))
            {
                var bytes = Convert.FromBase64String(text);
                var decoded = Encoding.UTF8.GetString(bytes);
                // 解码后是文本内容才采用
                if (decoded.Any(ch => ch == '\n' || ch == '{' || ch == '[' || ch == '<' || ch == '，' || ch == '一'))
                    return decoded;
            }
        }
        catch { /* 解码失败按原文处理 */ }
        return raw;
    }

    internal static object? CloneToNative(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(CloneToNative).ToList(),
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => CloneToNative(p.Value)),
            _ => el.ToString()
        };
    }
}

/// <summary>Excel 读取节点：读取 Excel（base64 / data URL）第一个工作表，首行为表头，返回行字典数组。</summary>
public class ExcelReadNodeExecutor : INodeExecutor
{
    public Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.ExcelReadConfig
            ?? throw new InvalidOperationException($"Excel 读取节点 [{node.Key}] 未配置 excelReadConfig。");

        var raw = string.IsNullOrWhiteSpace(cfg.Input) ? "" : context.GetVariable(cfg.Input)?.ToString() ?? "";
        var text = raw.Trim();
        byte[] bytes;
        try
        {
            if (text.StartsWith("data:"))
            {
                var comma = text.IndexOf(',');
                bytes = Convert.FromBase64String(text[(comma + 1)..]);
            }
            else
            {
                bytes = Convert.FromBase64String(text);
            }
        }
        catch
        {
            throw new InvalidOperationException("Excel 内容不是有效的 base64 / data URL，请确认输入变量为 Excel 文件内容");
        }

        using var ms = new MemoryStream(bytes);
        using var wb = new XLWorkbook(ms);
        var ws = string.IsNullOrWhiteSpace(cfg.SheetName)
            ? wb.Worksheets.First()
            : wb.Worksheets.FirstOrDefault(w => w.Name == cfg.SheetName)
              ?? throw new InvalidOperationException($"工作表 [{cfg.SheetName}] 不存在");

        var rows = new List<Dictionary<string, object?>>();
        var headerRow = ws.FirstRowUsed();
        if (headerRow == null) { context.SetVariable(cfg.Output, rows); return Task.FromResult(node.Outgoings.FirstOrDefault()); }

        var headers = new List<string>();
        foreach (var cell in headerRow.CellsUsed())
            headers.Add(cell.GetString());

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            if (row.RowNumber() == headerRow.RowNumber()) continue;
            var dict = new Dictionary<string, object?>();
            var ci = 0;
            foreach (var cell in row.CellsUsed())
            {
                var key = headers.Count > ci ? headers[ci] : $"column{ci + 1}";
                dict[key] = cell.Value.IsBlank ? null : (object?)cell.GetString();
                ci++;
            }
            if (dict.Count > 0) rows.Add(dict);
        }

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, rows);

        return Task.FromResult(node.Outgoings.FirstOrDefault());
    }
}

/// <summary>文件写入节点：把变量内容（对象序列化为 JSON）生成 data URL 写入输出变量，供下载或后续节点使用。</summary>
public class FileWriteNodeExecutor : INodeExecutor
{
    public Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.FileWriteConfig
            ?? throw new InvalidOperationException($"文件写入节点 [{node.Key}] 未配置 fileWriteConfig。");

        var val = string.IsNullOrWhiteSpace(cfg.Content) ? null : context.GetVariable(cfg.Content);
        var fileType = (cfg.FileType ?? "text").ToLower();
        var content = val switch
        {
            null => "",
            string s => s,
            _ => JsonSerializer.Serialize(val)
        };

        var mime = fileType switch
        {
            "json" => "application/json",
            "csv" => "text/csv",
            _ => "text/plain"
        };
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content));
        var dataUrl = $"data:{mime};base64,{base64}";

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, dataUrl);
        if (!string.IsNullOrWhiteSpace(cfg.FileName))
            context.SetVariable(cfg.FileName, dataUrl);

        return Task.FromResult(node.Outgoings.FirstOrDefault());
    }
}
