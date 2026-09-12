namespace Juggle.Domain.Entities;

/// <summary>
/// 自定义智能助手
/// 每个助手可配置名称、系统提示词、输入/输出参数列表，
/// 启用后出现在「模型助手」菜单中，运行页按参数表单输入并调用大模型。
/// </summary>
public class AiAssistantEntity : BaseEntity
{
    /// <summary>助手名称（菜单显示）</summary>
    public string? AssistantName { get; set; }

    /// <summary>助手描述</summary>
    public string? Description { get; set; }

    /// <summary>系统提示词（人设与任务说明）</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>输入参数列表（JSON: [{name,label,type,options,default}]）</summary>
    public string? InputParams { get; set; }

    /// <summary>输出参数列表（JSON: [{name,label,type}]）</summary>
    public string? OutputParams { get; set; }

    /// <summary>辅助提问词列表（JSON 字符串数组，运行页显示为快捷按钮）</summary>
    public string? QuickPrompts { get; set; }

    /// <summary>图标（emoji 或图片 data URL，显示在菜单与运行页）</summary>
    public string? Icon { get; set; }

    /// <summary>是否启用：1=启用（出现在菜单） 0=禁用</summary>
    public int Enabled { get; set; } = 1;

    /// <summary>支持的能力（JSON: {skills:[id],apis:["code"],flows:["key"],tools:["name"]}）</summary>
    public string? Capabilities { get; set; }

    /// <summary>默认供应商 ID（0=第一个启用供应商）</summary>
    public long ProviderId { get; set; }

    /// <summary>默认模型（空=供应商默认模型）</summary>
    public string? Model { get; set; }

    /// <summary>温度（0-2，null=服务默认）</summary>
    public double? Temperature { get; set; }

    /// <summary>最大输出字数（0/null=不限制）</summary>
    public int? MaxTokens { get; set; }

    /// <summary>随机种子（0/null=随机）</summary>
    public int? Seed { get; set; }

    /// <summary>是否启用深度思考（enable_thinking）</summary>
    public int EnableThinking { get; set; }
}
