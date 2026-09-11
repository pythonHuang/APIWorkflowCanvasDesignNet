namespace Juggle.Domain.Entities;

/// <summary>
/// Redis 实例配置（可配置多个，其中一个为默认）。
/// 流程 REDIS_GET/REDIS_SET 节点按实例选择，0 表示默认实例。
/// </summary>
public class RedisConfigEntity : BaseEntity
{
    /// <summary>实例名称（如 缓存主库）</summary>
    public string? ConfigName { get; set; }

    public string? Host { get; set; }
    public string? Port { get; set; }
    public string? Password { get; set; }
    public string? Db { get; set; }

    /// <summary>是否默认实例：1=默认</summary>
    public int IsDefault { get; set; }
}
