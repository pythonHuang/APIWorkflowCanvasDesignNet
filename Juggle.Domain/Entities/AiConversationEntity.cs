namespace Juggle.Domain.Entities;

/// <summary>
/// AI 助手对话会话（多轮对话）
/// 记录助手配置快照（系统提示词/输入输出参数）、完整消息历史与最终输出，
/// 支持进行中/已结束两种状态，已结束会话可在历史中查看。
/// </summary>
public class AiConversationEntity : BaseEntity
{
    /// <summary>助手 ID</summary>
    public long AssistantId { get; set; }

    /// <summary>助手名称快照</summary>
    public string? AssistantName { get; set; }

    /// <summary>系统提示词快照</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>输入参数快照（JSON）</summary>
    public string? InputParams { get; set; }

    /// <summary>输出参数快照（JSON）</summary>
    public string? OutputParams { get; set; }

    /// <summary>消息历史（JSON: [{role,content}]）</summary>
    public string? Messages { get; set; }

    /// <summary>最终输出（JSON，结束时解析输出参数得到）</summary>
    public string? Outputs { get; set; }

    /// <summary>使用的供应商 ID</summary>
    public long ProviderId { get; set; }

    /// <summary>使用的模型</summary>
    public string? Model { get; set; }

    /// <summary>会话标题（首条用户消息截断）</summary>
    public string? Title { get; set; }

    /// <summary>状态：0=进行中 1=已结束</summary>
    public int Status { get; set; } = 0;
}
