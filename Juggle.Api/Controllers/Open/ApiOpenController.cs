using System.Text;
using System.Text.Json;
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

    private static Dictionary<string, object?> ToObjectDict(Dictionary<string, string> dict)
    {
        return dict.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
    }
}
