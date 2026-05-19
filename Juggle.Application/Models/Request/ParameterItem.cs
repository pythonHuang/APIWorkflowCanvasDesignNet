namespace Juggle.Application.Models.Request;

/// <summary>
/// 参数项定义
/// 用于描述接口或流程的输入/输出参数
/// </summary>
public class ParameterItem
{
    /// <summary>
    /// 参数 ID（可选，0 表示新增）
    /// </summary>
    public long? Id { get; set; }

    /// <summary>
    /// 参数编码
    /// </summary>
    public string ParamCode { get; set; } = "";

    /// <summary>
    /// 参数名称
    /// </summary>
    public string ParamName { get; set; } = "";

    /// <summary>
    /// 数据类型
    /// string、int、long、decimal、bool、date、datetime、object、array
    /// </summary>
    public string DataType { get; set; } = "string";

    /// <summary>
    /// 业务对象编码（可选）
    /// 当数据类型为 object 时，指定关联的业务对象
    /// </summary>
    public string? ObjectCode { get; set; }

    /// <summary>
    /// 是否必填
    /// 0-否；1-是
    /// </summary>
    public int Required { get; set; } = 0;

    /// <summary>
    /// 默认值（可选）
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// 参数描述（可选）
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 参数位置：query=查询参数, body=请求体字段, rawBody=整个请求体
    /// </summary>
    public string? ParamPosition { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int SortNum { get; set; }
}