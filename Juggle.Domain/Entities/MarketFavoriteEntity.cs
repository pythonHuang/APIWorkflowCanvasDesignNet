namespace Juggle.Domain.Entities;

/// <summary>
/// 市场收藏：租户对市场条目的收藏记录（支持过滤是否收藏）。
/// </summary>
public class MarketFavoriteEntity : BaseEntity
{
    /// <summary>市场条目 ID（t_market_item.id）</summary>
    public long MarketItemId { get; set; }
}
