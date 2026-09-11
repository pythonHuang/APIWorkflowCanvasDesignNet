namespace Juggle.Domain.Entities;

/// <summary>
/// AI 大模型供应商配置
/// 支持 OpenAI 兼容接口（DeepSeek / 通义千问 / Kimi / OpenAI 等），可配置多个供应商并启停。
/// </summary>
public class AiProviderEntity : BaseEntity
{
    /// <summary>供应商名称（如 DeepSeek、通义千问）</summary>
    public string? ProviderName { get; set; }

    /// <summary>接口地址（OpenAI 兼容，如 https://api.deepseek.com/v1）</summary>
    public string? BaseUrl { get; set; }

    /// <summary>API 密钥</summary>
    public string? ApiKey { get; set; }

    /// <summary>默认模型（如 deepseek-chat）</summary>
    public string? Model { get; set; }

    /// <summary>可用模型列表（逗号分隔，如 deepseek-chat,deepseek-reasoner）</summary>
    public string? Models { get; set; }

    /// <summary>是否启用：1=启用 0=禁用</summary>
    public int Enabled { get; set; } = 1;

    /// <summary>支持的能力（JSON: {skills:[id], apis:[methodCode], flows:[flowKey], tools:[工具名]}）</summary>
    public string? Capabilities { get; set; }

    /// <summary>备注</summary>
    public string? Remark { get; set; }
}
