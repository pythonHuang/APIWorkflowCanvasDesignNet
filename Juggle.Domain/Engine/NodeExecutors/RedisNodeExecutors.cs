using System.Text.Json;
using StackExchange.Redis;

namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>Redis 连接管理（按连接串缓存复用）。</summary>
public static class RedisConnectionPool
{
    private static readonly Dictionary<string, ConnectionMultiplexer> _cache = new();

    public static ConnectionMultiplexer Get(string connStr)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(connStr, out var conn) || !conn.IsConnected)
            {
                conn?.Dispose();
                conn = ConnectionMultiplexer.Connect(connStr);
                _cache[connStr] = conn;
            }
            return conn;
        }
    }
}

/// <summary>Redis 缓存查询节点：按 key 取值（JSON 字符串自动解析为对象）。</summary>
public class RedisGetNodeExecutor : INodeExecutor
{
    private readonly string _connStr;

    public RedisGetNodeExecutor(string connStr) => _connStr = connStr;

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.RedisGetConfig
            ?? throw new InvalidOperationException($"Redis 查询节点 [{node.Key}] 未配置 redisGetConfig。");
        if (string.IsNullOrWhiteSpace(cfg.Key))
            throw new InvalidOperationException($"Redis 查询节点 [{node.Key}] 未配置 key。");

        var key = RenderTemplate(cfg.Key, context);
        var db = RedisConnectionPool.Get(_connStr).GetDatabase();

        var value = await db.StringGetAsync(key);
        object? result = null;
        if (value.HasValue)
        {
            var str = value.ToString();
            // JSON 字符串自动解析为对象/数组
            try
            {
                using var doc = JsonDocument.Parse(str);
                result = FileParseNodeExecutor.CloneToNative(doc.RootElement);
            }
            catch { result = str; }
        }

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, result);

        return node.Outgoings.FirstOrDefault();
    }

    internal static string RenderTemplate(string template, FlowContext context)
        => System.Text.RegularExpressions.Regex.Replace(template, @"\$\{([^}]+)\}", m =>
        {
            var name = m.Groups[1].Value.Trim();
            return context.GetVariable(name)?.ToString() ?? "";
        });
}

/// <summary>Redis 缓存设置节点：写入 key-value（可设过期秒数），结果写入输出变量。</summary>
public class RedisSetNodeExecutor : INodeExecutor
{
    private readonly string _connStr;

    public RedisSetNodeExecutor(string connStr) => _connStr = connStr;

    public async Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var cfg = node.RedisSetConfig
            ?? throw new InvalidOperationException($"Redis 设置节点 [{node.Key}] 未配置 redisSetConfig。");
        if (string.IsNullOrWhiteSpace(cfg.Key))
            throw new InvalidOperationException($"Redis 设置节点 [{node.Key}] 未配置 key。");

        var key = RedisGetNodeExecutor.RenderTemplate(cfg.Key, context);
        var val = string.IsNullOrWhiteSpace(cfg.Value)
            ? ""
            : context.GetVariable(cfg.Value) switch
            {
                string s => s,
                var v => JsonSerializer.Serialize(v)
            };
        var db = RedisConnectionPool.Get(_connStr).GetDatabase();

        var ok = cfg.ExpireSeconds > 0
            ? await db.StringSetAsync(key, val, TimeSpan.FromSeconds(cfg.ExpireSeconds))
            : await db.StringSetAsync(key, val);

        if (!string.IsNullOrWhiteSpace(cfg.Output))
            context.SetVariable(cfg.Output, ok);

        return node.Outgoings.FirstOrDefault();
    }
}
