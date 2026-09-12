namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>大模型调用请求（AI 节点与文件解析图片识别共用）。</summary>
public record AiChatRequest(string SystemPrompt, string UserInput, List<string> Images, long ProviderId, string? Model,
    double? Temperature = null, int? MaxTokens = null, int? Seed = null, bool EnableThinking = false);

/// <summary>
/// AI 大模型节点执行器：把输入变量内容作为用户消息、按系统提示词调用大模型，
/// 回复写入输出变量（支持选择供应商/模型、图片输入、输出到变量/入参/出参）。
/// 对话函数由应用层注入（供应商/模型配置见系统设置 → 大模型设置）。
/// </summary>
public class AiNodeExecutor : INodeExecutor
{
    private readonly Func<AiChatRequest, Task<string>> _chat;
    private readonly Func<List<long>, Task<string>>? _skillResolver;

    public AiNodeExecutor(Func<AiChatRequest, Task<string>> chat, Func<List<long>, Task<string>>? skillResolver = null)
    {
        _chat = chat;
        _skillResolver = skillResolver;
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

        var reply = await _chat(new AiChatRequest(systemPrompt, input, images, cfg.ProviderId, string.IsNullOrWhiteSpace(cfg.Model) ? null : cfg.Model,
            cfg.Temperature, cfg.MaxTokens, cfg.Seed, cfg.EnableThinking));

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
