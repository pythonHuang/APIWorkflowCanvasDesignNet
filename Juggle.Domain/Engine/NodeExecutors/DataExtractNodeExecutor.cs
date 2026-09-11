using System.Text;
using System.Text.Json;

namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// 数据提取节点：从输入文本中提取内容。
/// 提取类型：json（首个 JSON 对象/数组）、code（代码块）、keyword（关键字后内容）、
///          between（开始/结束标志之间）、length（按偏移与长度截取）。
/// </summary>
public class DataExtractNodeExecutor : INodeExecutor
{
    public Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.DataExtractConfig
            ?? throw new InvalidOperationException($"数据提取节点 [{node.Key}] 未配置 dataExtractConfig。");

        var raw = string.IsNullOrWhiteSpace(cfg.Input) ? "" : context.GetVariable(cfg.Input)?.ToString() ?? "";
        var type = (cfg.ExtractType ?? "json").ToLower();

        object? result = type switch
        {
            "json" => ExtractJson(raw),
            "code" => ExtractCode(raw),
            "keyword" => ExtractAfterKeyword(raw, cfg.Keyword ?? ""),
            "between" => ExtractBetween(raw, cfg.StartFlag ?? "", cfg.EndFlag ?? ""),
            "length" => ExtractLength(raw, cfg.Offset, cfg.Length),
            _ => raw
        };

        if (!string.IsNullOrWhiteSpace(cfg.Output))
        {
            switch ((cfg.OutputTargetType ?? "VARIABLE").ToUpper())
            {
                case "OUTPUT":
                    context.SetOutputParameter(cfg.Output, result);
                    break;
                default:
                    context.SetVariable(cfg.Output, result);
                    break;
            }
        }

        return Task.FromResult(node.Outgoings.FirstOrDefault());
    }

    /// <summary>提取首个 JSON 对象/数组（返回解析对象，失败返回原文）。</summary>
    public static object? ExtractJson(string raw)
    {
        var start = raw.IndexOfAny(new[] { '{', '[' });
        if (start < 0) return raw;
        var end = FindBalancedEnd(raw, start);
        if (end < 0) return raw;
        var json = raw.Substring(start, end - start + 1);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return FileParseNodeExecutor.CloneToNative(doc.RootElement);
        }
        catch { return json; }
    }

    /// <summary>从 { 或 [ 开始找平衡闭合位置。</summary>
    private static int FindBalancedEnd(string s, int start)
    {
        var openers = new Stack<char>();
        for (int i = start; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch == '{' || ch == '[')
            {
                openers.Push(ch);
            }
            else if (ch == '}' || ch == ']')
            {
                if (openers.Count == 0) return -1;
                var opener = openers.Pop();
                if ((ch == '}' && opener != '{') || (ch == ']' && opener != '[')) return -1;
                if (openers.Count == 0) return i;
            }
        }
        return -1;
    }

    /// <summary>提取代码块内容（```lang ... ```）。</summary>
    public static string ExtractCode(string raw)
    {
        var fenceIdx = raw.IndexOf("```", StringComparison.Ordinal);
        if (fenceIdx < 0) return raw;
        var contentStart = raw.IndexOf('\n', fenceIdx);
        if (contentStart < 0) return raw;
        var endIdx = raw.IndexOf("```", contentStart + 1, StringComparison.Ordinal);
        if (endIdx < 0) return raw.Substring(contentStart + 1).Trim();
        return raw.Substring(contentStart + 1, endIdx - contentStart - 1).Trim();
    }

    /// <summary>提取关键字之后的内容（跳过 : ：= 空格等分隔符，取到行尾）。</summary>
    public static string ExtractAfterKeyword(string raw, string keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return raw;
        var idx = raw.IndexOf(keyword, StringComparison.Ordinal);
        if (idx < 0) return "";
        var pos = idx + keyword.Length;
        while (pos < raw.Length && (raw[pos] == ':' || raw[pos] == '：' || raw[pos] == '=' || raw[pos] == ' ' || raw[pos] == '\t'))
            pos++;
        var lineEnd = raw.IndexOf('\n', pos);
        return lineEnd < 0 ? raw.Substring(pos).Trim() : raw.Substring(pos, lineEnd - pos).Trim();
    }

    /// <summary>提取开始标志与结束标志之间的内容（首次出现）。</summary>
    public static string ExtractBetween(string raw, string startFlag, string endFlag)
    {
        if (string.IsNullOrEmpty(startFlag)) return raw;
        var sIdx = raw.IndexOf(startFlag, StringComparison.Ordinal);
        if (sIdx < 0) return "";
        var start = sIdx + startFlag.Length;
        if (string.IsNullOrEmpty(endFlag)) return raw.Substring(start);
        var eIdx = raw.IndexOf(endFlag, start, StringComparison.Ordinal);
        return eIdx < 0 ? raw.Substring(start) : raw.Substring(start, eIdx - start);
    }

    /// <summary>按偏移与长度截取。</summary>
    public static string ExtractLength(string raw, int offset, int length)
    {
        var off = Math.Max(0, offset);
        if (off >= raw.Length) return "";
        var len = length > 0 ? length : raw.Length - off;
        return raw.Substring(off, Math.Min(len, raw.Length - off));
    }
}
