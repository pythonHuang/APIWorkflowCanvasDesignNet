namespace Juggle.Application.Models.Request;

/// <summary>
/// 获取数据源数据库对象（表/视图/存储过程）请求模型
/// </summary>
public class DataSourceMetadataRequest
{
    /// <summary>
    /// 数据源名称（流程中通过此名称引用数据源）
    /// </summary>
    public string DataSourceName { get; set; } = "";
}

/// <summary>
/// 获取表/视图字段列表请求模型
/// </summary>
public class DataSourceColumnsRequest
{
    /// <summary>
    /// 数据源名称
    /// </summary>
    public string DataSourceName { get; set; } = "";

    /// <summary>
    /// 表名或视图名
    /// </summary>
    public string TableName { get; set; } = "";
}

/// <summary>
/// 获取存储过程参数列表请求模型
/// </summary>
public class DataSourceProcParamsRequest
{
    /// <summary>
    /// 数据源名称
    /// </summary>
    public string DataSourceName { get; set; } = "";

    /// <summary>
    /// 存储过程名称
    /// </summary>
    public string ProcName { get; set; } = "";
}

/// <summary>
/// 单独测试 SQL 请求模型
/// </summary>
public class DataSourceTestSqlRequest
{
    /// <summary>
    /// 数据源名称
    /// </summary>
    public string DataSourceName { get; set; } = "";

    /// <summary>
    /// SQL 语句，支持 ${varName} 模板变量
    /// </summary>
    public string Sql { get; set; } = "";

    /// <summary>
    /// 操作类型：QUERY（查询）/ UPDATE（更改，事务回滚）
    /// </summary>
    public string OperationType { get; set; } = "QUERY";

    /// <summary>
    /// 模板变量值（varName → 值），未提供的变量替换为空字符串
    /// </summary>
    public Dictionary<string, string?>? Params { get; set; }
}
