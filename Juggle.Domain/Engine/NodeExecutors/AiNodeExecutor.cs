namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// AI 大模型节点执行器：把输入变量内容作为用户消息、按系统提示词调用大模型，
/// 回复写入输出变量。对话函数由应用层注入（供应商/模型配置见系统设置 → 大模型设置）。
/// </summary>
public class AiNodeExecutor : INodeExecutor
{
    private readonly Func<string, string, Task<string>> _chat;

    public AiNodeExecutor(Func<string, string, Task<string>> chat) => _chat = chat;

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.AiConfig
            ?? throw new InvalidOperationException($"AI 节点 [{node.Key}] 未配置 aiConfig。");

        var input = string.IsNullOrWhiteSpace(cfg.Input) ? "" : context.GetVariable(cfg.Input)?.ToString() ?? "";
        var systemPrompt = string.IsNullOrWhiteSpace(cfg.SystemPrompt) ? "你是一个智能助手。" : cfg.SystemPrompt;

        var reply = await _chat(systemPrompt, input);

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, reply);

        return node.Outgoings.FirstOrDefault();
    }
}
