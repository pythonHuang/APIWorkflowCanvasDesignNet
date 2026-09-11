namespace Juggle.Domain.Entities;

/// <summary>
/// 市场条目：接口/流程/模型助手/技能/报表的共享与导入中心。
/// 发布后平台级可见（TenantId=null），任意租户可导入到本地。
/// </summary>
public class MarketItemEntity : BaseEntity
{
    /// <summary>条目类型：api / flow / assistant / skill / report</summary>
    public string? ItemType { get; set; }

    /// <summary>条目名称</summary>
    public string? ItemName { get; set; }

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>分组</summary>
    public string? GroupName { get; set; }

    /// <summary>内容快照（JSON，按类型结构不同）</summary>
    public string? ContentJson { get; set; }

    /// <summary>下载（导入）次数</summary>
    public int DownloadCount { get; set; }

    /// <summary>是否启用：1=上架 0=下架</summary>
    public int Enabled { get; set; } = 1;
}
