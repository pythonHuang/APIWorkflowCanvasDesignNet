namespace Juggle.Domain.Entities;

/// <summary>
/// 参数定义实体
/// 用于定义 API 或流程的输入输出参数结构
/// </summary>
public class ParameterEntity : BaseEntity
{
    /// <summary>
    /// 所属者 ID（API ID 或流程定义 ID）
    /// </summary>
    public long? OwnerId { get; set; }

    /// <summary>
    /// 所属者编码（套件编码或流程 Key）
    /// </summary>
    public string? OwnerCode { get; set; }

    /// <summary>
    /// 参数类型：
    /// - 1：API 入参
    /// - 2：API 出参
    /// - 3：对象属性
    /// - 4：API Header
    /// - 5：流程入参
    /// - 6：流程出参
    /// </summary>
    public int ParamType { get; set; }

    /// <summary>
    /// 参数编码（唯一标识）
    /// </summary>
    public string? ParamCode { get; set; }

    /// <summary>
    /// 参数名称（中文描述）
    /// </summary>
    public string? ParamName { get; set; }

    /// <summary>
    /// 数据类型：
    /// - string：字符串
    /// - int/integer：整数
    /// - boolean：布尔值
    /// - object：对象（引用 ObjectEntity）
    /// - array：数组
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>
    /// 关联的对象编码（当 DataType=object 时引用）
    /// </summary>
    public string? ObjectCode { get; set; }

    /// <summary>
    /// 是否必填：0-可选，1-必填
    /// </summary>
    public int Required { get; set; } = 0;

    /// <summary>
    /// 默认值
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// 参数描述说明
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 参数位置：query=查询参数, body=请求体字段, rawBody=整个请求体, ""=自动
    /// </summary>
    public string? ParamPosition { get; set; }

    /// <summary>
    /// 排序号（用于参数展示顺序）
    /// </summary>
    public int SortNum { get; set; } = 0;
}
