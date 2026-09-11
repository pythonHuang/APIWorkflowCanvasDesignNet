namespace Juggle.Domain.Entities;

/// <summary>
/// AI 技能（Skill）：可复用的提示词技能/工具定义。
/// 可被大模型节点（AI 节点）勾选，执行时技能内容拼入系统提示词；
/// 支持单个/批量导入导出，进入技能市场流转。
/// </summary>
public class SkillEntity : BaseEntity
{
    /// <summary>技能名称</summary>
    public string? SkillName { get; set; }

    /// <summary>技能分组</summary>
    public string? GroupName { get; set; }

    /// <summary>技能描述（模型选择依据）</summary>
    public string? Description { get; set; }

    /// <summary>技能内容（提示词/markdown）</summary>
    public string? Content { get; set; }

    /// <summary>是否启用：1=启用 0=禁用</summary>
    public int Enabled { get; set; } = 1;

    /// <summary>来源市场条目 ID（来自市场导入时记录）</summary>
    public long? MarketSkillId { get; set; }
}
