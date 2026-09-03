using System.Data;
using System.Data.Common;
using System.Text.RegularExpressions;
using Juggle.Application.Services.Flow;
using Juggle.Domain.Engine.NodeExecutors;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Application.Services.Impl;

/// <summary>
/// 数据源服务 —— 统一管理连接字符串构建和连接测试逻辑，
/// 消除 DataSourceController.Test、FlowExecutionService.BuildDataSourceInfos 中的重复实现。
/// 同时提供流程设计器的 SQL 辅助能力：表/视图/存储过程元数据、字段列表、SQL 单独测试。
/// </summary>
public class DataSourceService
{
    private readonly JuggleDbContext _db;

    public DataSourceService(JuggleDbContext db) => _db = db;

    /// <summary>构建连接字符串（委托给 FlowExecutionService 的静态方法，保持单一来源）。</summary>
    public static string BuildConnectionString(DataSourceEntity ds)
        => FlowExecutionService.BuildConnectionString(ds);

    /// <summary>通过 ID 查找数据源并测试连接。</summary>
    public async Task<(bool Ok, string Message)> TestConnectionAsync(long id)
    {
        var ds = await _db.DataSources.FindAsync(id);
        if (ds == null) return (false, "数据源不存在");

        var dsInfo = BuildDataSourceInfo(ds);
        return await MysqlNodeExecutor.TestConnectionAsync(dsInfo);
    }

    /// <summary>按名称加载数据源连接信息（流程中通过名称引用数据源）。</summary>
    public async Task<DataSourceInfo> LoadDataSourceInfoAsync(string dataSourceName)
    {
        var ds = await _db.DataSources
            .FirstOrDefaultAsync(d => d.Deleted == 0 && d.DsName == dataSourceName);
        if (ds == null) throw new InvalidOperationException($"数据源 [{dataSourceName}] 未找到，请先在系统设置中配置数据源。");
        return BuildDataSourceInfo(ds);
    }

    private static DataSourceInfo BuildDataSourceInfo(DataSourceEntity ds)
    {
        return new DataSourceInfo
        {
            DsType  = (ds.DsType ?? "sqlite").ToLower(),
            ConnStr = BuildConnectionString(ds),
            DsName  = ds.DsName ?? ""
        };
    }

    // ────────────────────────────────────────────────────────────────
    // SQL 设计辅助（表/视图/存储过程元数据）
    // ────────────────────────────────────────────────────────────────

    /// <summary>获取数据源的表、视图、存储过程列表。</summary>
    public async Task<DbObjectsResult> GetDbObjectsAsync(string dataSourceName)
    {
        var dsInfo = await LoadDataSourceInfoAsync(dataSourceName);
        var result = new DbObjectsResult();
        await using var conn = MysqlNodeExecutor.CreateConnection(dsInfo);
        await conn.OpenAsync();

        switch (dsInfo.DsType.ToLower())
        {
            case "mysql":
            {
                // SHOW FULL TABLES: 第0列表名，第1列 Table_type（BASE TABLE / VIEW）
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SHOW FULL TABLES";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetValue(0)?.ToString() ?? "";
                        var type = reader.GetValue(1)?.ToString() ?? "";
                        if (type.Contains("VIEW", StringComparison.OrdinalIgnoreCase))
                            result.Views.Add(new DbObjectItem { Name = name });
                        else
                            result.Tables.Add(new DbObjectItem { Name = name });
                    }
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SHOW PROCEDURE STATUS WHERE Db = DATABASE()";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        result.Procedures.Add(new DbObjectItem { Name = reader["Name"]?.ToString() ?? "" });
                }
                break;
            }
            case "sqlite":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') AND name NOT LIKE 'sqlite_%' ORDER BY name";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var name = reader.GetValue(0)?.ToString() ?? "";
                    var type = reader.GetValue(1)?.ToString() ?? "";
                    if (type == "view") result.Views.Add(new DbObjectItem { Name = name });
                    else result.Tables.Add(new DbObjectItem { Name = name });
                }
                break;
            }
            case "postgresql" or "postgres":
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT table_name, table_type FROM information_schema.tables WHERE table_schema NOT IN ('pg_catalog','information_schema') ORDER BY table_name";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetValue(0)?.ToString() ?? "";
                        var type = reader.GetValue(1)?.ToString() ?? "";
                        if (type == "VIEW") result.Views.Add(new DbObjectItem { Name = name });
                        else result.Tables.Add(new DbObjectItem { Name = name });
                    }
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT routine_name FROM information_schema.routines WHERE routine_type = 'PROCEDURE' AND routine_schema NOT IN ('pg_catalog','information_schema') ORDER BY routine_name";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        result.Procedures.Add(new DbObjectItem { Name = reader.GetValue(0)?.ToString() ?? "" });
                }
                break;
            }
            case "sqlserver" or "mssql":
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT TABLE_NAME, TABLE_TYPE FROM INFORMATION_SCHEMA.TABLES ORDER BY TABLE_NAME";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetValue(0)?.ToString() ?? "";
                        var type = reader.GetValue(1)?.ToString() ?? "";
                        if (type == "VIEW") result.Views.Add(new DbObjectItem { Name = name });
                        else result.Tables.Add(new DbObjectItem { Name = name });
                    }
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT name FROM sys.procedures ORDER BY name";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        result.Procedures.Add(new DbObjectItem { Name = reader.GetValue(0)?.ToString() ?? "" });
                }
                break;
            }
            case "oracle":
            case "dm":   // 达梦兼容 Oracle 数据字典
            {
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT table_name FROM user_tables ORDER BY table_name";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        result.Tables.Add(new DbObjectItem { Name = reader.GetValue(0)?.ToString() ?? "" });
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT view_name FROM user_views ORDER BY view_name";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        result.Views.Add(new DbObjectItem { Name = reader.GetValue(0)?.ToString() ?? "" });
                }
                await using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT object_name FROM user_objects WHERE object_type = 'PROCEDURE' ORDER BY object_name";
                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                        result.Procedures.Add(new DbObjectItem { Name = reader.GetValue(0)?.ToString() ?? "" });
                }
                break;
            }
            default:
                throw new InvalidOperationException($"不支持的数据库类型: {dsInfo.DsType}");
        }

        return result;
    }

    /// <summary>
    /// 获取表/视图的字段列表（含中文注释、默认值、是否必填），按数据库类型适配元数据查询。
    /// </summary>
    public async Task<List<DbColumnItem>> GetColumnsAsync(string dataSourceName, string tableName)
    {
        var dsInfo = await LoadDataSourceInfoAsync(dataSourceName);
        var columns = new List<DbColumnItem>();
        var safeName = tableName.Replace("'", "''");
        await using var conn = MysqlNodeExecutor.CreateConnection(dsInfo);
        await conn.OpenAsync();

        switch (dsInfo.DsType.ToLower())
        {
            case "mysql":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SHOW FULL COLUMNS FROM {tableName}";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(new DbColumnItem
                    {
                        Name         = reader["Field"]?.ToString() ?? "",
                        DataType     = reader["Type"]?.ToString() ?? "",
                        IsNullable   = string.Equals(reader["Null"]?.ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = reader["Default"]?.ToString(),
                        Comment      = reader["Comment"]?.ToString() ?? ""
                    });
                }
                break;
            }
            case "sqlite":
            {
                // PRAGMA table_info: cid/name/type/notnull/dflt_value/pk（SQLite 无注释）
                // 注意: INTEGER PRIMARY KEY 的 notnull 恒为 0，需按主键列补判
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"PRAGMA table_info('{safeName}')";
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var notNull = Convert.ToInt64(reader.GetValue(3)) == 1;
                    var isPk = Convert.ToInt64(reader.GetValue(5)) > 0;
                    columns.Add(new DbColumnItem
                    {
                        Name         = reader.GetValue(1)?.ToString() ?? "",
                        DataType     = reader.GetValue(2)?.ToString() ?? "",
                        IsNullable   = !notNull && !isPk,
                        DefaultValue = reader.IsDBNull(4) ? null : reader.GetValue(4)?.ToString(),
                        Comment      = ""
                    });
                }
                break;
            }
            case "postgresql" or "postgres":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT c.column_name, c.data_type, c.is_nullable, c.column_default,
                           pg_catalog.col_description(format('%I.%I', c.table_schema, c.table_name)::regclass, c.ordinal_position) AS comment
                    FROM information_schema.columns c
                    WHERE c.table_schema NOT IN ('pg_catalog', 'information_schema') AND c.table_name = '{safeName}'
                    ORDER BY c.ordinal_position
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(new DbColumnItem
                    {
                        Name         = reader.GetValue(0)?.ToString() ?? "",
                        DataType     = reader.GetValue(1)?.ToString() ?? "",
                        IsNullable   = string.Equals(reader.GetValue(2)?.ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                        Comment      = reader.IsDBNull(4) ? "" : reader.GetValue(4)?.ToString() ?? ""
                    });
                }
                break;
            }
            case "sqlserver" or "mssql":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, c.COLUMN_DEFAULT,
                           CAST(ep.value AS NVARCHAR(500)) AS comment
                    FROM INFORMATION_SCHEMA.COLUMNS c
                    LEFT JOIN sys.extended_properties ep
                      ON ep.major_id = OBJECT_ID(c.TABLE_SCHEMA + '.' + c.TABLE_NAME)
                     AND ep.minor_id = c.ORDINAL_POSITION
                     AND ep.class = 1 AND ep.name = 'MS_Description'
                    WHERE c.TABLE_NAME = '{safeName}'
                    ORDER BY c.ORDINAL_POSITION
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(new DbColumnItem
                    {
                        Name         = reader.GetValue(0)?.ToString() ?? "",
                        DataType     = reader.GetValue(1)?.ToString() ?? "",
                        IsNullable   = string.Equals(reader.GetValue(2)?.ToString(), "YES", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                        Comment      = reader.IsDBNull(4) ? "" : reader.GetValue(4)?.ToString() ?? ""
                    });
                }
                break;
            }
            case "oracle":
            case "dm":   // 达梦兼容 Oracle 数据字典
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT c.COLUMN_NAME, c.DATA_TYPE, c.NULLABLE, c.DATA_DEFAULT, cc.COMMENTS AS comment
                    FROM USER_TAB_COLUMNS c
                    LEFT JOIN USER_COL_COMMENTS cc ON cc.TABLE_NAME = c.TABLE_NAME AND cc.COLUMN_NAME = c.COLUMN_NAME
                    WHERE c.TABLE_NAME = '{safeName.ToUpper()}'
                    ORDER BY c.COLUMN_ID
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    columns.Add(new DbColumnItem
                    {
                        Name         = reader.GetValue(0)?.ToString() ?? "",
                        DataType     = reader.GetValue(1)?.ToString() ?? "",
                        IsNullable   = string.Equals(reader.GetValue(2)?.ToString(), "Y", StringComparison.OrdinalIgnoreCase),
                        DefaultValue = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString(),
                        Comment      = reader.IsDBNull(4) ? "" : reader.GetValue(4)?.ToString() ?? ""
                    });
                }
                break;
            }
            default:
                throw new InvalidOperationException($"不支持的数据库类型: {dsInfo.DsType}");
        }

        return columns;
    }

    /// <summary>获取存储过程的参数列表（参数名/类型/模式/默认值），按数据库类型适配。</summary>
    public async Task<List<DbProcParamItem>> GetProcedureParamsAsync(string dataSourceName, string procName)
    {
        var dsInfo = await LoadDataSourceInfoAsync(dataSourceName);
        var safeName = procName.Replace("'", "''");
        var @params = new List<DbProcParamItem>();
        await using var conn = MysqlNodeExecutor.CreateConnection(dsInfo);
        await conn.OpenAsync();

        switch (dsInfo.DsType.ToLower())
        {
            case "mysql":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT PARAMETER_NAME, DATA_TYPE, PARAMETER_MODE
                    FROM information_schema.parameters
                    WHERE SPECIFIC_NAME = '{safeName}' AND ROUTINE_TYPE = 'PROCEDURE'
                    ORDER BY ORDINAL_POSITION
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    @params.Add(new DbProcParamItem
                    {
                        Name     = reader.GetValue(0)?.ToString() ?? "",
                        DataType = reader.GetValue(1)?.ToString() ?? "",
                        Mode     = reader.GetValue(2)?.ToString() ?? "IN"
                    });
                }
                break;
            }
            case "postgresql" or "postgres":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT parameter_name, data_type, parameter_mode
                    FROM information_schema.parameters
                    WHERE specific_name = '{safeName}'
                    ORDER BY ordinal_position
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    @params.Add(new DbProcParamItem
                    {
                        Name     = reader.GetValue(0)?.ToString() ?? "",
                        DataType = reader.GetValue(1)?.ToString() ?? "",
                        Mode     = reader.GetValue(2)?.ToString() ?? "IN"
                    });
                }
                break;
            }
            case "sqlserver" or "mssql":
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT p.name, t.name AS data_type,
                           CASE WHEN p.has_default_value = 1 THEN CAST(p.default_value AS NVARCHAR(200)) ELSE NULL END AS default_value,
                           CASE WHEN p.is_output = 1 THEN 'OUT' ELSE 'IN' END AS mode
                    FROM sys.procedures sp
                    JOIN sys.parameters p ON sp.object_id = p.object_id
                    JOIN sys.types t ON p.user_type_id = t.user_type_id
                    WHERE sp.name = '{safeName}' AND p.parameter_id > 0
                    ORDER BY p.parameter_id
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    @params.Add(new DbProcParamItem
                    {
                        Name         = reader.GetValue(0)?.ToString() ?? "",
                        DataType     = reader.GetValue(1)?.ToString() ?? "",
                        DefaultValue = reader.IsDBNull(2) ? null : reader.GetValue(2)?.ToString(),
                        Mode         = reader.GetValue(3)?.ToString() ?? "IN"
                    });
                }
                break;
            }
            case "oracle":
            case "dm":
            {
                // SUBSTR 兼容 LONG 类型的 DEFAULT_VALUE
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"""
                    SELECT ARGUMENT_NAME, DATA_TYPE, IN_OUT, SUBSTR(DEFAULT_VALUE, 1, 200)
                    FROM USER_ARGUMENTS
                    WHERE OBJECT_NAME = '{safeName.ToUpper()}' AND ARGUMENT_NAME IS NOT NULL
                    ORDER BY POSITION
                    """;
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    @params.Add(new DbProcParamItem
                    {
                        Name         = reader.GetValue(0)?.ToString() ?? "",
                        DataType     = reader.GetValue(1)?.ToString() ?? "",
                        Mode         = reader.GetValue(2)?.ToString() ?? "IN",
                        DefaultValue = reader.IsDBNull(3) ? null : reader.GetValue(3)?.ToString()
                    });
                }
                break;
            }
            default:
                // SQLite 等无存储过程
                break;
        }

        return @params;
    }

    // ────────────────────────────────────────────────────────────────
    // SQL 单独测试
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 单独测试 SQL：QUERY 返回前 100 行预览；UPDATE 在事务中执行并回滚（不影响真实数据）。
    /// 模板变量 ${varName} 使用 vars 提供值，未提供的变量替换为空字符串。
    /// </summary>
    public async Task<DbTestSqlResult> TestSqlAsync(string dataSourceName, string sql, string operationType, Dictionary<string, string?>? vars)
    {
        var dsInfo = await LoadDataSourceInfoAsync(dataSourceName);
        var rendered = RenderTemplate(sql, vars);
        await using var conn = MysqlNodeExecutor.CreateConnection(dsInfo);
        await conn.OpenAsync();

        if (operationType?.ToUpper() == "UPDATE")
        {
            // 更改操作：事务执行后回滚，保证测试不污染数据
            await using var tx = await conn.BeginTransactionAsync();
            try
            {
                await using var cmd = conn.CreateCommand();
                cmd.Transaction = (DbTransaction)tx;
                cmd.CommandText = rendered;
                var affected = await cmd.ExecuteNonQueryAsync();
                await tx.RollbackAsync();
                return new DbTestSqlResult { OperationType = "UPDATE", AffectedRows = affected };
            }
            catch
            {
                try { await tx.RollbackAsync(); } catch { /* 连接已断开时忽略 */ }
                throw;
            }
        }

        // 查询操作：读取前 100 行预览
        var result = new DbTestSqlResult { OperationType = "QUERY" };
        var colNames = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = rendered;
            await using var reader = await cmd.ExecuteReaderAsync();
            for (int i = 0; i < reader.FieldCount; i++)
                colNames.Add(reader.GetName(i));

            var total = 0;
            while (await reader.ReadAsync())
            {
                total++;
                if (total > 100) continue;   // 只保留前 100 行
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                result.Rows.Add(row);
            }
            result.RowCount  = total;
            result.Truncated = total > 100;
        }

        // 单表查询时附带字段中文注释（无 JOIN 且能解析出表名）
        var tableMatch = Regex.Match(rendered, @"\bfrom\s+([A-Za-z0-9_\.]+)", RegexOptions.IgnoreCase);
        var hasJoin = Regex.IsMatch(rendered, @"\bjoin\b", RegexOptions.IgnoreCase);
        if (tableMatch.Success && !hasJoin)
        {
            try
            {
                var columns = await GetColumnsAsync(dataSourceName, tableMatch.Groups[1].Value);
                var commentMap = columns.ToDictionary(c => c.Name, c => c.Comment, StringComparer.OrdinalIgnoreCase);
                foreach (var name in colNames)
                    result.Columns.Add(new DbResultColumn
                    {
                        Name    = name,
                        Comment = commentMap.TryGetValue(name, out var comment) ? comment : ""
                    });
            }
            catch
            {
                // 注释获取失败不影响测试结果展示
                foreach (var name in colNames)
                    result.Columns.Add(new DbResultColumn { Name = name });
            }
        }
        else
        {
            foreach (var name in colNames)
                result.Columns.Add(new DbResultColumn { Name = name });
        }

        return result;
    }

    private static string RenderTemplate(string sql, Dictionary<string, string?>? vars)
    {
        return Regex.Replace(sql, @"\$\{([^}]+)\}", m =>
        {
            var varName = m.Groups[1].Value.Trim();
            return vars != null && vars.TryGetValue(varName, out var v) && v != null ? v : "";
        });
    }
}

/// <summary>数据库对象列表（表/视图/存储过程）</summary>
public class DbObjectsResult
{
    public List<DbObjectItem> Tables { get; set; } = new();
    public List<DbObjectItem> Views { get; set; } = new();
    public List<DbObjectItem> Procedures { get; set; } = new();
}

public class DbObjectItem
{
    public string Name { get; set; } = "";
}

/// <summary>表/视图字段</summary>
public class DbColumnItem
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    public string Comment { get; set; } = "";
    public string? DefaultValue { get; set; }
    public bool IsNullable { get; set; } = true;
}

/// <summary>存储过程参数</summary>
public class DbProcParamItem
{
    public string Name { get; set; } = "";
    public string DataType { get; set; } = "";
    /// <summary>IN / OUT / INOUT</summary>
    public string Mode { get; set; } = "IN";
    public string? DefaultValue { get; set; }
}

/// <summary>SQL 单独测试结果</summary>
public class DbTestSqlResult
{
    /// <summary>QUERY / UPDATE</summary>
    public string OperationType { get; set; } = "QUERY";

    /// <summary>查询列（名称+中文注释）（QUERY）</summary>
    public List<DbResultColumn> Columns { get; set; } = new();

    /// <summary>查询结果行（QUERY，最多 100 行）</summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>查询总行数（QUERY）</summary>
    public long RowCount { get; set; }

    /// <summary>结果是否被截断（超过 100 行）</summary>
    public bool Truncated { get; set; }

    /// <summary>影响行数（UPDATE）</summary>
    public int AffectedRows { get; set; }
}

/// <summary>查询结果列（名称+中文注释）</summary>
public class DbResultColumn
{
    public string Name { get; set; } = "";
    public string Comment { get; set; } = "";
}
