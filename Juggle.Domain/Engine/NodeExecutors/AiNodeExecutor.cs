namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>大模型调用请求（AI 节点与文件解析图片识别共用）。</summary>
public record AiChatRequest(string SystemPrompt, string UserInput, List<string> Images, long ProviderId, string? Model,
    double? Temperature = null, int? MaxTokens = null, int? Seed = null, bool EnableThinking = false,
    string? ToolsJson = null, List<Dictionary<string, object?>>? History = null);

/// <summary>大模型回复结果（文本 + 工具调用）。</summary>
public class AiChatResult
{
    public string Text { get; set; } = "";
    public List<AiToolCall> ToolCalls { get; set; } = new();
}

/// <summary>模型发起的工具调用（OpenAI function calling）。</summary>
public class AiToolCall
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
}

/// <summary>
/// AI 大模型节点执行器：把输入变量内容作为用户消息、按系统提示词调用大模型，
/// 回复写入输出变量（支持选择供应商/模型、图片输入、输出到变量/入参/出参）。
/// 支持函数调用（tools）：模型可调用配置的接口/流程工具，执行结果回传后生成最终回答（最多 4 轮）。
/// 对话函数由应用层注入（供应商/模型配置见系统设置 → 大模型设置）。
/// </summary>
public class AiNodeExecutor : INodeExecutor
{
    private readonly Func<AiChatRequest, Task<AiChatResult>> _chat;
    private readonly Func<List<long>, Task<string>>? _skillResolver;
    /// <summary>工具定义解析（toolApis,toolFlows → OpenAI tools JSON 数组字符串）</summary>
    private readonly Func<string, string, Task<string>>? _toolsResolver;
    /// <summary>工具执行（工具名, 参数 JSON → 结果文本）</summary>
    private readonly Func<string, string, Task<string>>? _toolRunner;

    public AiNodeExecutor(Func<AiChatRequest, Task<AiChatResult>> chat,
        Func<List<long>, Task<string>>? skillResolver = null,
        Func<string, string, Task<string>>? toolsResolver = null,
        Func<string, string, Task<string>>? toolRunner = null)
    {
        _chat = chat;
        _skillResolver = skillResolver;
        _toolsResolver = toolsResolver;
        _toolRunner = toolRunner;
    }

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.AiConfig
            ?? throw new InvalidOperationException($"AI 节点 [{node.Key}] 未配置 aiConfig。");

        var input = string.IsNullOrWhiteSpace(cfg.Input) ? "" : context.GetVariable(cfg.Input)?.ToString() ?? "";
        var systemPrompt = string.IsNullOrWhiteSpace(cfg.SystemPrompt) ? "你是一个智能助手。" : cfg.SystemPrompt;

        // 已勾选技能：内容拼入系统提示词
        var skillIds = new List<long>();
        foreach (var s in (cfg.Skills ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (long.TryParse(s, out var id)) skillIds.Add(id);
        }
        if (skillIds.Count > 0 && _skillResolver != null)
        {
            var skillsText = await _skillResolver(skillIds);
            if (!string.IsNullOrWhiteSpace(skillsText))
                systemPrompt = skillsText + "\n\n" + systemPrompt;
        }

        // 图片输入（视觉模型识别）
        var images = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.InputImages))
        {
            foreach (var name in cfg.InputImages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var v = context.GetVariable(name);
                if (v != null)
                {
                    var str = v.ToString() ?? "";
                    if (str.StartsWith("data:image") || str.StartsWith("http"))
                        images.Add(str);
                }
            }
        }

        // 工具（接口/流程函数调用）：解析 OpenAI tools 定义
        var toolsJson = "";
        if (_toolsResolver != null && (!string.IsNullOrWhiteSpace(cfg.ToolApis) || !string.IsNullOrWhiteSpace(cfg.ToolFlows)))
            toolsJson = await _toolsResolver(cfg.ToolApis ?? "", cfg.ToolFlows ?? "");

        // 多轮函数调用循环：模型调用工具 → 执行 → 结果回传 → 最终回答（最多 4 轮）
        var history = new List<Dictionary<string, object?>>();
        AiChatResult? result = null;
        var lastText = "";
        const int maxRounds = 4;
        for (var round = 0; round < maxRounds; round++)
        {
            result = await _chat(new AiChatRequest(systemPrompt, input, images, cfg.ProviderId,
                string.IsNullOrWhiteSpace(cfg.Model) ? null : cfg.Model,
                cfg.Temperature, cfg.MaxTokens, cfg.Seed, cfg.EnableThinking,
                toolsJson.Length > 0 ? toolsJson : null,
                history.Count > 0 ? history : null));

            if (!string.IsNullOrWhiteSpace(result.Text)) lastText = result.Text;
            if (result.ToolCalls.Count == 0) break;

            // 记录 assistant 的 tool_calls 消息
            history.Add(new Dictionary<string, object?>
            {
                ["role"] = "assistant",
                ["content"] = null,
                ["tool_calls"] = result.ToolCalls.Select(t => (object)new Dictionary<string, object?>
                {
                    ["id"] = t.Id,
                    ["type"] = "function",
                    ["function"] = new Dictionary<string, object?> { ["name"] = t.Name, ["arguments"] = t.Arguments }
                }).ToList()
            });

            // 逐个执行工具调用并把结果回传
            foreach (var call in result.ToolCalls)
            {
                var toolResult = "工具执行失败：未配置工具执行器";
                if (_toolRunner != null)
                {
                    try { toolResult = await _toolRunner(call.Name, call.Arguments); }
                    catch (Exception ex) { toolResult = $"工具执行失败：{ex.Message}"; }
                }
                history.Add(new Dictionary<string, object?>
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = call.Id,
                    ["content"] = toolResult
                });
            }
        }

        var reply = string.IsNullOrWhiteSpace(result?.Text) ? lastText : result!.Text;

        if (!string.IsNullOrWhiteSpace(cfg.Output))
        {
            switch ((cfg.OutputTargetType ?? "VARIABLE").ToUpper())
            {
                case "OUTPUT":
                    context.SetOutputParameter(cfg.Output, reply);
                    break;
                case "INPUT":
                    context.SetVariable(cfg.Output, reply);
                    break;
                default:
                    context.SetVariable(cfg.Output, reply);
                    break;
            }
        }

        return node.Outgoings.FirstOrDefault();
    }
}
