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

    /// <summary>获取表/视图的字段列表（所有数据库统一使用 SELECT * WHERE 1=0 取元数据）。</summary>
    public async Task<List<DbColumnItem>> GetColumnsAsync(string dataSourceName, string tableName)
    {
        var dsInfo = await LoadDataSourceInfoAsync(dataSourceName);
        var columns = new List<DbColumnItem>();
        await using var conn = MysqlNodeExecutor.CreateConnection(dsInfo);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {tableName} WHERE 1=0";
        await using var reader = await cmd.ExecuteReaderAsync();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columns.Add(new DbColumnItem
            {
                Name     = reader.GetName(i),
                DataType = reader.GetDataTypeName(i)
            });
        }
        return columns;
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
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = rendered;
            await using var reader = await cmd.ExecuteReaderAsync();
            for (int i = 0; i < reader.FieldCount; i++)
                result.Columns.Add(reader.GetName(i));

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
}

/// <summary>SQL 单独测试结果</summary>
public class DbTestSqlResult
{
    /// <summary>QUERY / UPDATE</summary>
    public string OperationType { get; set; } = "QUERY";

    /// <summary>查询列名（QUERY）</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>查询结果行（QUERY，最多 100 行）</summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>查询总行数（QUERY）</summary>
    public long RowCount { get; set; }

    /// <summary>结果是否被截断（超过 100 行）</summary>
    public bool Truncated { get; set; }

    /// <summary>影响行数（UPDATE）</summary>
    public int AffectedRows { get; set; }
}
