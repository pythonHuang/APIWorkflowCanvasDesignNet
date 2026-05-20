using Juggle.Application.Models.Request;
using Juggle.Application.Models.Response;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Juggle.Api.Controllers.Api;

[ApiController]
[Route("api/suite/api")]
[Authorize]
public class ApiController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;

    public ApiController(JuggleDbContext db, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
    }

    [HttpPost("add")]
    public async Task<ApiResult> Add([FromBody] ApiAddRequest req)
    {
        // 检查套件是否存在
        var suite = await _db.Suites.FirstOrDefaultAsync(s => s.SuiteCode == req.SuiteCode && s.Deleted == 0);
        if (suite == null) return ApiResult.Fail("套件不存在");

        var code = $"api_{Guid.NewGuid():N}";
        var entity = new ApiEntity
        {
            SuiteCode = req.SuiteCode,
            MethodCode = code,
            MethodName = req.MethodName,
            MethodDesc = req.MethodDesc,
            Url = req.Url,
            RequestType = req.RequestType,
            ContentType = req.ContentType,
            MockJson = req.MockJson,
            MethodType = req.MethodType,
            SoapVersion = req.SoapVersion,
            SoapMethod = req.SoapMethod,
            SoapNamespace = req.SoapNamespace,
            SoapAction = req.SoapAction,
            CreatedAt = DateTime.Now.ToString("o")
        };
        _db.Apis.Add(entity);
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var entity = await _db.Apis.FindAsync(id);
        if (entity == null) return ApiResult.Fail("接口不存在");
        entity.Deleted = 1;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpPut("update")]
    public async Task<ApiResult> Update([FromBody] ApiUpdateRequest req)
    {
        var entity = await _db.Apis.FindAsync(req.Id);
        if (entity == null) return ApiResult.Fail("接口不存在");
        entity.MethodName = req.MethodName;
        entity.MethodDesc = req.MethodDesc;
        entity.Url = req.Url;
        entity.RequestType = req.RequestType;
        entity.ContentType = req.ContentType;
        entity.MockJson = req.MockJson;
        entity.MethodType = req.MethodType;
        entity.SoapVersion = req.SoapVersion;
        entity.SoapMethod = req.SoapMethod;
        entity.SoapNamespace = req.SoapNamespace;
        entity.SoapAction = req.SoapAction;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpGet("info/{id}")]
    public async Task<ApiResult> Info(long id)
    {
        var entity = await _db.Apis.FindAsync(id);
        if (entity == null || entity.Deleted == 1) return ApiResult.Fail("接口不存在");
        var inputParams = await _db.Parameters.Where(p => p.OwnerId == id && p.ParamType == 1 && p.Deleted == 0).OrderBy(p => p.SortNum).ToListAsync();
        var outputParams = await _db.Parameters.Where(p => p.OwnerId == id && p.ParamType == 2 && p.Deleted == 0).OrderBy(p => p.SortNum).ToListAsync();
        var headerParams = await _db.Parameters.Where(p => p.OwnerId == id && p.ParamType == 4 && p.Deleted == 0).OrderBy(p => p.SortNum).ToListAsync();
        return ApiResult.Success(new { api = entity, inputParams, outputParams, headerParams });
    }

    [HttpPost("list")]
    public async Task<ApiResult> List([FromBody] dynamic req)
    {
        string suiteCode = req.GetProperty("suiteCode").GetString() ?? "";
        var list = await _db.Apis.Where(a => a.SuiteCode == suiteCode && a.Deleted == 0).OrderBy(a => a.Id).ToListAsync();
        return ApiResult.Success(list);
    }

    [HttpPost("debug")]
    public async Task<ApiResult> Debug([FromBody] ApiDebugRequest req)
    {
        var api = await _db.Apis.FindAsync(req.ApiId);
        if (api == null) return ApiResult.Fail("接口不存在");

        var methodType = api.MethodType?.ToUpper() ?? "HTTP";

        try
        {
            // Mock 模式：如果接口配置了 MockJson，直接返回预设数据
            if (!string.IsNullOrEmpty(api.MockJson))
            {
                return ApiResult.Success(new { response = api.MockJson, mock = true });
            }

            // WebService（SOAP 1.1 / 1.2）调试
            if (methodType == "WEBSERVICE")
            {
                return await DebugWebService(api, req);
            }

            // HTTP 调用（原有逻辑）
            var client = _httpClientFactory.CreateClient();
            foreach (var h in req.Headers)
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value?.ToString());

            // 加载参数定义，获取位置信息
            var paramDefs = await _db.Parameters
                .Where(p => p.OwnerId == api.Id && p.ParamType == 1 && p.Deleted == 0)
                .ToListAsync();
            var posMap = paramDefs.ToDictionary(p => p.ParamCode!, p => p.ParamPosition ?? "");

            string responseJson;
            var requestType = api.RequestType?.ToUpper() ?? "GET";

            // rawBody 参数：整个请求体用该参数的值
            var rawBodyKey = posMap.FirstOrDefault(kv => kv.Value == "rawBody").Key;

            // 分离 query 和 body 参数
            var queryParams = req.Params.Where(kv => posMap.GetValueOrDefault(kv.Key, "") == "query").ToList();
            var bodyParams = req.Params.Where(kv => posMap.GetValueOrDefault(kv.Key, "") != "query").ToList();

            if (requestType == "GET" || requestType == "DELETE")
            {
                var url = api.Url!;
                if (req.Params.Count > 0)
                {
                    var query = string.Join("&", req.Params.Select(kv =>
                        $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value?.ToString() ?? "")}"));
                    url = url.Contains('?') ? $"{url}&{query}" : $"{url}?{query}";
                }
                var resp = requestType == "GET" ? await client.GetAsync(url) : await client.DeleteAsync(url);
                responseJson = await resp.Content.ReadAsStringAsync();
            }
            else
            {
                HttpContent content;
                if (!string.IsNullOrEmpty(rawBodyKey) && req.Params.ContainsKey(rawBodyKey))
                {
                    // rawBody 模式：直接用值作为整个请求体
                    content = new StringContent(req.Params[rawBodyKey]?.ToString() ?? "",
                        System.Text.Encoding.UTF8, api.ContentType == "XML" ? "application/xml" : "text/plain");
                }
                else
                {
                    // 标准模式：所有非 query 参数序列化为 JSON
                    var bodyDict = bodyParams.ToDictionary(kv => kv.Key, kv => kv.Value);
                    content = new StringContent(
                        JsonSerializer.Serialize(bodyDict),
                        System.Text.Encoding.UTF8, "application/json");
                }
                var resp = requestType == "PUT" ? await client.PutAsync(api.Url, content) : await client.PostAsync(api.Url, content);
                responseJson = await resp.Content.ReadAsStringAsync();
            }
            // URL 中包含 query 参数的处理
            if (queryParams.Any() && !(requestType == "GET" || requestType == "DELETE"))
            {
                // 对于非 GET 请求，将 query 参数附在 URL 上
                // Note: This is already handled above for GET/DELETE
            }

            return ApiResult.Success(new { response = responseJson });
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"调用失败: {ex.Message}");
        }
    }

    /// <summary>调试 WebService（SOAP 1.1 / 1.2）接口</summary>
    private async Task<ApiResult> DebugWebService(ApiEntity api, ApiDebugRequest req)
    {
        var isSoap12 = api.SoapVersion == "12";

        // 操作名称：优先用实体字段，其次 URL ?op= 参数
        var methodName = !string.IsNullOrWhiteSpace(api.SoapMethod) ? api.SoapMethod! : "Request";
        if (string.IsNullOrWhiteSpace(api.SoapMethod))
        {
            var uri = new Uri(api.Url!);
            var queryParams = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            if (queryParams.TryGetValue("op", out var opVal) && !string.IsNullOrEmpty(opVal))
                methodName = opVal!;
        }

        // 命名空间：优先用实体字段，其次取 URL 路径
        var soapNs = !string.IsNullOrWhiteSpace(api.SoapNamespace)
            ? api.SoapNamespace!
            : new Uri(api.Url!).GetLeftPart(UriPartial.Path);

        // SOAPAction：优先用实体字段，其次取 Header 中的
        var soapAction = !string.IsNullOrWhiteSpace(api.SoapAction) ? api.SoapAction! : "";
        if (string.IsNullOrEmpty(soapAction) && req.Headers.ContainsKey("SOAPAction"))
            soapAction = req.Headers["SOAPAction"]?.ToString() ?? "";

        // 构建 XML 参数
        var paramXml = string.Join("", req.Params.Select(kv =>
            $"<{kv.Key}>{System.Security.SecurityElement.Escape(kv.Value?.ToString() ?? "")}</{kv.Key}>"));

        // SOAP 信封命名空间
        var soapEnvNs = isSoap12
            ? "http://www.w3.org/2003/05/soap-envelope"
            : "http://schemas.xmlsoap.org/soap/envelope/";
        var contentType = isSoap12 ? "application/soap+xml" : "text/xml";

        var soapBody = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""{soapEnvNs}"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xmlns:xsd=""http://www.w3.org/2001/XMLSchema"">
  <soap:Body>
    <{methodName} xmlns=""{soapNs}"">
      {paramXml}
    </{methodName}>
  </soap:Body>
</soap:Envelope>";

        var client = _httpClientFactory.CreateClient();
        var content = new StringContent(soapBody, System.Text.Encoding.UTF8, contentType);
        if (!string.IsNullOrEmpty(soapAction))
            content.Headers.Add("SOAPAction", $"\"{soapAction}\"");

        // 附加其他 headers
        foreach (var h in req.Headers)
            if (h.Key != "SOAPAction")
                client.DefaultRequestHeaders.TryAddWithoutValidation(h.Key, h.Value?.ToString());

        var resp = await client.PostAsync(api.Url, content);
        var responseText = await resp.Content.ReadAsStringAsync();
        return ApiResult.Success(new { response = responseText, soapBody });
    }

    /// <summary>导出接口（含参数）</summary>
    [HttpPost("export")]
    public async Task<ApiResult> Export([FromBody] ApiExportRequest req)
    {
        var apis = await _db.Apis
            .Where(a => a.SuiteCode == req.SuiteCode && a.Deleted == 0)
            .OrderBy(a => a.Id)
            .ToListAsync();

        if (req.Ids is { Count: > 0 })
            apis = apis.Where(a => req.Ids.Contains(a.Id)).ToList();

        var result = new List<object>();
        foreach (var api in apis)
        {
            var inputParams = await _db.Parameters
                .Where(p => p.OwnerId == api.Id && p.ParamType == 1 && p.Deleted == 0)
                .OrderBy(p => p.SortNum).ToListAsync();
            var outputParams = await _db.Parameters
                .Where(p => p.OwnerId == api.Id && p.ParamType == 2 && p.Deleted == 0)
                .OrderBy(p => p.SortNum).ToListAsync();
            var headerParams = await _db.Parameters
                .Where(p => p.OwnerId == api.Id && p.ParamType == 4 && p.Deleted == 0)
                .OrderBy(p => p.SortNum).ToListAsync();

            result.Add(new
            {
                api.MethodName, api.MethodDesc, api.Url, api.RequestType, api.ContentType,
                api.MockJson, api.MethodType, api.SoapVersion, api.SoapMethod, api.SoapNamespace, api.SoapAction,
                InputParams = inputParams.Select(p => new
                {
                    p.ParamCode, p.ParamName, p.DataType, p.ObjectCode, p.Required,
                    p.DefaultValue, p.ParamPosition, p.Description
                }),
                OutputParams = outputParams.Select(p => new
                {
                    p.ParamCode, p.ParamName, p.DataType, p.ObjectCode, p.Required,
                    p.DefaultValue, p.ParamPosition, p.Description
                }),
                HeaderParams = headerParams.Select(p => new
                {
                    p.ParamCode, p.ParamName, p.DataType, p.ObjectCode, p.Required,
                    p.DefaultValue, p.ParamPosition, p.Description
                })
            });
        }
        return ApiResult.Success(result);
    }

    /// <summary>导入接口（含参数）</summary>
    [HttpPost("import")]
    public async Task<ApiResult> Import([FromBody] ApiImportRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SuiteCode)) return ApiResult.Fail("套件Code不能为空");
        if (req.Apis == null || req.Apis.Count == 0) return ApiResult.Fail("没有要导入的接口");

        foreach (var item in req.Apis)
        {
            var code = $"api_{Guid.NewGuid():N}";
            var entity = new ApiEntity
            {
                SuiteCode = req.SuiteCode,
                MethodCode = code,
                MethodName = item.MethodName,
                MethodDesc = item.MethodDesc,
                Url = item.Url,
                RequestType = item.RequestType ?? "GET",
                ContentType = item.ContentType,
                MockJson = item.MockJson,
                MethodType = item.MethodType ?? "HTTP",
                SoapVersion = item.SoapVersion,
                SoapMethod = item.SoapMethod,
                SoapNamespace = item.SoapNamespace,
                SoapAction = item.SoapAction,
                CreatedAt = DateTime.Now.ToString("o")
            };
            _db.Apis.Add(entity);
            await _db.SaveChangesAsync();

            // 导入入参
            int sort = 1;
            if (item.InputParams != null)
            {
                foreach (var p in item.InputParams)
                {
                    _db.Parameters.Add(new ParameterEntity
                    {
                        OwnerId = entity.Id, OwnerCode = code, ParamType = 1,
                        ParamCode = p.ParamCode ?? "", ParamName = p.ParamName ?? "",
                        DataType = p.DataType ?? "string", ObjectCode = p.ObjectCode,
                        Required = p.Required, DefaultValue = p.DefaultValue,
                        ParamPosition = p.ParamPosition, Description = p.Description,
                        SortNum = sort++, CreatedAt = DateTime.Now.ToString("o")
                    });
                }
            }
            // 导入出参
            sort = 1;
            if (item.OutputParams != null)
            {
                foreach (var p in item.OutputParams)
                {
                    _db.Parameters.Add(new ParameterEntity
                    {
                        OwnerId = entity.Id, OwnerCode = code, ParamType = 2,
                        ParamCode = p.ParamCode ?? "", ParamName = p.ParamName ?? "",
                        DataType = p.DataType ?? "string", ObjectCode = p.ObjectCode,
                        Required = p.Required, DefaultValue = p.DefaultValue,
                        ParamPosition = p.ParamPosition, Description = p.Description,
                        SortNum = sort++, CreatedAt = DateTime.Now.ToString("o")
                    });
                }
            }
            // 导入 Header
            sort = 1;
            if (item.HeaderParams != null)
            {
                foreach (var p in item.HeaderParams)
                {
                    _db.Parameters.Add(new ParameterEntity
                    {
                        OwnerId = entity.Id, OwnerCode = code, ParamType = 4,
                        ParamCode = p.ParamCode ?? "", ParamName = p.ParamName ?? "",
                        DataType = p.DataType ?? "string", ObjectCode = p.ObjectCode,
                        Required = p.Required, DefaultValue = p.DefaultValue,
                        ParamPosition = p.ParamPosition, Description = p.Description,
                        SortNum = sort++, CreatedAt = DateTime.Now.ToString("o")
                    });
                }
            }
        }
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }
}

public class ApiExportRequest
{
    public string SuiteCode { get; set; } = "";
    public List<long>? Ids { get; set; }
}

public class ApiImportRequest
{
    public string SuiteCode { get; set; } = "";
    public List<ApiImportItem> Apis { get; set; } = new();
}

public class ApiImportItem
{
    public string MethodName { get; set; } = "";
    public string? MethodDesc { get; set; }
    public string Url { get; set; } = "";
    public string? RequestType { get; set; }
    public string? ContentType { get; set; }
    public string? MockJson { get; set; }
    public string? MethodType { get; set; }
    public string? SoapVersion { get; set; }
    public string? SoapMethod { get; set; }
    public string? SoapNamespace { get; set; }
    public string? SoapAction { get; set; }
    public List<ApiImportParam>? InputParams { get; set; }
    public List<ApiImportParam>? OutputParams { get; set; }
    public List<ApiImportParam>? HeaderParams { get; set; }
}

public class ApiImportParam
{
    public string? ParamCode { get; set; }
    public string? ParamName { get; set; }
    public string? DataType { get; set; }
    public string? ObjectCode { get; set; }
    public int Required { get; set; }
    public string? DefaultValue { get; set; }
    public string? ParamPosition { get; set; }
    public string? Description { get; set; }
}
