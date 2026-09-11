namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// 知识库检索节点：按查询内容从知识库检索相关片段，拼接为上下文文本写入输出变量
/// （供 AI 大模型节点做 RAG 问答）。检索逻辑由应用层 KnowledgeService 注入。
/// </summary>
public class KbSearchNodeExecutor : INodeExecutor
{
    private readonly Func<long, string, int, Task<string>> _search;

    public KbSearchNodeExecutor(Func<long, string, int, Task<string>> search) => _search = search;

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.KbSearchConfig
            ?? throw new InvalidOperationException($"知识库检索节点 [{node.Key}] 未配置 kbSearchConfig。");
        if (cfg.KbId <= 0)
            throw new InvalidOperationException($"知识库检索节点 [{node.Key}] 未配置知识库。");

        var query = string.IsNullOrWhiteSpace(cfg.Query)
            ? ""
            : RedisGetNodeExecutor.RenderTemplate(cfg.Query, context);

        var contextText = await _search(cfg.KbId, query, cfg.TopK > 0 ? cfg.TopK : 5);

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, contextText);

        return node.Outgoings.FirstOrDefault();
    }
}
