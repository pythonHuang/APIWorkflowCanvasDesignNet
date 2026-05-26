namespace Juggle.Application.Models.Request;

public class ApiAddRequest
{
    public string SuiteCode { get; set; } = "";
    public string MethodName { get; set; } = "";
    public string? MethodDesc { get; set; }
    public string Url { get; set; } = "";
    public string RequestType { get; set; } = "GET";
    public string ContentType { get; set; } = "JSON";
    public string? MockJson { get; set; }
    /// <summary>HTTP WEBSERVICE</summary>
    public string MethodType { get; set; } = "HTTP";
    /// <summary>SOAP 版本: 11 / 12</summary>
    public string? SoapVersion { get; set; }
    /// <summary>SOAP 操作名</summary>
    public string? SoapMethod { get; set; }
    /// <summary>SOAP 命名空间</summary>
    public string? SoapNamespace { get; set; }
    /// <summary>SOAPAction 头</summary>
    public string? SoapAction { get; set; }
    /// <summary>服务别名（/open/api/{alias}）</summary>
    public string? ServiceAlias { get; set; }
}