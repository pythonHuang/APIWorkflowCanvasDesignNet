using Juggle.Application.Models.Response;
using Juggle.Application.Services;
using Juggle.Application.Services.Impl;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

/// <summary>
/// AI 大模型控制器：供应商管理 + 需求对话生成接口流程编排 + 接口智能接入。
/// 支持任意 OpenAI 兼容接口（DeepSeek / 通义千问 / Kimi / OpenAI 等）。
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiService _aiService;
    private readonly JuggleDbContext _db;
    private readonly ITenantAccessor _tenant;
    private readonly IHttpClientFactory _httpClientFactory;

    public AiController(AiService aiService, JuggleDbContext db, ITenantAccessor tenant, IHttpClientFactory httpClientFactory)
    {
        _aiService = aiService;
        _db = db;
        _tenant = tenant;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>拉取供应商可用模型列表（兼作配置测试：密钥正确返回模型列表）</summary>
    [HttpPost("fetch-models")]
    public async Task<ApiResult> FetchModels([FromBody] AiFetchModelsRequest req)
    {
        try
        {
            var url = (req.BaseUrl ?? "").Trim().TrimEnd('/');
            if (!url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
                url += "/models";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(req.ApiKey))
                msg.Headers.TryAddWithoutValidation("Authorization", $"Bearer {req.ApiKey.Trim()}");
            using var resp = await client.SendAsync(msg);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"接口返回 {(int)resp.StatusCode}: {Truncate(body, 200)}");
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var m in dataEl.EnumerateArray())
                {
                    var id = m.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (!string.IsNullOrEmpty(id) && !models.Contains(id)) models.Add(id);
                }
            }
            return ApiResult.Success(new { models });
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"配置测试失败: {ex.Message}");
        }
    }

    private static string Truncate(string s, int len) => s.Length <= len ? s : s[..len] + "...";

    // ==================== 供应商管理 ====================

    /// <summary>供应商列表（含启停状态）</summary>
    [HttpGet("providers")]
    public async Task<ApiResult> Providers()
        => ApiResult.Success(await _aiService.GetProvidersAsync());

    /// <summary>启用的供应商（下拉选择用）</summary>
    [HttpGet("providers/enabled")]
    public async Task<ApiResult> EnabledProviders()
        => ApiResult.Success(await _aiService.GetEnabledProvidersAsync());

    /// <summary>新增/更新供应商</summary>
    [HttpPost("provider/save")]
    public async Task<ApiResult> SaveProvider([FromBody] AiProviderSaveRequest req)
    {
        AiProviderEntity entity;
        if (req.Id > 0)
        {
            entity = await _db.AiProviders.FindAsync(req.Id) ?? throw new Exception("供应商不存在");
        }
        else
        {
            entity = new AiProviderEntity { CreatedAt = DateTime.Now.ToString("o"), TenantId = _tenant.TenantId };
            _db.AiProviders.Add(entity);
        }
        entity.ProviderName = req.ProviderName;
        entity.BaseUrl = req.BaseUrl;
        entity.ApiKey = req.ApiKey;
        entity.Model = req.Model;
        entity.Models = req.Models;
        entity.Enabled = req.Enabled ? 1 : 0;
        entity.Remark = req.Remark;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    /// <summary>删除供应商</summary>
    [HttpDelete("provider/{id}")]
    public async Task<ApiResult> DeleteProvider(long id)
    {
        var entity = await _db.AiProviders.FindAsync(id);
        if (entity == null) return ApiResult.Fail("供应商不存在");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>启用/禁用供应商</summary>
    [HttpPost("provider/toggle")]
    public async Task<ApiResult> ToggleProvider([FromBody] AiProviderToggleRequest req)
    {
        var entity = await _db.AiProviders.FindAsync(req.Id);
        if (entity == null) return ApiResult.Fail("供应商不存在");
        entity.Enabled = req.Enabled ? 1 : 0;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>读取 AI 模型配置</summary>
    [HttpGet("config")]
    public async Task<ApiResult> GetConfig()
    {
        try
        {
            return ApiResult.Success(await _aiService.GetConfigAsync());
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>保存 AI 模型配置（OpenAI 兼容接口地址/密钥/模型名）</summary>
    [HttpPost("config")]
    public async Task<ApiResult> SaveConfig([FromBody] AiConfigRequest req)
    {
        try
        {
            await _aiService.SaveConfigAsync(req.BaseUrl, req.ApiKey, req.Model);
            return ApiResult.Success();
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>根据需求对话生成接口流程编排 JSON</summary>
    [HttpPost("generate-flow")]
    public async Task<ApiResult> GenerateFlow([FromBody] AiGenerateFlowRequest req)
    {
        try
        {
            var flow = await _aiService.GenerateFlowAsync(req.Requirement, req.Apis, req.InputParams, req.OutputParams, req.ProviderId, req.Model);
            return ApiResult.Success(flow);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>确认生成流程：创建流程定义并写入编排内容</summary>
    [HttpPost("apply-flow")]
    public async Task<ApiResult> ApplyFlow([FromBody] AiApplyFlowRequest req)
    {
        try
        {
            var result = await _aiService.ApplyFlowAsync(req.FlowName, req.FlowDesc, req.GroupName, req.Nodes);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>根据需求对话生成套件与接口（预览）</summary>
    [HttpPost("generate-apis")]
    public async Task<ApiResult> GenerateApis([FromBody] AiGenerateApisRequest req)
    {
        try
        {
            var result = await _aiService.GenerateApisAsync(req.Requirement, req.ExistingSuites, req.ProviderId, req.Model);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>确认接入：套件与接口写入数据库</summary>
    [HttpPost("apply-apis")]
    public async Task<ApiResult> ApplyApis([FromBody] AiApplyApisRequest req)
    {
        try
        {
            var result = await _aiService.ApplyApisAsync(req.Suites, req.Apis);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }
}

public class AiConfigRequest
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
}

public class AiGenerateFlowRequest
{
    public string Requirement { get; set; } = "";
    public long ProviderId { get; set; }
    public string? Model { get; set; }
    public List<Dictionary<string, object?>>? Apis { get; set; }
    public List<Dictionary<string, object?>>? InputParams { get; set; }
    public List<Dictionary<string, object?>>? OutputParams { get; set; }
}

public class AiApplyFlowRequest
{
    public string FlowName { get; set; } = "";
    public string? FlowDesc { get; set; }
    public string? GroupName { get; set; }
    public List<Dictionary<string, object?>>? Nodes { get; set; }
}

public class AiGenerateApisRequest
{
    public string Requirement { get; set; } = "";
    public long ProviderId { get; set; }
    public string? Model { get; set; }
    public List<Dictionary<string, object?>>? ExistingSuites { get; set; }
}

public class AiApplyApisRequest
{
    public List<Dictionary<string, object?>>? Suites { get; set; }
    public List<Dictionary<string, object?>>? Apis { get; set; }
}

public class AiProviderSaveRequest
{
    public long Id { get; set; }
    public string ProviderName { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public string Models { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string? Remark { get; set; }
}

public class AiProviderToggleRequest
{
    public long Id { get; set; }
    public bool Enabled { get; set; }
}

public class AiFetchModelsRequest
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
}
