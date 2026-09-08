using Juggle.Application.Models.Response;
using Juggle.Application.Services.Impl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Juggle.Api.Controllers.Api;

/// <summary>
/// AI 大模型控制器：模型配置 + 需求对话生成接口流程编排。
/// 支持任意 OpenAI 兼容接口（DeepSeek / 通义千问 / Kimi / OpenAI 等）。
/// </summary>
[ApiController]
[Route("api/ai")]
[Authorize]
public class AiController : ControllerBase
{
    private readonly AiService _aiService;

    public AiController(AiService aiService) => _aiService = aiService;

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
            var flow = await _aiService.GenerateFlowAsync(req.Requirement, req.Apis, req.InputParams, req.OutputParams);
            return ApiResult.Success(flow);
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
    public List<Dictionary<string, object?>>? Apis { get; set; }
    public List<Dictionary<string, object?>>? InputParams { get; set; }
    public List<Dictionary<string, object?>>? OutputParams { get; set; }
}
