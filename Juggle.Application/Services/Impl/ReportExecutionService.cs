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

    /// <summary>渲染报表为HTML</summary>
    public async Task<string> RenderToHtml(string layoutJson, Dictionary<string, object?>? queryParams = null)
    {
        using var doc = JsonDocument.Parse(layoutJson);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
        sb.AppendLine("table{border-collapse:collapse;font-family:'Microsoft YaHei',sans-serif;}");
        sb.AppendLine("td{padding:4px 6px;}");
        sb.AppendLine("@media print{@page{size:A4;margin:15mm}}</style></head><body>");

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
                if (dt != null) datasets[id] = dt;
            }
        }
        return datasets;
    }

    /// <summary>HTML 渲染核心：数据行按数据集自动扩展，静态行（标题/表头/汇总）渲染一次。</summary>
    private static string RenderHtmlCore(JsonElement root, Dictionary<string, DataTable> datasets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'><style>");
        sb.AppendLine("table{border-collapse:collapse;font-family:'Microsoft YaHei',sans-serif;}");
        sb.AppendLine("td{padding:4px 6px;}");
        sb.AppendLine("@media print{@page{size:A4;margin:15mm}}</style></head><body>");

        var cellMap = BuildCellMap(root);
        var maxCols = GetColCount(root);
        var materialized = MaterializeRows(root, datasets);
        var templateUsed = new HashSet<(int r, int c)>();   // 静态行之间的跨行合并占用

        sb.AppendLine("<table>");
        foreach (var rr in materialized)
        {
            sb.Append("<tr>");
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

                var style = hasCell ? "border:1px solid #ccc;" + ReadCellStyle(cell) : "border:1px solid #ccc;";
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
        sb.AppendLine("</table></body></html>");
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

            if (dt != null && (expand == "auto" || type == "data"))
            {
                // 数据扩展行：每条数据渲染一行
                for (int di = 0; di < dt.Rows.Count; di++)
                    result.Add(new MaterializedRow { TemplateRow = ri, DataIndex = di, RowNumber = di + 1, Dataset = dt });
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
            if (!datasets.TryGetValue(dsName, out dt) || dt == null) return "";
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

    private static int GetInt(JsonElement el, string prop, int def)
        => el.TryGetProperty(prop, out var v) ? Math.Max(1, v.GetInt32()) : def;

    /// <summary>读取单元格样式为 HTML 内联样式追加内容。</summary>
    private static string ReadCellStyle(JsonElement cell)
    {
        var style = "";
        if (cell.TryGetProperty("style", out var sEl))
        {
            if (sEl.TryGetProperty("bold", out var b) && b.ValueKind == JsonValueKind.True) style += "font-weight:bold;";
            if (sEl.TryGetProperty("italic", out var it) && it.ValueKind == JsonValueKind.True) style += "font-style:italic;";
            if (sEl.TryGetProperty("fontSize", out var fs)) style += $"font-size:{fs.GetInt32()}px;";
            if (sEl.TryGetProperty("color", out var cl)) style += $"color:{cl.GetString()};";
            if (sEl.TryGetProperty("bgColor", out var bg)) style += $"background-color:{bg.GetString()};";
            if (sEl.TryGetProperty("align", out var al)) style += $"text-align:{al.GetString()};";
            if (sEl.TryGetProperty("border", out var bd) && bd.ValueKind == JsonValueKind.False) style = style.Replace("border:1px solid #ccc;", "");
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
    }

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
                    if (se.TryGetProperty("fontSize", out var fs)) xlCell.Style.Font.FontSize = fs.GetDouble();
                    if (se.TryGetProperty("color", out var cl)) xlCell.Style.Font.FontColor = XLColor.FromHtml(cl.GetString()!);
                    if (se.TryGetProperty("bgColor", out var bg)) xlCell.Style.Fill.BackgroundColor = XLColor.FromHtml(bg.GetString()!);
                    if (se.TryGetProperty("align", out var al))
                        xlCell.Style.Alignment.Horizontal = al.GetString() switch
                        {
                            "center" => XLAlignmentHorizontalValues.Center,
                            "right" => XLAlignmentHorizontalValues.Right,
                            _ => XLAlignmentHorizontalValues.Left
                        };
                    if (se.TryGetProperty("border", out var bd) && bd.ValueKind != JsonValueKind.False)
                        xlCell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

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
