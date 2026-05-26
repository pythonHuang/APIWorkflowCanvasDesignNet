namespace Juggle.Domain.Entities;

/// <summary>
/// API 方法定义实体
/// 用于存储可被流程调用的外部 API 元数据信息
/// </summary>
public class ApiEntity : BaseEntity
{
    /// <summary>
    /// 所属套件编码
    /// </summary>
    public string? SuiteCode { get; set; }

    /// <summary>
    /// 方法编码（唯一标识）
    /// </summary>
    public string? MethodCode { get; set; }

    /// <summary>
    /// 方法名称（中文描述）
    /// </summary>
    public string? MethodName { get; set; }

    /// <summary>
    /// 方法描述
    /// </summary>
    public string? MethodDesc { get; set; }

    /// <summary>
    /// API 请求地址（URL）
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// HTTP 请求方法：GET / POST / PUT / DELETE
    /// </summary>
    public string? RequestType { get; set; }

    /// <summary>
    /// 请求内容类型：JSON / FORM
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Mock 模式返回的预设 JSON 数据
    /// 设置后调用此方法时跳过真实请求，直接返回此数据
    /// </summary>
    public string? MockJson { get; set; }

    /// <summary>
    /// 调用方式：HTTP / WEBSERVICE
    /// </summary>
    public string? MethodType { get; set; } = "HTTP";

    /// <summary>
    /// SOAP 版本: 11=SOAP 1.1, 12=SOAP 1.2
    /// </summary>
    public string? SoapVersion { get; set; } = "11";

    /// <summary>
    /// SOAP 操作名称（对应 wsdl:operation name）
    /// </summary>
    public string? SoapMethod { get; set; }

    /// <summary>
    /// SOAP 命名空间（xmlns）
    /// </summary>
    public string? SoapNamespace { get; set; }

    /// <summary>
    /// SOAPAction 请求头值
    /// </summary>
    public string? SoapAction { get; set; }

    /// <summary>
    /// 服务别名（用于友好访问: /open/api/{alias}）
    /// </summary>
    public string? ServiceAlias { get; set; }

    /// <summary>状态: 1=启用, 0=停用</summary>
    public int Status { get; set; } = 1;
}
