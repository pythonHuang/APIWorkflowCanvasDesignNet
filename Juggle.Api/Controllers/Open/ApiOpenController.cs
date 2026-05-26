using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Open;

/// <summary>
/// 接口开放调用控制器
/// 通过 Access Token 授权后直接调用套件中已配置的接口
/// GET/POST /open/api/{methodCode}
/// </summary>
[ApiController]
[Route("open/api")]
public class ApiOpenController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public ApiOpenController(JuggleDbContext db, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    private async Task<bool> ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        return await _db.Tokens.AnyAsync(t => t.TokenValue == token && t.Status == 1 && t.Deleted == 0);
    }

    [HttpGet("{code}")]
    public async Task<ApiResult> Get(string code,
        [FromQuery] Dictionary<string, string> queryParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        return await ExecuteApi(code, "GET", ToObjectDict(queryParams), new Dictionary<string, string>());
    }

    [HttpPost("{code}")]
    public async Task<ApiResult> Post(string code,
        [FromBody] Dictionary<string, object?> bodyParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        // 从 Headers 中提取自定义头
        var headers = new Dictionary<string, string>();
        foreach (var h in Request.Headers)
        {
            if (!h.Key.StartsWith("Content-") && !h.Key.StartsWith("Host") &&
                h.Key != "X-Access-Token")
                headers[h.Key] = h.Value.ToString();
        }
        return await ExecuteApi(code, "POST", bodyParams, headers);
    }

    private async Task<ApiResult> ExecuteApi(string code, string method, Dictionary<string, object?> inputParams, Dictionary<string, string> headers)
    {
        // 先用 methodCode 查找，再用 serviceAlias 查找
        var api = await _db.Apis.FirstOrDefaultAsync(a => a.MethodCode == code && a.Deleted == 0)
            ?? await _db.Apis.FirstOrDefaultAsync(a => a.ServiceAlias == code && a.Deleted == 0);
        if (api == null) return ApiResult.Fail("接口不存在");
        if (api.Status == 0) return ApiResult.Fail("接口已停用");

        // 加载参数定义
        var paramDefs = await _db.Parameters
            .Where(p => p.OwnerId == api.Id && p.ParamType == 1 && p.Deleted == 0)
            .ToListAsync();
        var posMap = paramDefs.ToDictionary(p => p.ParamCode!, p => p.ParamPosition ?? "");

        try
        {
            // Mock 模式
            if (!string.IsNullOrEmpty(api.MockJson))
                return ApiResult.Success(new { response = api.MockJson, mock = true });

            var client = _httpClientFactory.CreateClient();
            foreach (var h in headers)
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value);

            var requestType = api.RequestType?.ToUpper() ?? method;
            string responseText;

            var rawBodyKey = posMap.FirstOrDefault(kv => kv.Value == "rawBody").Key;
            var queryParams = inputParams.Where(kv => posMap.GetValueOrDefault(kv.Key, "") == "query").ToList();
            var bodyParams = inputParams.Where(kv => posMap.GetValueOrDefault(kv.Key, "") != "query").ToList();

            if (requestType == "GET" || requestType == "DELETE")
            {
                var url = api.Url!;
                if (inputParams.Count > 0)
                {
                    var query = string.Join("&", inputParams.Select(kv =>
                        $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));
                    url = url.Contains('?') ? $"{url}&{query}" : $"{url}?{query}";
                }
                var resp = requestType == "GET" ? await client.GetAsync(url) : await client.DeleteAsync(url);
                responseText = await resp.Content.ReadAsStringAsync();
            }
            else
            {
                HttpContent content;
                if (!string.IsNullOrEmpty(rawBodyKey) && inputParams.ContainsKey(rawBodyKey))
                {
                    content = new StringContent(inputParams[rawBodyKey]?.ToString() ?? "",
                        Encoding.UTF8, api.ContentType == "XML" ? "application/xml" : "text/plain");
                }
                else
                {
                    var ctHeader = headers.FirstOrDefault(h =>
                        string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)).Value ?? "";
                    var contentType = !string.IsNullOrEmpty(ctHeader) ? ctHeader
                        : api.ContentType == "FORM" ? "application/x-www-form-urlencoded"
                        : "application/json";

                    if (contentType.Contains("x-www-form-urlencoded"))
                    {
                        var formStr = string.Join("&", bodyParams.Select(kv =>
                            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));
                        content = new StringContent(formStr, Encoding.UTF8, contentType);
                    }
                    else
                    {
                        var bodyDict = bodyParams.ToDictionary(kv => kv.Key, kv => kv.Value);
                        content = new StringContent(JsonSerializer.Serialize(bodyDict), Encoding.UTF8, contentType);
                    }
                }
                var resp = requestType == "PUT" ? await client.PutAsync(api.Url, content) : await client.PostAsync(api.Url, content);
                responseText = await resp.Content.ReadAsStringAsync();
            }

            return ApiResult.Success(new { response = responseText });
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"调用失败: {ex.Message}");
        }
    }

    /// <summary>生成接口 WSDL（无需认证）</summary>
    [HttpGet("wsdl/{code}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetWsdl(string code)
    {
        var api = await _db.Apis.FirstOrDefaultAsync(a => a.MethodCode == code && a.Deleted == 0)
            ?? await _db.Apis.FirstOrDefaultAsync(a => a.ServiceAlias == code && a.Deleted == 0);
        if (api == null) return Content("接口不存在", "text/plain");
        if (api.MethodType != "WEBSERVICE") return Content("该接口不是 WebService 类型", "text/plain");

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var inputParams = await _db.Parameters.Where(p => p.OwnerId == api.Id && p.ParamType == 1 && p.Deleted == 0).OrderBy(p => p.SortNum).ToListAsync();
        var outputParams = await _db.Parameters.Where(p => p.OwnerId == api.Id && p.ParamType == 2 && p.Deleted == 0).OrderBy(p => p.SortNum).ToListAsync();
        var ns = api.SoapNamespace ?? $"http://juggle.local/{code}";
        var wsdl = BuildApiWsdl(api, inputParams, outputParams, baseUrl, ns);
        return Content(wsdl, "text/xml; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>SOAP 调用接口（需要 Token）</summary>
    [HttpPost("soap/{code}")]
    public async Task<IActionResult> SoapTrigger(string code,
        [FromHeader(Name = "SOAPAction")] string? soapAction,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return new ContentResult { Content = BuildSoapFault("无效的 Access Token"), ContentType = "text/xml; charset=utf-8", StatusCode = 401 };
        var api = await _db.Apis.FirstOrDefaultAsync(a => a.MethodCode == code && a.Deleted == 0)
            ?? await _db.Apis.FirstOrDefaultAsync(a => a.ServiceAlias == code && a.Deleted == 0);
        if (api == null)
            return new ContentResult { Content = BuildSoapFault("接口不存在"), ContentType = "text/xml; charset=utf-8", StatusCode = 404 };
        if (api.Status == 0)
            return new ContentResult { Content = BuildSoapFault("接口已停用"), ContentType = "text/xml; charset=utf-8", StatusCode = 403 };

        Dictionary<string, object?> inputParams;
        try
        {
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
            var soapXml = await reader.ReadToEndAsync();
            inputParams = ParseSoapBody(soapXml);
        }
        catch (Exception ex)
        {
            return new ContentResult { Content = BuildSoapFault($"SOAP 解析失败: {ex.Message}"), ContentType = "text/xml; charset=utf-8", StatusCode = 400 };
        }

        var result = await ExecuteApi(api.MethodCode!, "POST", inputParams, new Dictionary<string, string>());
        if (result.Code != 200)
            return new ContentResult { Content = BuildSoapFault(result.Message ?? "调用失败"), ContentType = "text/xml; charset=utf-8" };

        var ns = api.SoapNamespace ?? $"http://juggle.local/{code}";
        var responseXml = BuildSoapResponse(result.Data, ns, code);
        return new ContentResult { Content = responseXml, ContentType = "text/xml; charset=utf-8" };
    }

    // ========== WSDL / SOAP 工具方法 ==========

    private static string BuildApiWsdl(ApiEntity api, List<ParameterEntity> inputs, List<ParameterEntity> outputs, string baseUrl, string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<definitions name=\"{api.MethodCode}\" targetNamespace=\"{ns}\" xmlns=\"http://schemas.xmlsoap.org/wsdl/\" xmlns:soap=\"http://schemas.xmlsoap.org/wsdl/soap/\" xmlns:tns=\"{ns}\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\">");
        sb.AppendLine("  <types>");
        sb.AppendLine($"    <xsd:schema targetNamespace=\"{ns}\" elementFormDefault=\"qualified\">");
        sb.AppendLine("      <xsd:element name=\"Request\"><xsd:complexType><xsd:sequence>");
        foreach (var p in inputs)
            sb.AppendLine($"        <xsd:element name=\"{p.ParamCode}\" type=\"xsd:{MapWsdlType(p.DataType)}\" />");
        sb.AppendLine("      </xsd:sequence></xsd:complexType></xsd:element>");
        sb.AppendLine("      <xsd:element name=\"Response\"><xsd:complexType><xsd:sequence>");
        foreach (var p in outputs)
            sb.AppendLine($"        <xsd:element name=\"{p.ParamCode}\" type=\"xsd:{MapWsdlType(p.DataType)}\" />");
        sb.AppendLine("      </xsd:sequence></xsd:complexType></xsd:element>");
        sb.AppendLine("    </xsd:schema>");
        sb.AppendLine("  </types>");
        sb.AppendLine("  <message name=\"RequestMessage\"><part name=\"parameters\" element=\"tns:Request\" /></message>");
        sb.AppendLine("  <message name=\"ResponseMessage\"><part name=\"parameters\" element=\"tns:Response\" /></message>");
        sb.AppendLine($"  <portType name=\"{api.MethodCode}Port\"><operation name=\"Call\"><input message=\"tns:RequestMessage\" /><output message=\"tns:ResponseMessage\" /></operation></portType>");
        sb.AppendLine($"  <binding name=\"{api.MethodCode}Binding\" type=\"tns:{api.MethodCode}Port\">");
        sb.AppendLine($"    <soap:binding transport=\"http://schemas.xmlsoap.org/soap/http\" />");
        sb.AppendLine($"    <operation name=\"Call\"><soap:operation soapAction=\"{ns}/Call\" /><input><soap:body use=\"literal\" /></input><output><soap:body use=\"literal\" /></output></operation>");
        sb.AppendLine("  </binding>");
        sb.AppendLine($"  <service name=\"{api.MethodCode}\"><port name=\"{api.MethodCode}Port\" binding=\"tns:{api.MethodCode}Binding\"><soap:address location=\"{baseUrl}/open/api/soap/{api.MethodCode}\" /></port></service>");
        sb.AppendLine("</definitions>");
        return sb.ToString();
    }

    private static string MapWsdlType(string? t) => (t?.ToLower()) switch { "integer" or "int" or "long" => "integer", "double" or "float" or "decimal" => "double", "boolean" or "bool" => "boolean", "date" or "datetime" => "date", _ => "string" };

    private static Dictionary<string, object?> ParseSoapBody(string soapXml)
    {
        var result = new Dictionary<string, object?>();
        var xdoc = XDocument.Parse(soapXml);
        XNamespace soapEnv = "http://schemas.xmlsoap.org/soap/envelope/";
        XNamespace soapEnv12 = "http://www.w3.org/2003/05/soap-envelope";
        var body = xdoc.Root?.Element(soapEnv + "Body") ?? xdoc.Root?.Element(soapEnv12 + "Body");
        if (body == null) return result;
        var firstChild = body.Elements().FirstOrDefault();
        if (firstChild == null) return result;
        foreach (var el in firstChild.Elements()) result[el.Name.LocalName] = el.HasElements ? (object)el.ToString() : el.Value;
        return result;
    }

    private static string BuildSoapResponse(object? data, string ns, string code)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tns=\"" + ns + "\">");
        sb.AppendLine("  <soap:Body>");
        sb.AppendLine($"    <tns:CallResponse xmlns:tns=\"{ns}\">");
        if (data is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var p in je.EnumerateObject())
                sb.AppendLine($"      <tns:{p.Name}>{p.Value}</tns:{p.Name}>");
        sb.AppendLine("    </tns:CallResponse>");
        sb.AppendLine("  </soap:Body>");
        sb.AppendLine("</soap:Envelope>");
        return sb.ToString();
    }

    private static string BuildSoapFault(string message)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body><soap:Fault><faultcode>soap:Client</faultcode><faultstring>{System.Security.SecurityElement.Escape(message)}</faultstring></soap:Fault></soap:Body>
</soap:Envelope>";
    }

    private static Dictionary<string, object?> ToObjectDict(Dictionary<string, string> dict)
    {
        return dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
    }
}
