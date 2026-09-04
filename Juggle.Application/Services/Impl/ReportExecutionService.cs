using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Juggle.Domain.Engine;
using Juggle.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace Juggle.Application.Services.Impl;

public class ReportExecutionService
{
    private readonly JuggleDbContext _db;
    public ReportExecutionService(JuggleDbContext db) => _db = db;

    /// <summary>执行数据源的SQL查询返回DataTable</summary>
    public async Task<DataTable> ExecuteQuery(long dataSourceId, string sql, Dictionary<string, object?>? parameters = null)
        => (await ExecuteQueryInternal(dataSourceId, sql, parameters, 0)).Table;

    /// <summary>执行查询并限制返回行数（SQL 测试用），通过 Total 返回总行数</summary>
    public async Task<(DataTable Table, int Total)> ExecuteQueryLimitedAsync(long dataSourceId, string sql, Dictionary<string, object?>? parameters, int maxRows)
        => await ExecuteQueryInternal(dataSourceId, sql, parameters, maxRows);

    private async Task<(DataTable Table, int Total)> ExecuteQueryInternal(long dataSourceId, string sql, Dictionary<string, object?>? parameters, int maxRows)
    {
        ValidateSql(sql);
        var ds = await _db.DataSources.FindAsync(dataSourceId)
            ?? throw new Exception("数据源不存在");
        var connStr = BuildConnectionString(ds);
        using var conn = CreateConnection(ds.DsType!, connStr);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        if (parameters != null)
            foreach (var p in parameters)
                cmd.Parameters.Add(CreateParameter(p.Key, p.Value, ds.DsType!));

        var dt = new DataTable();
        using var reader = cmd.ExecuteReader();
        if (maxRows <= 0)
        {
            dt.Load(reader);
            return (dt, dt.Rows.Count);
        }

        // 受限读取：统计总行数，只保留前 maxRows 行
        for (int i = 0; i < reader.FieldCount; i++)
            dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i) ?? typeof(object));
        var values = new object[reader.FieldCount];
        var total = 0;
        while (reader.Read())
        {
            total++;
            if (dt.Rows.Count >= maxRows) continue;
            reader.GetValues(values);
            dt.Rows.Add(values.Select(v => v == DBNull.Value ? (object?)null : v).ToArray());
        }
        return (dt, total);
    }

    /// <summary>SQL 安全校验：禁止写操作关键字</summary>
    public static void ValidateSql(string sql)
    {
        var upper = " " + sql.ToUpper().Trim() + " ";
        var dangerous = new[] { " DROP ", " DELETE ", " INSERT ", " ALTER ", " CREATE ", " TRUNCATE ", " EXEC ", " EXECUTE " };
        foreach (var kw in dangerous)
        {
            if (upper.Contains(kw))
                throw new Exception($"SQL 包含不允许的操作: {kw.Trim()}");
        }
        // UPDATE 仅拦截不以 SELECT...FOR UPDATE 开头的
        if (upper.Contains(" UPDATE ") && !upper.TrimStart().StartsWith("SELECT"))
            throw new Exception("SQL 包含不允许的操作: UPDATE");
    }

    /// <summary>渲染报表为HTML（数据行自动扩展、列宽/行高所见即所得、支持斑马纹隔行变色）</summary>
    public async Task<string> RenderToHtml(string layoutJson, Dictionary<string, object?>? queryParams = null)
    {
        using var doc = JsonDocument.Parse(layoutJson);
        var root = doc.RootElement;
        var datasets = await ResolveDatasetsAsync(root, queryParams);
        return RenderHtmlCore(root, datasets);
    }

    /// <summary>执行 layoutJson 中定义的全部数据集，返回 数据集id → DataTable。</summary>
    private async Task<Dictionary<string, DataTable>> ResolveDatasetsAsync(JsonElement root, Dictionary<string, object?>? queryParams)
    {
        var datasets = new Dictionary<string, DataTable>();
        if (root.TryGetProperty("datasets", out var dsArr) && dsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var ds in dsArr.EnumerateArray())
            {
                var id = ds.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var sourceType = ds.TryGetProperty("sourceType", out var stEl) ? stEl.GetString() ?? "" : "";
                var sourceRef = ds.TryGetProperty("sourceRef", out var srEl) ? srEl.GetString() ?? "" : "";
                var customSql = ds.TryGetProperty("customSql", out var csEl) ? csEl.GetString() ?? "" : "";
                var dataSourceId = ds.TryGetProperty("dataSourceId", out var dsi)
                    ? dsi.ValueKind switch
                    {
                        JsonValueKind.Number => dsi.GetInt64(),
                        JsonValueKind.String when long.TryParse(dsi.GetString(), out var v) => v,
                        _ => 0
                    }
                    : 0;

                var dt = await ResolveSource(sourceType, sourceRef, customSql, dataSourceId, queryParams);
                if (dt != null)
                {
                    datasets[id] = dt;
                    // 占位符通常用数据集名称引用（如 ${订单.字段}），按名称再注册一份索引
                    var name = ds.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(name) && !datasets.ContainsKey(name))
                        datasets[name] = dt;
                }
            }
        }
        return datasets;
    }

    /// <summary>HTML 渲染核心：数据行按数据集自动扩展，静态行（标题/表头/汇总）渲染一次。</summary>
    private static string RenderHtmlCore(JsonElement root, Dictionary<string, DataTable> datasets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
        sb.AppendLine("table{border-collapse:collapse;font-family:'Microsoft YaHei',sans-serif;table-layout:fixed;}");
        sb.AppendLine("td{padding:4px 6px;word-wrap:break-word;}");
        sb.AppendLine("@media print{@page{size:A4;margin:15mm}}</style></head><body>");

        var cellMap = BuildCellMap(root);
        var maxCols = GetColCount(root);
        var materialized = MaterializeRows(root, datasets);
        var templateUsed = new HashSet<(int r, int c)>();   // 静态行之间的跨行合并占用

        // 文档背景图：包一层容器渲染
        var pageBg = root.TryGetProperty("page", out var pgEl)
            && pgEl.TryGetProperty("bgImage", out var pbi) && pbi.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(pbi.GetString())
            ? pbi.GetString() : null;
        if (pageBg != null)
            sb.AppendLine($"<div style=\"background-image:url('{pageBg}');background-size:cover;background-position:center;-webkit-print-color-adjust:exact;print-color-adjust:exact;padding:8px;\">");

        sb.AppendLine("<table>");
        // 列宽所见即所得：按设计器 cols[].width 生成 colgroup
        if (root.TryGetProperty("cols", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            sb.Append("<colgroup>");
            foreach (var col in colsEl.EnumerateArray())
            {
                var w = col.TryGetProperty("width", out var we) && we.ValueKind == JsonValueKind.Number ? Math.Max(0, we.GetInt32()) : 0;
                sb.Append(w > 0 ? $"<col style='width:{w}px'/>" : "<col/>");
            }
            sb.AppendLine("</colgroup>");
        }
        foreach (var rr in materialized)
        {
            // 应用设计器的行高设置
            var rowHeight = GetRowHeight(root, rr.TemplateRow);
            sb.Append("<tr");
            if (rowHeight > 0) sb.Append($" style='height:{rowHeight}px'");
            sb.Append(">");
            var used = new HashSet<int>();
            for (int ci = 0; ci < maxCols; ci++)
            {
                if (used.Contains(ci)) continue;
                if (rr.DataIndex == null && templateUsed.Contains((rr.TemplateRow, ci))) continue;

                var hasCell = cellMap.TryGetValue((rr.TemplateRow, ci), out var cell);
                var value = hasCell ? ResolveCellValue(cell, rr, datasets) : "";
                var colspan = hasCell ? Math.Max(1, GetInt(cell, "colspan", 1)) : 1;
                var rowspan = hasCell ? Math.Max(1, GetInt(cell, "rowspan", 1)) : 1;
                if (rr.DataIndex != null) rowspan = 1;   // 数据扩展行不支持跨行合并

                var style = ReadCellStyle(cell, hasCell);
                if (rowHeight > 0) style += $"height:{rowHeight}px;";
                // 斑马纹：偶数数据行加浅灰底（单元格自带背景色时优先生效）
                if (rr.Zebra && rr.RowNumber % 2 == 0 && (!hasCell || !HasBgColor(cell)))
                    style += "background-color:#f5f7fa;";
                if (colspan > 1) sb.Append($"<td colspan='{colspan}' style='{style}'>");
                else sb.Append($"<td style='{style}'>");
                sb.Append(value);
                sb.AppendLine("</td>");

                for (int dc = 1; dc < colspan; dc++) used.Add(ci + dc);
                if (rr.DataIndex == null)
                    for (int dr = 0; dr < rowspan; dr++)
                        for (int dc = 0; dc < colspan; dc++)
                            if (dr > 0 || dc > 0)
                                templateUsed.Add((rr.TemplateRow + dr, ci + dc));
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</table>");
        if (pageBg != null) sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>物化渲染行：数据行按数据集行数扩展，其余行渲染一次。</summary>
    private static List<MaterializedRow> MaterializeRows(JsonElement root, Dictionary<string, DataTable> datasets)
    {
        var result = new List<MaterializedRow>();
        if (!root.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array)
            return result;

        for (int ri = 0; ri < rowsEl.GetArrayLength(); ri++)
        {
            var rowEl = rowsEl[ri];
            var type = rowEl.TryGetProperty("type", out var rtEl) ? rtEl.GetString() ?? "" : "";
            var datasetId = rowEl.TryGetProperty("dataset", out var dEl) ? dEl.GetString() ?? "" : "";
            var expand = rowEl.TryGetProperty("expand", out var eEl) ? eEl.GetString() ?? "" : "";

            var dt = !string.IsNullOrEmpty(datasetId) && datasets.TryGetValue(datasetId, out var v) ? v : null;

            var zebra = rowEl.TryGetProperty("zebra", out var zEl) && zEl.ValueKind == JsonValueKind.True;

            if (dt != null && (expand == "auto" || type == "data"))
            {
                // 数据扩展行：每条数据渲染一行
                for (int di = 0; di < dt.Rows.Count; di++)
                    result.Add(new MaterializedRow { TemplateRow = ri, DataIndex = di, RowNumber = di + 1, Dataset = dt, Zebra = zebra });
            }
            else
            {
                // 静态行（标题/表头/汇总/无数据集）：渲染一次
                var dataIdx = dt != null && dt.Rows.Count > 0 ? 0 : (int?)null;
                result.Add(new MaterializedRow
                {
                    TemplateRow = ri,
                    DataIndex = dataIdx,
                    RowNumber = 0,
                    Dataset = dataIdx.HasValue ? dt : null
                });
            }
        }
        return result;
    }

    /// <summary>解析单元格值：${字段} / ${数据集.字段} / ${字段:聚合} / ${rowIndex} 占位符 + =公式。</summary>
    private static string ResolveCellValue(JsonElement cell, MaterializedRow rr, Dictionary<string, DataTable> datasets)
    {
        var raw = cell.TryGetProperty("value", out var vEl) ? vEl.GetString() ?? "" : "";
        if (string.IsNullOrEmpty(raw)) return "";

        var value = raw;
        if (value.Contains("${"))
            value = Regex.Replace(value, @"\$\{([^}]+)\}", m => ResolvePlaceholder(m.Groups[1].Value.Trim(), rr, datasets));

        // 公式 =xxx（数据行用当前行字段值参与计算，静态/汇总行无数据上下文）
        if (value.StartsWith("="))
        {
            try
            {
                if (rr.Dataset != null && rr.DataIndex.HasValue)
                {
                    var cellVals = new Dictionary<string, object?>();
                    for (int c = 0; c < rr.Dataset.Columns.Count; c++)
                        cellVals[$"${rr.Dataset.Columns[c].ColumnName}"] = rr.Dataset.Rows[rr.DataIndex.Value][c];
                    cellVals["_rowIndex"] = rr.DataIndex.Value;
                    value = FormulaEngine.Eval(value, cellVals, cellVals)?.ToString() ?? "";
                }
                else
                {
                    value = FormulaEngine.Eval(value)?.ToString() ?? "";
                }
            }
            catch { value = "#ERR"; }
        }
        return value;
    }

    /// <summary>解析单个占位符：支持 [数据集.]字段[:SUM|AVG|MIN|MAX|COUNT] 与 rowIndex。</summary>
    private static string ResolvePlaceholder(string body, MaterializedRow rr, Dictionary<string, DataTable> datasets)
    {
        var field = body;
        var agg = "";
        var colonIdx = field.LastIndexOf(':');
        if (colonIdx > 0)
        {
            var cand = field[(colonIdx + 1)..].ToUpperInvariant();
            if (cand is "SUM" or "AVG" or "MIN" or "MAX" or "COUNT")
            {
                agg = cand;
                field = field[..colonIdx];
            }
        }

        string? dsName = null;
        var dotIdx = field.LastIndexOf('.');
        if (dotIdx > 0)
        {
            dsName = field[..dotIdx];
            field = field[(dotIdx + 1)..];
        }

        if (dsName == null && field.Equals("rowIndex", StringComparison.OrdinalIgnoreCase))
            return rr.DataIndex.HasValue ? rr.RowNumber.ToString() : "";

        var dt = rr.Dataset;
        if (dsName != null)
        {
            if (!datasets.TryGetValue(dsName, out dt) || dt == null)
            {
                // 兼容数据集名称的大小写/命名差异，按名称忽略大小写再匹配一次
                dt = datasets.FirstOrDefault(kv => string.Equals(kv.Key, dsName, StringComparison.OrdinalIgnoreCase)).Value;
                if (dt == null) return "";
            }
        }
        if (dt == null || !dt.Columns.Contains(field)) return "";

        if (agg.Length > 0) return AggregateValue(dt, field, agg);

        if (rr.DataIndex.HasValue && ReferenceEquals(dt, rr.Dataset))
            return dt.Rows[rr.DataIndex.Value][field]?.ToString() ?? "";
        // 引用其它数据集 → 取第 0 行
        return dt.Rows.Count > 0 ? dt.Rows[0][field]?.ToString() ?? "" : "";
    }

    /// <summary>列聚合：SUM/AVG/MIN/MAX/COUNT（数值/日期/字符串自适应）。</summary>
    private static string AggregateValue(DataTable dt, string field, string agg)
    {
        var values = new List<object?>();
        foreach (System.Data.DataRow r in dt.Rows)
        {
            var v = r[field];
            if (v != null && v != DBNull.Value) values.Add(v);
        }
        if (agg == "COUNT") return values.Count.ToString();
        if (values.Count == 0) return "";

        // 数值聚合
        if (values.All(v => v is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
                || (v is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))))
        {
            var nums = values.Select(v => v is string s
                ? double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)
                : Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            var result = agg switch
            {
                "SUM" => nums.Sum(),
                "AVG" => nums.Average(),
                "MIN" => nums.Min(),
                "MAX" => nums.Max(),
                _ => 0d
            };
            return FormatNum(result);
        }

        // 日期聚合
        if (values.All(v => v is DateTime || (v is string s && DateTime.TryParse(s, out _))))
        {
            var dates = values.Select(v => v is DateTime d ? d : DateTime.Parse(v!.ToString()!, System.Globalization.CultureInfo.InvariantCulture)).ToList();
            return agg switch
            {
                "MIN" => dates.Min().ToString("yyyy-MM-dd HH:mm:ss"),
                "MAX" => dates.Max().ToString("yyyy-MM-dd HH:mm:ss"),
                _ => ""
            };
        }

        // 字符串聚合（MIN/MAX）
        if (agg is "MIN" or "MAX")
        {
            var strs = values.Select(v => v?.ToString() ?? "").ToList();
            return agg == "MIN" ? strs.Min()! : strs.Max()!;
        }
        return "";
    }

    private static string FormatNum(double d)
        => d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e15
            ? ((long)d).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>构建模板单元格 lookup（模板行号, 列号）→ 单元格定义。</summary>
    private static Dictionary<(int r, int c), JsonElement> BuildCellMap(JsonElement root)
    {
        var map = new Dictionary<(int r, int c), JsonElement>();
        if (root.TryGetProperty("cells", out var cellsEl) && cellsEl.ValueKind == JsonValueKind.Array)
            foreach (var c in cellsEl.EnumerateArray())
            {
                var r = c.TryGetProperty("r", out var re) ? re.GetInt32() : 0;
                var cc = c.TryGetProperty("c", out var ce) ? ce.GetInt32() : 0;
                map[(r, cc)] = c;
            }
        return map;
    }

    private static int GetColCount(JsonElement root)
        => root.TryGetProperty("cols", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array ? colsEl.GetArrayLength() : 0;

    /// <summary>读取模板行的行高（px），无设置为 0。</summary>
    private static int GetRowHeight(JsonElement root, int rowIndex)
    {
        if (root.TryGetProperty("rows", out var rowsEl) && rowsEl.ValueKind == JsonValueKind.Array
            && rowIndex >= 0 && rowIndex < rowsEl.GetArrayLength())
        {
            var rowEl = rowsEl[rowIndex];
            if (rowEl.TryGetProperty("height", out var he) && he.ValueKind == JsonValueKind.Number)
                return Math.Max(0, he.GetInt32());
        }
        return 0;
    }

    private static int GetInt(JsonElement el, string prop, int def)
        => el.TryGetProperty(prop, out var v) ? Math.Max(1, v.GetInt32()) : def;

    /// <summary>读取单元格样式为 HTML 内联样式（含默认边框处理）。</summary>
    private static string ReadCellStyle(JsonElement cell, bool hasCell)
    {
        var hasBorderProp = hasCell && cell.TryGetProperty("style", out var sEl) && sEl.TryGetProperty("border", out _);
        // 未显式设置边框时给默认细边框；设置了（false/对象）则由下面逻辑接管
        var style = hasBorderProp ? "" : "border:1px solid #ccc;";
        if (hasCell && cell.TryGetProperty("style", out sEl))
        {
            if (sEl.TryGetProperty("bold", out var b) && b.ValueKind == JsonValueKind.True) style += "font-weight:bold;";
            if (sEl.TryGetProperty("italic", out var it) && it.ValueKind == JsonValueKind.True) style += "font-style:italic;";
            if (sEl.TryGetProperty("underline", out var un) && un.ValueKind == JsonValueKind.True) style += "text-decoration:underline;";
            if (sEl.TryGetProperty("fontSize", out var fs) && fs.ValueKind == JsonValueKind.Number) style += $"font-size:{fs.GetInt32()}px;";
            if (sEl.TryGetProperty("fontName", out var fn) && fn.ValueKind == JsonValueKind.String) style += $"font-family:{fn.GetString()};";
            if (sEl.TryGetProperty("color", out var cl) && cl.ValueKind == JsonValueKind.String) style += $"color:{cl.GetString()};";
            if (sEl.TryGetProperty("bgColor", out var bg) && bg.ValueKind == JsonValueKind.String) style += $"background-color:{bg.GetString()};";
            if (sEl.TryGetProperty("align", out var al) && al.ValueKind == JsonValueKind.String) style += $"text-align:{al.GetString()};";

            // 边框：对象=分边(上/下/左/右)/粗细/颜色/线型/交叉斜线；false=无边框
            if (sEl.TryGetProperty("border", out var bdEl) && bdEl.ValueKind == JsonValueKind.Object)
            {
                var bw = GetInt(bdEl, "width", 1);
                var bc = bdEl.TryGetProperty("color", out var bce) && bce.ValueKind == JsonValueKind.String ? bce.GetString() : "#333";
                var bs = bdEl.TryGetProperty("style", out var bse) && bse.ValueKind == JsonValueKind.String ? bse.GetString() : "solid";
                var bt = bdEl.TryGetProperty("top", out var bte) && bte.ValueKind == JsonValueKind.True;
                var bb = bdEl.TryGetProperty("bottom", out var bbe) && bbe.ValueKind == JsonValueKind.True;
                var bl = bdEl.TryGetProperty("left", out var ble) && ble.ValueKind == JsonValueKind.True;
                var br = bdEl.TryGetProperty("right", out var bre) && bre.ValueKind == JsonValueKind.True;
                if (bt) style += $"border-top:{bw}px {bs} {bc};";
                if (bb) style += $"border-bottom:{bw}px {bs} {bc};";
                if (bl) style += $"border-left:{bw}px {bs} {bc};";
                if (br) style += $"border-right:{bw}px {bs} {bc};";
                // 交叉斜线：两条对角渐变
                if (bdEl.TryGetProperty("diagonal", out var bdD) && bdD.ValueKind == JsonValueKind.True)
                {
                    var half = bw * 0.5;
                    style += $"background-image:linear-gradient(to top right,transparent calc(50% - {half}px),{bc},transparent calc(50% + {half}px)),linear-gradient(to bottom right,transparent calc(50% - {half}px),{bc},transparent calc(50% + {half}px));";
                }
            }

            // 单元格背景图
            if (sEl.TryGetProperty("bgImage", out var bgi) && bgi.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(bgi.GetString()))
            {
                var size = sEl.TryGetProperty("bgImageSize", out var bis) && bis.ValueKind == JsonValueKind.String ? bis.GetString() : "cover";
                style += $"background-image:url('{bgi.GetString()}');background-size:{size};background-position:center;background-repeat:no-repeat;";
            }
        }
        return style;
    }

    /// <summary>一次渲染输出行（HTML/Excel 共用）。</summary>
    private class MaterializedRow
    {
        public int TemplateRow { get; set; }          // 模板行号（用于取单元格定义/样式）
        public int? DataIndex { get; set; }           // 数据集行索引（null=静态行）
        public int RowNumber { get; set; }            // 数据行序号（1-based，静态行为 0）
        public DataTable? Dataset { get; set; }       // 本行绑定的数据集
        public bool Zebra { get; set; }               // 斑马纹：偶数数据行浅灰底
    }

    /// <summary>单元格是否显式设置了背景色。</summary>
    private static bool HasBgColor(JsonElement cell)
        => cell.TryGetProperty("style", out var sEl)
           && sEl.TryGetProperty("bgColor", out var bg)
           && bg.ValueKind == JsonValueKind.String;

    /// <summary>导出 Excel (.xlsx) — ClosedXML（与 HTML 同一套扩展/聚合/占位符逻辑）</summary>
    public async Task<byte[]> ExportExcelAsync(string layoutJson, Dictionary<string, object?>? queryParams = null)
    {
        using var doc = JsonDocument.Parse(layoutJson);
        var root = doc.RootElement;
        var datasets = await ResolveDatasetsAsync(root, queryParams);
        return ExportExcelCore(root, datasets);
    }

    private static byte[] ExportExcelCore(JsonElement root, Dictionary<string, DataTable> datasets)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");
        var cellMap = BuildCellMap(root);
        var maxCols = GetColCount(root);
        var materialized = MaterializeRows(root, datasets);
        var templateUsed = new HashSet<(int r, int c)>();

        int outRow = 0;
        foreach (var rr in materialized)
        {
            // 应用设计器的行高设置
            var rowHeight = GetRowHeight(root, rr.TemplateRow);
            if (rowHeight > 0) ws.Row(outRow + 1).Height = rowHeight;
            var used = new HashSet<int>();
            for (int ci = 0; ci < maxCols; ci++)
            {
                if (used.Contains(ci)) continue;
                if (rr.DataIndex == null && templateUsed.Contains((rr.TemplateRow, ci))) continue;
                if (!cellMap.TryGetValue((rr.TemplateRow, ci), out var cell)) continue;   // Excel 只写有定义的单元格

                var value = ResolveCellValue(cell, rr, datasets);
                var colspan = Math.Max(1, GetInt(cell, "colspan", 1));
                var rowspan = Math.Max(1, GetInt(cell, "rowspan", 1));
                if (rr.DataIndex != null) rowspan = 1;

                var xlCell = ws.Cell(outRow + 1, ci + 1);
                xlCell.Value = value;

                if (colspan > 1 || rowspan > 1)
                    ws.Range(outRow + 1, ci + 1, outRow + rowspan, ci + colspan).Merge();

                if (cell.TryGetProperty("style", out var se))
                {
                    if (se.TryGetProperty("bold", out var b) && b.ValueKind == JsonValueKind.True) xlCell.Style.Font.Bold = true;
                    if (se.TryGetProperty("italic", out var it) && it.ValueKind == JsonValueKind.True) xlCell.Style.Font.Italic = true;
                    if (se.TryGetProperty("underline", out var un) && un.ValueKind == JsonValueKind.True) xlCell.Style.Font.Underline = XLFontUnderlineValues.Single;
                    if (se.TryGetProperty("fontSize", out var fs) && fs.ValueKind == JsonValueKind.Number) xlCell.Style.Font.FontSize = fs.GetDouble();
                    if (se.TryGetProperty("fontName", out var fn) && fn.ValueKind == JsonValueKind.String) xlCell.Style.Font.FontName = fn.GetString()!;
                    if (se.TryGetProperty("color", out var cl) && cl.ValueKind == JsonValueKind.String) xlCell.Style.Font.FontColor = XLColor.FromHtml(cl.GetString()!);
                    if (se.TryGetProperty("bgColor", out var bg) && bg.ValueKind == JsonValueKind.String) xlCell.Style.Fill.BackgroundColor = XLColor.FromHtml(bg.GetString()!);
                    if (se.TryGetProperty("align", out var al) && al.ValueKind == JsonValueKind.String)
                        xlCell.Style.Alignment.Horizontal = al.GetString() switch
                        {
                            "center" => XLAlignmentHorizontalValues.Center,
                            "right" => XLAlignmentHorizontalValues.Right,
                            _ => XLAlignmentHorizontalValues.Left
                        };
                    // 边框：false=无；对象=分边/粗细/颜色/线型/交叉斜线；true=旧格式外框细线
                    if (se.TryGetProperty("border", out var bdEl))
                    {
                        if (bdEl.ValueKind == JsonValueKind.False)
                        {
                            xlCell.Style.Border.OutsideBorder = XLBorderStyleValues.None;
                            xlCell.Style.Border.InsideBorder = XLBorderStyleValues.None;
                        }
                        else if (bdEl.ValueKind == JsonValueKind.Object)
                        {
                            var bw = GetInt(bdEl, "width", 1);
                            var bc = bdEl.TryGetProperty("color", out var bce) && bce.ValueKind == JsonValueKind.String ? bce.GetString() : "#333";
                            var bs = bdEl.TryGetProperty("style", out var bse) && bse.ValueKind == JsonValueKind.String ? bse.GetString() : "solid";
                            var lineStyle = bw >= 2 ? XLBorderStyleValues.Medium : XLBorderStyleValues.Thin;
                            if (bs == "dashed") lineStyle = XLBorderStyleValues.Dashed;
                            else if (bs == "dotted") lineStyle = XLBorderStyleValues.Dotted;
                            else if (bs == "double") lineStyle = XLBorderStyleValues.Double;
                            var xlColor = XLColor.FromHtml(bc!);
                            var xlBorder = xlCell.Style.Border;
                            if (bdEl.TryGetProperty("top", out var bte) && bte.ValueKind == JsonValueKind.True) { xlBorder.TopBorder = lineStyle; xlBorder.TopBorderColor = xlColor; }
                            if (bdEl.TryGetProperty("bottom", out var bbe) && bbe.ValueKind == JsonValueKind.True) { xlBorder.BottomBorder = lineStyle; xlBorder.BottomBorderColor = xlColor; }
                            if (bdEl.TryGetProperty("left", out var ble) && ble.ValueKind == JsonValueKind.True) { xlBorder.LeftBorder = lineStyle; xlBorder.LeftBorderColor = xlColor; }
                            if (bdEl.TryGetProperty("right", out var bre) && bre.ValueKind == JsonValueKind.True) { xlBorder.RightBorder = lineStyle; xlBorder.RightBorderColor = xlColor; }
                            if (bdEl.TryGetProperty("diagonal", out var bdD) && bdD.ValueKind == JsonValueKind.True)
                            {
                                xlBorder.DiagonalBorder = lineStyle;
                                xlBorder.DiagonalBorderColor = xlColor;
                                xlBorder.DiagonalUp = true;
                                xlBorder.DiagonalDown = true;
                            }
                        }
                        else if (bdEl.ValueKind != JsonValueKind.False)
                        {
                            xlCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        }
                    }
                }

                // 斑马纹：偶数数据行浅灰底（单元格自带背景色时优先生效）
                if (rr.Zebra && rr.RowNumber % 2 == 0 && !HasBgColor(cell))
                    xlCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f5f7fa");

                for (int dc = 1; dc < colspan; dc++) used.Add(ci + dc);
                if (rr.DataIndex == null)
                    for (int dr = 0; dr < rowspan; dr++)
                        for (int dc = 0; dc < colspan; dc++)
                            if (dr > 0 || dc > 0)
                                templateUsed.Add((rr.TemplateRow + dr, ci + dc));
            }
            outRow++;
        }

        // 列宽
        if (root.TryGetProperty("cols", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            int ci = 0;
            foreach (var col in colsEl.EnumerateArray())
            {
                var w = col.TryGetProperty("width", out var we) ? we.GetInt32() : 100;
                ws.Column(ci + 1).Width = w / 7.0; // px → character width approx
                ci++;
            }
        }

        using var ms = new System.IO.MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>导出 PDF — 基于 HTML 渲染 + QuestPDF 嵌入（降级为 HTML 输出供浏览器打印）</summary>
    public async Task<byte[]> ExportPdfAsync(string layoutJson, Dictionary<string, object?>? queryParams = null)
    {
        var html = await RenderToHtml(layoutJson, queryParams);
        return Encoding.UTF8.GetBytes(html);
    }

    // ===== Data Source Helpers =====

    private async Task<DataTable?> ResolveSource(string sourceType, string sourceRef, string customSql,
        long dataSourceId, Dictionary<string, object?>? queryParams)
    {
        switch (sourceType)
        {
            case "dataview":
                if (long.TryParse(sourceRef, out var dvId))
                {
                    var dv = await _db.Set<Domain.Entities.DataViewEntity>().FindAsync(dvId);
                    if (dv != null && !string.IsNullOrEmpty(dv.Sql))
                        return await ExecuteQuery(dv.DataSourceId, dv.Sql, queryParams);
                }
                break;
            case "sql":
                if (!string.IsNullOrEmpty(customSql))
                {
                    // 优先使用数据集保存的数据源；旧数据未保存 dataSourceId 时回退到第一个数据源
                    var dsEntity = dataSourceId > 0
                        ? await _db.DataSources.FirstOrDefaultAsync(d => d.Deleted == 0 && d.Id == dataSourceId)
                        : await _db.DataSources.FirstOrDefaultAsync(d => d.Deleted == 0);
                    if (dsEntity != null)
                        return await ExecuteQuery(dsEntity.Id, customSql, queryParams);
                }
                break;
        }
        return null;
    }

    // ===== Connection Factory (reuses MysqlNodeExecutor pattern) =====

    private static IDbConnection CreateConnection(string dsType, string connStr) => dsType switch
    {
        "sqlite" => new SqliteConnection(connStr),
        "mysql" => new MySqlConnection(connStr),
        "postgresql" or "postgres" => new NpgsqlConnection(connStr),
        "sqlserver" or "mssql" => new Microsoft.Data.SqlClient.SqlConnection(connStr),
        _ => throw new Exception($"Unsupported DB: {dsType}")
    };

    private static IDbDataParameter CreateParameter(string name, object? value, string dsType)
    {
        return dsType switch
        {
            "sqlite" => new SqliteParameter($"@{name}", value ?? DBNull.Value),
            "mysql" => new MySqlParameter($"@{name}", value ?? DBNull.Value),
            "postgresql" or "postgres" => new NpgsqlParameter($"@{name}", value ?? DBNull.Value),
            "sqlserver" or "mssql" => new Microsoft.Data.SqlClient.SqlParameter($"@{name}", value ?? DBNull.Value),
            _ => new SqliteParameter($"@{name}", value ?? DBNull.Value)
        };
    }

    private static string BuildConnectionString(Domain.Entities.DataSourceEntity ds) => ds.DsType?.ToLower() switch
    {
        "sqlite" => $"Data Source={ds.DbName ?? "juggle.db"}",
        "mysql" => $"Server={ds.Host};Port={ds.Port};Database={ds.DbName};User={ds.Username};Password={ds.Password};CharSet=utf8mb4;",
        "postgresql" or "postgres" => $"Host={ds.Host};Port={ds.Port};Database={ds.DbName};Username={ds.Username};Password={ds.Password};No Reset On Close=true;",
        "sqlserver" or "mssql" => $"Server={ds.Host},{ds.Port};Database={ds.DbName};User Id={ds.Username};Password={ds.Password};TrustServerCertificate=True;",
        _ => throw new Exception($"Unsupported DB: {ds.DsType}")
    };
}
