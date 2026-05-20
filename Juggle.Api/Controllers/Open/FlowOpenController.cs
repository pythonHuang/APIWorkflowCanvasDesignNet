using System.Text;
using System.Xml.Linq;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Models.Response;
using Juggle.Application.Services.Flow;
using Juggle.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Open;

/// <summary>
/// 流程开放接口控制器
/// 供外部系统通过 Access Token 触发流程执行
/// 支持同步/异步执行、版本指定、权限校验、WSDL 生成、SOAP 调用等功能
/// </summary>
[ApiController]
[Route("open/flow")]
public class FlowOpenController : ControllerBase
{
    private readonly JuggleDbContext      _db;
    private readonly FlowExecutionService _flowExec;

    public FlowOpenController(JuggleDbContext db, FlowExecutionService flowExec)
    {
        _db       = db;
        _flowExec = flowExec;
    }

    /// <summary>
    /// 验证 Token 是否有效
    /// </summary>
    /// <param name="token">Access Token</param>
    /// <returns>Token 是否有效</returns>
    private async Task<bool> ValidateToken(string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        return await _db.Tokens.AnyAsync(t => t.TokenValue == token && t.Status == 1 && t.Deleted == 0);
    }

    /// <summary>校验 Token 对指定流程是否有权限（无权限配置则允许全部）</summary>
    private async Task<bool> ValidateFlowPermission(string? token, string flowKey)
    {
        if (string.IsNullOrEmpty(token)) return false;
        var tokenEntity = await _db.Tokens.FirstOrDefaultAsync(t => t.TokenValue == token && t.Status == 1 && t.Deleted == 0);
        if (tokenEntity == null) return false;

        // 查询是否有权限配置
        var hasPermissions = await _db.TokenPermissions.AnyAsync(p => p.TokenId == tokenEntity.Id && p.Deleted == 0);
        if (!hasPermissions) return true; // 无权限配置 = 全部允许

        // 有权限配置，检查是否包含该流程
        return await _db.TokenPermissions.AnyAsync(p =>
            p.TokenId == tokenEntity.Id && p.PermissionType == "FLOW" &&
            p.ResourceKey == flowKey && p.Deleted == 0);
    }

    // ──────────────────────────────────────────────
    // 带版本号
    // ──────────────────────────────────────────────

    [HttpGet("trigger/{version}/{key}")]
    public async Task<ApiResult> TriggerGet(string version, string key,
        [FromQuery] Dictionary<string, string> queryParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        if (!await ValidateFlowPermission(token, key))
            return ApiResult.Fail("该 Token 无权访问此流程");

        return await TriggerFlowByVersion(version, key,
            queryParams.ToDictionary(k => k.Key, k => (object?)k.Value));
    }

    [HttpPost("trigger/{version}/{key}")]
    public async Task<ApiResult> TriggerPost(string version, string key,
        [FromBody] Dictionary<string, object?> bodyParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        if (!await ValidateFlowPermission(token, key))
            return ApiResult.Fail("该 Token 无权访问此流程");

        return await TriggerFlowByVersion(version, key, bodyParams);
    }

    // ──────────────────────────────────────────────
    // 不带版本号（取最新已发布版本）
    // ──────────────────────────────────────────────

    [HttpGet("trigger/{key}")]
    public async Task<ApiResult> TriggerLatestGet(string key,
        [FromQuery] Dictionary<string, string> queryParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        if (!await ValidateFlowPermission(token, key))
            return ApiResult.Fail("该 Token 无权访问此流程");

        return await TriggerFlowLatest(key,
            queryParams.ToDictionary(k => k.Key, k => (object?)k.Value));
    }

    [HttpPost("trigger/{key}")]
    public async Task<ApiResult> TriggerLatestPost(string key,
        [FromBody] Dictionary<string, object?> bodyParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        if (!await ValidateFlowPermission(token, key))
            return ApiResult.Fail("该 Token 无权访问此流程");

        return await TriggerFlowLatest(key, bodyParams);
    }

    // ──────────────────────────────────────────────
    // 服务别名访问（通过别名触发流程最新版本）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 通过服务别名触发流程（GET），自动查找对应流程并执行最新已发布版本。
    /// 访问地址: /open/services/{alias}
    /// </summary>
    [HttpGet("~/open/services/{alias}")]
    public async Task<ApiResult> ServiceGet(string alias,
        [FromQuery] Dictionary<string, string> queryParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        var flowKey = await GetFlowKeyByAlias(alias);
        if (flowKey == null) return ApiResult.Fail($"服务别名 '{alias}' 不存在");
        if (!await ValidateFlowPermission(token, flowKey))
            return ApiResult.Fail("该 Token 无权访问此流程");
        return await TriggerFlowLatest(flowKey,
            queryParams.ToDictionary(k => k.Key, k => (object?)k.Value));
    }

    /// <summary>
    /// 通过服务别名触发流程（POST），自动查找对应流程并执行最新已发布版本。
    /// 访问地址: /open/services/{alias}
    /// </summary>
    [HttpPost("~/open/services/{alias}")]
    public async Task<ApiResult> ServicePost(string alias,
        [FromBody] Dictionary<string, object?> bodyParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        var flowKey = await GetFlowKeyByAlias(alias);
        if (flowKey == null) return ApiResult.Fail($"服务别名 '{alias}' 不存在");
        if (!await ValidateFlowPermission(token, flowKey))
            return ApiResult.Fail("该 Token 无权访问此流程");
        return await TriggerFlowLatest(flowKey, bodyParams);
    }

    private async Task<string?> GetFlowKeyByAlias(string alias)
    {
        var definition = await _db.FlowDefinitions
            .Where(f => f.ServiceAlias == alias && f.Deleted == 0)
            .FirstOrDefaultAsync();
        return definition?.FlowKey;
    }

    // ──────────────────────────────────────────────
    // 异步触发（立即返回 logId，后台执行）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 异步触发流程（POST），立即返回 logId，不等待执行完成。
    /// 调用方通过 GET /open/flow/result/{logId} 轮询执行结果。
    /// </summary>
    [HttpPost("triggerAsync/{key}")]
    public async Task<ApiResult> TriggerAsyncPost(string key,
        [FromBody] Dictionary<string, object?> bodyParams,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);
        if (!await ValidateFlowPermission(token, key))
            return ApiResult.Fail("该 Token 无权访问此流程");

        var flowVersion = await _db.FlowVersions
            .Where(v => v.FlowKey == key && v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
        if (flowVersion == null)
            return ApiResult.Fail("未找到已发布的流程版本");

        var definition = await _db.FlowDefinitions
            .FirstOrDefaultAsync(f => f.FlowKey == key && f.Deleted == 0);
        if (definition == null)
            return ApiResult.Fail("流程定义不存在");

        // 预先写入一条 RUNNING 日志，获得 logId
        var logId = await _flowExec.CreateRunningLogAsync(definition, flowVersion.Version!, bodyParams);

        // 后台异步执行，不阻塞当前请求
        _ = Task.Run(async () =>
        {
            try
            {
                await _flowExec.RunAsyncWithLog(definition, flowVersion.FlowContent!, bodyParams,
                    "open_async", flowVersion.Version!, logId);
            }
            catch { /* 异常已在 RunAsyncWithLog 内记录到日志 */ }
        });

        return ApiResult.Success(new { logId, message = "流程已提交异步执行，请通过 logId 轮询结果" });
    }

    /// <summary>
    /// 查询异步流程执行结果。
    /// status: RUNNING（执行中）/ SUCCESS（成功）/ FAILED（失败）
    /// </summary>
    [HttpGet("result/{logId}")]
    public async Task<ApiResult> GetAsyncResult(long logId,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return ApiResult.Fail("无效的 Access Token", 401);

        var log = await _db.FlowLogs.FirstOrDefaultAsync(l => l.Id == logId && l.Deleted == 0);
        if (log == null)
            return ApiResult.Fail("日志记录不存在");

        object? outputData = null;
        if (!string.IsNullOrEmpty(log.OutputJson) && log.Status != "RUNNING")
        {
            try { outputData = System.Text.Json.JsonSerializer.Deserialize<object>(log.OutputJson); }
            catch { outputData = log.OutputJson; }
        }

        return ApiResult.Success(new
        {
            logId    = log.Id,
            status   = log.Status,
            flowKey  = log.FlowKey,
            flowName = log.FlowName,
            version  = log.Version,
            startTime = log.StartTime,
            endTime  = log.EndTime,
            costMs   = log.CostMs,
            errorMessage = log.ErrorMessage,
            output   = outputData
        });
    }

    // ──────────────────────────────────────────────
    // 内部实现
    // ──────────────────────────────────────────────

    /// <summary>生成流程的 WSDL（无需认证，通过 flowKey 获取最新已发布版本）</summary>
    [HttpGet("wsdl/{key}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetWsdl(string key)
    {
        var flowVersion = await _db.FlowVersions
            .Where(v => v.FlowKey == key && v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
        if (flowVersion == null) return Content("未找到已发布的流程版本", "text/plain");

        var definition = await _db.FlowDefinitions
            .FirstOrDefaultAsync(f => f.FlowKey == key && f.Deleted == 0);
        if (definition == null) return Content("流程定义不存在", "text/plain");

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var triggerUrl = $"{baseUrl}/open/flow/trigger/{key}";
        var wsdl = GenerateWsdl(definition, flowVersion, triggerUrl, key);
        return Content(wsdl, "text/xml; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>生成特定版本流程的 WSDL（无需认证）</summary>
    [HttpGet("wsdl/{version}/{key}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetWsdlVersioned(string version, string key)
    {
        var flowVersion = await _db.FlowVersions
            .FirstOrDefaultAsync(v => v.FlowKey == key && v.Version == version
                                   && v.Status == 1 && v.Deleted == 0);
        if (flowVersion == null) return Content("流程版本不存在或已禁用", "text/plain");

        var definition = await _db.FlowDefinitions
            .FirstOrDefaultAsync(f => f.FlowKey == key && f.Deleted == 0);
        if (definition == null) return Content("流程定义不存在", "text/plain");

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var triggerUrl = $"{baseUrl}/open/flow/trigger/{version}/{key}";
        var wsdl = GenerateWsdl(definition, flowVersion, triggerUrl, key);
        return Content(wsdl, "text/xml; charset=utf-8", Encoding.UTF8);
    }

    /// <summary>通过服务别名获取 WSDL（无需认证）</summary>
    [HttpGet("~/open/services/{alias}/wsdl")]
    [AllowAnonymous]
    public async Task<IActionResult> GetServiceWsdl(string alias)
    {
        var definition = await _db.FlowDefinitions
            .Where(f => f.ServiceAlias == alias && f.Deleted == 0)
            .FirstOrDefaultAsync();
        if (definition == null) return Content($"服务别名 '{alias}' 不存在", "text/plain");

        var flowVersion = await _db.FlowVersions
            .Where(v => v.FlowKey == definition.FlowKey && v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
        if (flowVersion == null) return Content("未找到已发布的流程版本", "text/plain");

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var triggerUrl = $"{baseUrl}/open/services/{alias}";
        var wsdl = GenerateWsdl(definition, flowVersion, triggerUrl, definition.FlowKey);
        return Content(wsdl, "text/xml; charset=utf-8", Encoding.UTF8);
    }

    // ──────────────────────────────────────────────
    // SOAP 触发支持（无需额外认证，复用 Token 校验）
    // ──────────────────────────────────────────────

    /// <summary>SOAP 触发流程（通过 flowKey）</summary>
    [HttpPost("soap/{key}")]
    public async Task<IActionResult> SoapTrigger(string key,
        [FromHeader(Name = "SOAPAction")] string? soapAction,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return new ContentResult { Content = BuildSoapFault("无效的 Access Token"), ContentType = "text/xml; charset=utf-8", StatusCode = 401 };
        if (!await ValidateFlowPermission(token, key))
            return new ContentResult { Content = BuildSoapFault("该 Token 无权访问此流程"), ContentType = "text/xml; charset=utf-8", StatusCode = 403 };

        return await ExecuteSoapFlow(key, soapAction);
    }

    /// <summary>SOAP 触发流程（通过服务别名）</summary>
    [HttpPost("~/open/services/{alias}/soap")]
    public async Task<IActionResult> SoapServiceTrigger(string alias,
        [FromHeader(Name = "SOAPAction")] string? soapAction,
        [FromHeader(Name = "X-Access-Token")] string? token)
    {
        if (!await ValidateToken(token))
            return new ContentResult { Content = BuildSoapFault("无效的 Access Token"), ContentType = "text/xml; charset=utf-8", StatusCode = 401 };
        var flowKey = await GetFlowKeyByAlias(alias);
        if (flowKey == null)
            return new ContentResult { Content = BuildSoapFault($"服务别名 '{alias}' 不存在"), ContentType = "text/xml; charset=utf-8", StatusCode = 404 };
        if (!await ValidateFlowPermission(token, flowKey))
            return new ContentResult { Content = BuildSoapFault("该 Token 无权访问此流程"), ContentType = "text/xml; charset=utf-8", StatusCode = 403 };

        return await ExecuteSoapFlow(flowKey, soapAction);
    }

    private async Task<IActionResult> ExecuteSoapFlow(string key, string? soapAction)
    {
        // 解析 SOAP body 提取参数
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

        // 执行流程
        var flowVersion = await _db.FlowVersions
            .Where(v => v.FlowKey == key && v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
        if (flowVersion == null)
            return new ContentResult { Content = BuildSoapFault("未找到已发布的流程版本"), ContentType = "text/xml; charset=utf-8", StatusCode = 404 };

        var definition = await _db.FlowDefinitions
            .FirstOrDefaultAsync(f => f.FlowKey == key && f.Deleted == 0);
        if (definition == null)
            return new ContentResult { Content = BuildSoapFault("流程定义不存在"), ContentType = "text/xml; charset=utf-8", StatusCode = 404 };

        var result = await _flowExec.RunAsync(
            definition, flowVersion.FlowContent!, inputParams, "open", flowVersion.Version!);

        if (!result.Success)
            return new ContentResult { Content = BuildSoapFault(result.ErrorMessage ?? "执行失败"), ContentType = "text/xml; charset=utf-8" };

        // 构建 SOAP 响应
        var ns = GetFlowNamespace(key);
        var responseXml = BuildSoapResponse(result.OutputData, ns, key);
        return new ContentResult { Content = responseXml, ContentType = "text/xml; charset=utf-8" };
    }

    // ──────────────────────────────────────────────
    // WSDL 生成
    // ──────────────────────────────────────────────

    private string GenerateWsdl(FlowDefinitionEntity def, FlowVersionEntity ver, string endpointUrl, string flowKey)
    {
        var inputParams = GetFlowParams(def.Id, 5);
        var outputParams = GetFlowParams(def.Id, 6);
        var ns = GetFlowNamespace(flowKey);
        var wsdlNs = "http://schemas.xmlsoap.org/wsdl/";
        var soapNs = "http://schemas.xmlsoap.org/wsdl/soap/";
        var xsdNs = "http://www.w3.org/2001/XMLSchema";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine($"<definitions name=\"{flowKey}\" targetNamespace=\"{ns}\" xmlns=\"{wsdlNs}\" xmlns:soap=\"{soapNs}\" xmlns:tns=\"{ns}\" xmlns:xsd=\"{xsdNs}\">");

        // Types
        sb.AppendLine("  <types>");
        sb.AppendLine($"    <xsd:schema targetNamespace=\"{ns}\" elementFormDefault=\"qualified\">");
        if (inputParams.Count > 0)
        {
            sb.AppendLine($"      <xsd:element name=\"Request\">");
            sb.AppendLine("        <xsd:complexType>");
            sb.AppendLine("          <xsd:sequence>");
            foreach (var p in inputParams)
                sb.AppendLine($"            <xsd:element name=\"{EscapeXml(p.ParamCode)}\" type=\"xsd:{MapXsdType(p.DataType)}\" minOccurs=\"{(p.Required == 1 ? "1" : "0")}\" />");
            sb.AppendLine("          </xsd:sequence>");
            sb.AppendLine("        </xsd:complexType>");
            sb.AppendLine("      </xsd:element>");
        }
        if (outputParams.Count > 0)
        {
            sb.AppendLine($"      <xsd:element name=\"Response\">");
            sb.AppendLine("        <xsd:complexType>");
            sb.AppendLine("          <xsd:sequence>");
            foreach (var p in outputParams)
                sb.AppendLine($"            <xsd:element name=\"{EscapeXml(p.ParamCode)}\" type=\"xsd:{MapXsdType(p.DataType)}\" minOccurs=\"0\" />");
            sb.AppendLine("          </xsd:sequence>");
            sb.AppendLine("        </xsd:complexType>");
            sb.AppendLine("      </xsd:element>");
        }
        sb.AppendLine("    </xsd:schema>");
        sb.AppendLine("  </types>");

        // Messages
        sb.AppendLine($"  <message name=\"RequestMessage\">");
        sb.AppendLine($"    <part name=\"parameters\" element=\"tns:Request\" />");
        sb.AppendLine($"  </message>");
        sb.AppendLine($"  <message name=\"ResponseMessage\">");
        sb.AppendLine($"    <part name=\"parameters\" element=\"tns:Response\" />");
        sb.AppendLine($"  </message>");

        // PortType
        sb.AppendLine($"  <portType name=\"{flowKey}Port\">");
        sb.AppendLine($"    <operation name=\"Trigger\">");
        sb.AppendLine("      <input message=\"tns:RequestMessage\" />");
        sb.AppendLine("      <output message=\"tns:ResponseMessage\" />");
        sb.AppendLine("    </operation>");
        sb.AppendLine("  </portType>");

        // Binding — SOAP 1.1
        sb.AppendLine($"  <binding name=\"{flowKey}Binding\" type=\"tns:{flowKey}Port\">");
        sb.AppendLine($"    <soap:binding transport=\"http://schemas.xmlsoap.org/soap/http\" style=\"document\" />");
        sb.AppendLine($"    <operation name=\"Trigger\">");
        sb.AppendLine($"      <soap:operation soapAction=\"{ns}/Trigger\" />");
        sb.AppendLine("      <input>");
        sb.AppendLine("        <soap:body use=\"literal\" />");
        sb.AppendLine("      </input>");
        sb.AppendLine("      <output>");
        sb.AppendLine("        <soap:body use=\"literal\" />");
        sb.AppendLine("      </output>");
        sb.AppendLine("    </operation>");
        sb.AppendLine("  </binding>");

        // Service
        sb.AppendLine($"  <service name=\"{flowKey}\">");
        sb.AppendLine($"    <port name=\"{flowKey}Port\" binding=\"tns:{flowKey}Binding\">");
        sb.AppendLine($"      <soap:address location=\"{EscapeXml(endpointUrl)}\" />");
        sb.AppendLine("    </port>");
        sb.AppendLine("  </service>");

        sb.AppendLine("</definitions>");
        return sb.ToString();
    }

    private List<(string ParamCode, string ParamName, string DataType, int Required)> GetFlowParams(long ownerId, int paramType)
    {
        return _db.Parameters
            .Where(p => p.OwnerId == ownerId && p.ParamType == paramType && p.Deleted == 0)
            .OrderBy(p => p.SortNum)
            .AsEnumerable()
            .Select(p => (p.ParamCode ?? "", p.ParamName ?? "", NormalizeWsdlType(p.DataType ?? "string"), p.Required))
            .ToList();
    }

    private static string NormalizeWsdlType(string t)
    {
        return (t?.ToLower()) switch
        {
            "integer" or "int" or "long" => "integer",
            "double" or "float" or "decimal" or "number" => "double",
            "boolean" or "bool" => "boolean",
            "date" or "datetime" => "date",
            "array" or "object" or "json" => "string",
            _ => "string"
        };
    }

    private static string MapXsdType(string t)
    {
        return (t?.ToLower()) switch
        {
            "integer" or "int" or "long" => "integer",
            "double" or "float" or "decimal" or "number" => "double",
            "boolean" or "bool" => "boolean",
            "date" or "datetime" => "date",
            _ => "string"
        };
    }

    private static string GetFlowNamespace(string flowKey)
    {
        var validKey = flowKey.Replace("-", "").Replace("_", "");
        return $"http://juggle.local/{validKey}";
    }

    private static string EscapeXml(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    // ──────────────────────────────────────────────
    // SOAP 解析与构建
    // ──────────────────────────────────────────────

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

        foreach (var el in firstChild.Elements())
        {
            result[el.Name.LocalName] = el.HasElements ? (object)el.ToString() : el.Value;
        }
        return result;
    }

    private static string BuildSoapResponse(Dictionary<string, object?> outputData, string ns, string flowKey)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:tns=\"" + EscapeXml(ns) + "\">");
        sb.AppendLine("  <soap:Body>");
        sb.AppendLine($"    <tns:TriggerResponse xmlns:tns=\"{EscapeXml(ns)}\">");
        foreach (var kv in outputData)
        {
            sb.AppendLine($"      <tns:{EscapeXml(kv.Key)}>{EscapeXml(kv.Value?.ToString() ?? "")}</tns:{EscapeXml(kv.Key)}>");
        }
        sb.AppendLine("    </tns:TriggerResponse>");
        sb.AppendLine("  </soap:Body>");
        sb.AppendLine("</soap:Envelope>");
        return sb.ToString();
    }

    private static string BuildSoapFault(string message)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
  <soap:Body>
    <soap:Fault>
      <faultcode>soap:Client</faultcode>
      <faultstring>{EscapeXml(message)}</faultstring>
    </soap:Fault>
  </soap:Body>
</soap:Envelope>";
    }

    // ──────────────────────────────────────────────
    // 内部实现
    // ──────────────────────────────────────────────

    private async Task<ApiResult> TriggerFlowByVersion(
        string version, string key, Dictionary<string, object?> inputParams)
    {
        var flowVersion = await _db.FlowVersions
            .FirstOrDefaultAsync(v => v.FlowKey == key && v.Version == version
                                   && v.Status == 1 && v.Deleted == 0);
        if (flowVersion == null)
            return ApiResult.Fail("流程版本不存在或已禁用");

        var definition = await _db.FlowDefinitions
            .FirstOrDefaultAsync(f => f.FlowKey == key && f.Deleted == 0);
        if (definition == null)
            return ApiResult.Fail("流程定义不存在");

        var result = await _flowExec.RunAsync(
            definition, flowVersion.FlowContent!, inputParams, "open", version);

        return result.Success
            ? ApiResult.Success(result.OutputData)
            : ApiResult.Fail(result.ErrorMessage ?? "执行失败");
    }

    private async Task<ApiResult> TriggerFlowLatest(
        string key, Dictionary<string, object?> inputParams)
    {
        // 取最新已发布（status=1）版本
        var flowVersion = await _db.FlowVersions
            .Where(v => v.FlowKey == key && v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();

        if (flowVersion == null)
            return ApiResult.Fail("未找到已发布的流程版本");

        var definition = await _db.FlowDefinitions
            .FirstOrDefaultAsync(f => f.FlowKey == key && f.Deleted == 0);
        if (definition == null)
            return ApiResult.Fail("流程定义不存在");

        var result = await _flowExec.RunAsync(
            definition, flowVersion.FlowContent!, inputParams, "open", flowVersion.Version!);

        return result.Success
            ? ApiResult.Success(result.OutputData)
            : ApiResult.Fail(result.ErrorMessage ?? "执行失败");
    }
}

