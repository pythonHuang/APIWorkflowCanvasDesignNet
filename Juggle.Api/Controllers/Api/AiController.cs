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
        entity.Capabilities = req.Capabilities;
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
            var result = await _aiService.ApplyFlowAsync(req.FlowName, req.FlowDesc, req.GroupName, req.Nodes, req.InputParams, req.OutputParams);
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

    // ==================== 自定义助手 ====================

    /// <summary>助手列表（管理页）</summary>
    [HttpGet("assistants")]
    public async Task<ApiResult> Assistants()
        => ApiResult.Success(await _db.AiAssistants.Where(a => a.Deleted == 0).OrderByDescending(a => a.Id).ToListAsync());

    /// <summary>启用的助手（菜单用）</summary>
    [HttpGet("assistants/enabled")]
    public async Task<ApiResult> EnabledAssistants()
        => ApiResult.Success(await _db.AiAssistants.Where(a => a.Deleted == 0 && a.Enabled == 1).OrderBy(a => a.Id).ToListAsync());

    /// <summary>新增/更新助手</summary>
    [HttpPost("assistant/save")]
    public async Task<ApiResult> SaveAssistant([FromBody] AiAssistantSaveRequest req)
    {
        AiAssistantEntity entity;
        if (req.Id > 0)
        {
            entity = await _db.AiAssistants.FindAsync(req.Id) ?? throw new Exception("助手不存在");
        }
        else
        {
            entity = new AiAssistantEntity { CreatedAt = DateTime.Now.ToString("o"), TenantId = _tenant.TenantId };
            _db.AiAssistants.Add(entity);
        }
        entity.AssistantName = req.AssistantName;
        entity.Description = req.Description;
        entity.SystemPrompt = req.SystemPrompt;
        entity.InputParams = req.InputParams;
        entity.OutputParams = req.OutputParams;
        entity.QuickPrompts = req.QuickPrompts;
        entity.Icon = req.Icon;
        entity.Capabilities = req.Capabilities;
        entity.ProviderId = req.ProviderId;
        entity.Model = string.IsNullOrWhiteSpace(req.Model) ? null : req.Model.Trim();
        entity.Temperature = req.Temperature;
        entity.MaxTokens = req.MaxTokens is > 0 ? req.MaxTokens : null;
        entity.Seed = req.Seed is > 0 ? req.Seed : null;
        entity.EnableThinking = req.EnableThinking ? 1 : 0;
        entity.Enabled = req.Enabled ? 1 : 0;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    /// <summary>用大模型优化系统提示词（草稿 → 结构化润色）</summary>
    [HttpPost("optimize-prompt")]
    public async Task<ApiResult> OptimizePrompt([FromBody] AiOptimizePromptRequest req)
    {
        try
        {
            var system = (req.Purpose ?? "") == "description"
                ? "你是文案专家。请把用户提供的内容优化为一句简洁、准确、易检索的中文描述（30字以内，直接输出描述文本，不要解释、不要 markdown 围栏、不要引号）。"
                : "你是提示词优化专家。请把用户提供的提示词草稿优化为结构清晰、约束明确、可稳定执行的高质量系统提示词（保持原语言，直接输出优化后的提示词文本，不要解释、不要 markdown 围栏）。";
            var result = await _aiService.ChatAsync(system, req.Prompt ?? "", req.ProviderId, modelOverride: req.Model);
            return ApiResult.Success(new { prompt = result });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>用大模型生成助手图标（返回 emoji）</summary>
    [HttpPost("generate-icon")]
    public async Task<ApiResult> GenerateIcon([FromBody] AiGenerateIconRequest req)
    {
        try
        {
            var system = "你是图标设计助手。根据助手名称与描述，只输出一个最贴切的 emoji 字符（不要任何其他文字、引号或解释）。";
            var result = await _aiService.ChatAsync(system, $"助手名称：{req.Name}\n描述：{req.Description}", req.ProviderId, modelOverride: req.Model);
            var icon = result.Trim().Trim('"', '\'', '`');
            // 提取首个 emoji 字符
            if (icon.Length > 2) icon = icon[..2];
            return ApiResult.Success(new { icon });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>删除助手</summary>
    [HttpDelete("assistant/{id}")]
    public async Task<ApiResult> DeleteAssistant(long id)
    {
        var entity = await _db.AiAssistants.FindAsync(id);
        if (entity == null) return ApiResult.Fail("助手不存在");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>启用/禁用助手</summary>
    [HttpPost("assistant/toggle")]
    public async Task<ApiResult> ToggleAssistant([FromBody] AiAssistantToggleRequest req)
    {
        var entity = await _db.AiAssistants.FindAsync(req.Id);
        if (entity == null) return ApiResult.Fail("助手不存在");
        entity.Enabled = req.Enabled ? 1 : 0;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>运行助手：输入参数 + 补充说明 → 大模型 → 输出参数(JSON)与原文</summary>
    [HttpPost("assistant-run")]
    public async Task<ApiResult> RunAssistant([FromBody] AiAssistantRunRequest req)
    {
        try
        {
            var assistant = await _db.AiAssistants.FirstOrDefaultAsync(a => a.Id == req.AssistantId && a.Deleted == 0)
                ?? throw new Exception("助手不存在");
            var result = await _aiService.RunAssistantAsync(assistant, req.Inputs, req.ExtraText, req.ProviderId, req.Model);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    // ==================== 报表智能生成 ====================

    /// <summary>根据需求生成报表（数据集/查询参数/排版），供预览确认后入库</summary>
    [HttpPost("generate-report")]
    public async Task<ApiResult> GenerateReport([FromBody] AiGenerateReportRequest req)
    {
        try
        {
            var result = await _aiService.GenerateReportAsync(req.Requirement, req.ReportName,
                req.DataViews, req.DataSources, req.Flows, req.Apis, req.ProviderId, req.Model);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    // ==================== 多轮对话 ====================

    /// <summary>开启新对话（快照助手配置与输入参数）</summary>
    [HttpPost("conversation/start")]
    public async Task<ApiResult> StartConversation([FromBody] AiConversationStartRequest req)
    {
        try
        {
            var assistant = await _db.AiAssistants.FirstOrDefaultAsync(a => a.Id == req.AssistantId && a.Deleted == 0)
                ?? throw new Exception("助手不存在");
            var conv = await _aiService.StartConversationAsync(assistant, req.Inputs, req.ProviderId, req.Model);
            return ApiResult.Success(new { conv.Id, conv.Title, conv.Status });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>多轮对话：发送消息（携带完整历史）</summary>
    [HttpPost("conversation/chat")]
    public async Task<ApiResult> ConversationChat([FromBody] AiConversationChatRequest req)
    {
        try
        {
            var conv = await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == req.ConversationId && c.Deleted == 0)
                ?? throw new Exception("会话不存在");
            if (conv.Status == 1) throw new Exception("会话已结束，请开启新对话");
            var reply = await _aiService.ConversationChatAsync(conv, req.Content, req.ProviderId, req.Model);
            return ApiResult.Success(new { reply });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>结束对话：有输出参数时生成最终结果，会话标记已结束</summary>
    [HttpPost("conversation/end")]
    public async Task<ApiResult> EndConversation([FromBody] AiConversationEndRequest req)
    {
        try
        {
            var conv = await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == req.ConversationId && c.Deleted == 0)
                ?? throw new Exception("会话不存在");
            var result = await _aiService.EndConversationAsync(conv, req.ProviderId, req.Model);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>会话历史列表（按助手）</summary>
    [HttpGet("conversations/{assistantId}")]
    public async Task<ApiResult> Conversations(long assistantId)
        => ApiResult.Success(await _db.AiConversations
            .Where(c => c.Deleted == 0 && c.AssistantId == assistantId)
            .OrderByDescending(c => c.Id)
            .Select(c => new { c.Id, c.Title, c.Status, c.Model, c.CreatedAt, c.UpdatedAt })
            .Take(200)
            .ToListAsync());

    /// <summary>会话详情（消息历史/最终输出）</summary>
    [HttpGet("conversation/{id}")]
    public async Task<ApiResult> ConversationDetail(long id)
    {
        var conv = await _db.AiConversations.FirstOrDefaultAsync(c => c.Id == id && c.Deleted == 0);
        if (conv == null) return ApiResult.Fail("会话不存在");
        return ApiResult.Success(conv);
    }

    /// <summary>删除会话</summary>
    [HttpDelete("conversation/{id}")]
    public async Task<ApiResult> DeleteConversation(long id)
    {
        var conv = await _db.AiConversations.FindAsync(id);
        if (conv == null) return ApiResult.Fail("会话不存在");
        conv.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
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
    public List<Dictionary<string, object?>>? InputParams { get; set; }
    public List<Dictionary<string, object?>>? OutputParams { get; set; }
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
    public string? Capabilities { get; set; }
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

public class AiAssistantSaveRequest
{
    public long Id { get; set; }
    public string AssistantName { get; set; } = "";
    public string? Description { get; set; }
    public string? SystemPrompt { get; set; }
    public string? InputParams { get; set; }
    public string? OutputParams { get; set; }
    public string? QuickPrompts { get; set; }
    public string? Icon { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>支持的能力（JSON: {skills,apis,flows,tools}）</summary>
    public string? Capabilities { get; set; }
    /// <summary>默认供应商 ID（0=第一个启用供应商）</summary>
    public long ProviderId { get; set; }
    /// <summary>默认模型（空=供应商默认）</summary>
    public string? Model { get; set; }
    /// <summary>温度（0-2，null=服务默认）</summary>
    public double? Temperature { get; set; }
    /// <summary>最大输出字数（0=不限制）</summary>
    public int? MaxTokens { get; set; }
    /// <summary>随机种子（0=随机）</summary>
    public int? Seed { get; set; }
    /// <summary>是否启用深度思考</summary>
    public bool EnableThinking { get; set; }
}

public class AiOptimizePromptRequest
{
    public string? Prompt { get; set; }
    public long ProviderId { get; set; }
    public string? Model { get; set; }
    /// <summary>优化目标：空=系统提示词优化；description=一句话描述优化</summary>
    public string? Purpose { get; set; }
}

public class AiGenerateIconRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public long ProviderId { get; set; }
    public string? Model { get; set; }
}

public class AiAssistantToggleRequest
{
    public long Id { get; set; }
    public bool Enabled { get; set; }
}

public class AiAssistantRunRequest
{
    public long AssistantId { get; set; }
    public long ProviderId { get; set; }
    public string? Model { get; set; }
    public Dictionary<string, object?>? Inputs { get; set; }
    public string? ExtraText { get; set; }
}

public class AiConversationStartRequest
{
    public long AssistantId { get; set; }
    public long ProviderId { get; set; }
    public string? Model { get; set; }
    public Dictionary<string, object?>? Inputs { get; set; }
}

public class AiConversationChatRequest
{
    public long ConversationId { get; set; }
    public string Content { get; set; } = "";
    public long ProviderId { get; set; }
    public string? Model { get; set; }
}

public class AiConversationEndRequest
{
    public long ConversationId { get; set; }
    public long ProviderId { get; set; }
    public string? Model { get; set; }
}

public class AiGenerateReportRequest
{
    public string Requirement { get; set; } = "";
    public string? ReportName { get; set; }
    public long ProviderId { get; set; }
    public string? Model { get; set; }
    public List<Dictionary<string, object?>>? DataViews { get; set; }
    public List<Dictionary<string, object?>>? DataSources { get; set; }
    public List<Dictionary<string, object?>>? Flows { get; set; }
    public List<Dictionary<string, object?>>? Apis { get; set; }
}
