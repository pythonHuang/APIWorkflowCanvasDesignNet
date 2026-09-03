using System.Data;
using System.Text;
using System.Text.Json;
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

        // 执行所有数据集
        var datasets = new Dictionary<string, DataTable>();
        if (root.TryGetProperty("datasets", out var dsArr))
        {
            foreach (var ds in dsArr.EnumerateArray())
            {
                var id = ds.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                var sourceType = ds.TryGetProperty("sourceType", out var stEl) ? stEl.GetString() ?? "" : "";
                var sourceRef = ds.TryGetProperty("sourceRef", out var srEl) ? srEl.GetString() ?? "" : "";
                var customSql = ds.TryGetProperty("customSql", out var csEl) ? csEl.GetString() ?? "" : "";

                var dt = await ResolveSource(sourceType, sourceRef, customSql, queryParams);
                if (dt != null) datasets[id] = dt;
            }
        }

        // 渲染表格
        sb.AppendLine("<table>");
        var rows = root.TryGetProperty("rows", out var rowsEl) ? rowsEl : default;
        var cols = root.TryGetProperty("cols", out var colsEl) ? colsEl : default;
        var cells = root.TryGetProperty("cells", out var cellsEl) ? cellsEl : default;

        // 构建 cells lookup
        var cellMap = new Dictionary<(int r, int c), JsonElement>();
        if (cells.ValueKind == JsonValueKind.Array)
            foreach (var c in cells.EnumerateArray())
            {
                var r = c.TryGetProperty("r", out var re) ? re.GetInt32() : 0;
                var cc = c.TryGetProperty("c", out var ce) ? ce.GetInt32() : 0;
                cellMap[(r, cc)] = c;
            }

        // 简单渲染：遍历 rows → 渲染每行
        int maxRows = rows.ValueKind == JsonValueKind.Array ? rows.GetArrayLength() : 0;
        int maxCols = cols.ValueKind == JsonValueKind.Array ? cols.GetArrayLength() : 0;
        var usedCells = new HashSet<(int, int)>();

        for (int ri = 0; ri < maxRows; ri++)
        {
            var rowEl = rows[ri];
            var rowType = rowEl.TryGetProperty("type", out var rtEl) ? rtEl.GetString() ?? "data" : "data";
            var dataset = rowEl.TryGetProperty("dataset", out var dsRefEl) ? dsRefEl.GetString() ?? "" : "";
            var expand = rowEl.TryGetProperty("expand", out var expEl) ? expEl.GetString() ?? "" : "";

            DataTable? dt = string.IsNullOrEmpty(dataset) || !datasets.ContainsKey(dataset) ? null : datasets[dataset];

            if (expand == "auto" && dt != null)
            {
                // 数据扩展行：为每行数据渲染一次
                for (int di = 0; di < dt.Rows.Count; di++)
                {
                    sb.Append("<tr>");
                    for (int ci = 0; ci < maxCols; ci++)
                    {
                        if (usedCells.Contains((ri, ci))) continue;
                        RenderCell(sb, ri, ci, cellMap, dt, di, datasets);
                    }
                    sb.AppendLine("</tr>");
                }
            }
            else if (dt != null && dt.Rows.Count > 0)
            {
                sb.Append("<tr>");
                for (int ci = 0; ci < maxCols; ci++)
                {
                    if (usedCells.Contains((ri, ci))) continue;
                    RenderCell(sb, ri, ci, cellMap, dt, 0, datasets);
                }
                sb.AppendLine("</tr>");
            }
            else
            {
                // 静态行
                sb.Append("<tr>");
                for (int ci = 0; ci < maxCols; ci++)
                {
                    if (usedCells.Contains((ri, ci))) continue;
                    RenderCell(sb, ri, ci, cellMap, null, 0, datasets);
                }
                sb.AppendLine("</tr>");
            }

            // 标记已处理的合并单元格
            foreach (var mk in cellMap.Keys.Where(k => k.r == ri))
            {
                var cell = cellMap[mk];
                var colspan = cell.TryGetProperty("colspan", out var csp) ? csp.GetInt32() : 1;
                var rowspan = cell.TryGetProperty("rowspan", out var rsp) ? rsp.GetInt32() : 1;
                for (int dr = 0; dr < rowspan; dr++)
                    for (int dc = 0; dc < colspan; dc++)
                        if (dr > 0 || dc > 0)
                            usedCells.Add((ri + dr, mk.c + dc));
            }
        }

        sb.AppendLine("</table></body></html>");
        return sb.ToString();
    }

    private static void RenderCell(StringBuilder sb, int ri, int ci,
        Dictionary<(int, int), JsonElement> cells, DataTable? dt, int dataRow,
        Dictionary<string, DataTable> datasets)
    {
        var style = "border:1px solid #ccc;";
        string value = "";
        int colspan = 1, rowspan = 1;

        if (cells.TryGetValue((ri, ci), out var cell))
        {
            value = cell.TryGetProperty("value", out var vEl) ? vEl.GetString() ?? "" : "";
            colspan = cell.TryGetProperty("colspan", out var csp) ? csp.GetInt32() : 1;
            rowspan = cell.TryGetProperty("rowspan", out var rsp) ? rsp.GetInt32() : 1;

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

            // ${field} 替换
            if (dt != null && value.Contains("${"))
            {
                foreach (System.Data.DataColumn col in dt.Columns)
                {
                    var key = $"${{{col.ColumnName}}}";
                    if (value.Contains(key))
                    {
                        var replacement = dt.Rows[dataRow][col]?.ToString() ?? "";
                        value = value.Replace(key, replacement);
                    }
                }
                value = value.Replace("${rowIndex}", (dataRow + 1).ToString());
            }

            // 公式 =xxx
            if (value.StartsWith("=") && dt != null)
            {
                try
                {
                    var cellVals = new Dictionary<string, object?>();
                    for (int c = 0; c < dt.Columns.Count; c++)
                        cellVals[$"${dt.Columns[c].ColumnName}"] = dt.Rows[dataRow][c];
                    cellVals["_rowIndex"] = dataRow;
                    value = FormulaEngine.Eval(value, cellVals, cellVals)?.ToString() ?? "";
                }
                catch { value = "#ERR"; }
            }
        }

        var tag = "td";
        if (colspan > 1) sb.Append($"<{tag} colspan='{colspan}'");
        else sb.Append($"<{tag}");
        if (rowspan > 1) sb.Append($" rowspan='{rowspan}'");
        sb.Append($" style='{style}'>");
        sb.Append(value);
        sb.AppendLine($"</{tag}>");
    }

    /// <summary>导出 Excel (.xlsx) — ClosedXML</summary>
    public byte[] ExportExcel(string layoutJson, Dictionary<string, object?>? queryParams = null)
    {
        using var doc = JsonDocument.Parse(layoutJson);
        var root = doc.RootElement;
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Sheet1");

        var cells = root.TryGetProperty("cells", out var cellsEl) ? cellsEl : default;
        var cellMap = new Dictionary<(int r, int c), JsonElement>();
        if (cells.ValueKind == JsonValueKind.Array)
            foreach (var c in cells.EnumerateArray())
            {
                var r = c.TryGetProperty("r", out var re) ? re.GetInt32() : 0;
                var cc = c.TryGetProperty("c", out var ce) ? ce.GetInt32() : 0;
                cellMap[(r, cc)] = c;
            }

        foreach (var kv in cellMap)
        {
            var (r, c) = kv.Key;
            var cell = kv.Value;
            var value = cell.TryGetProperty("value", out var ve) ? ve.GetString() ?? "" : "";
            var colspan = cell.TryGetProperty("colspan", out var csp) ? Math.Max(1, csp.GetInt32()) : 1;
            var rowspan = cell.TryGetProperty("rowspan", out var rsp) ? Math.Max(1, rsp.GetInt32()) : 1;

            var xlCell = ws.Cell(r + 1, c + 1);
            xlCell.Value = value;

            if (colspan > 1 || rowspan > 1)
            {
                var endR = r + rowspan;
                var endC = c + colspan;
                ws.Range(r + 1, c + 1, endR, endC).Merge();
            }

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
        }

        // 列宽
        var cols = root.TryGetProperty("cols", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array ? colsEl : default;
        if (cols.ValueKind == JsonValueKind.Array)
        {
            int ci = 0;
            foreach (var col in cols.EnumerateArray())
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
    public byte[] ExportPdf(string layoutJson, Dictionary<string, object?>? queryParams = null)
    {
        var html = RenderToHtml(layoutJson, queryParams).GetAwaiter().GetResult();
        return Encoding.UTF8.GetBytes(html);
    }

    // ===== Data Source Helpers =====

    private async Task<DataTable?> ResolveSource(string sourceType, string sourceRef, string customSql,
        Dictionary<string, object?>? queryParams)
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
                    // 使用第一个数据源执行自定义SQL
                    var firstDs = await _db.DataSources.FirstOrDefaultAsync(d => d.Deleted == 0);
                    if (firstDs != null)
                        return await ExecuteQuery(firstDs.Id, customSql, queryParams);
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
        "postgresql" or "postgres" => $"Host={ds.Host};Port={ds.Port};Database={ds.DbName};Username={ds.Username};Password={ds.Password};",
        "sqlserver" or "mssql" => $"Server={ds.Host},{ds.Port};Database={ds.DbName};User Id={ds.Username};Password={ds.Password};TrustServerCertificate=True;",
        _ => throw new Exception($"Unsupported DB: {ds.DsType}")
    };
}
