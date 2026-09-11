using Juggle.Application.Models.Response;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Juggle.Api.Controllers.Api;

/// <summary>Redis 配置：流程 REDIS_GET/REDIS_SET 节点与缓存能力使用。</summary>
[ApiController]
[Route("api/system/redis")]
[Authorize]
public class RedisConfigController : ControllerBase
{
    private readonly JuggleDbContext _db;

    public RedisConfigController(JuggleDbContext db) => _db = db;

    [HttpGet("config")]
    public async Task<ApiResult> GetConfig()
    {
        var configs = await _db.SystemConfigs
            .Where(c => c.Deleted == 0 && c.ConfigKey!.StartsWith("redis."))
            .ToListAsync();
        string? Get(string key) => configs.FirstOrDefault(c => c.ConfigKey == key)?.ConfigValue;
        return ApiResult.Success(new
        {
            host = Get("redis.host") ?? "",
            port = Get("redis.port") ?? "6379",
            password = Get("redis.password") ?? "",
            db = Get("redis.db") ?? "0"
        });
    }

    [HttpPost("config")]
    public async Task<ApiResult> SaveConfig([FromBody] RedisConfigRequest req)
    {
        await UpsertAsync("redis.host", req.Host?.Trim() ?? "", "Redis地址");
        await UpsertAsync("redis.port", req.Port?.Trim() ?? "6379", "Redis端口");
        await UpsertAsync("redis.password", req.Password?.Trim() ?? "", "Redis密码");
        await UpsertAsync("redis.db", req.Db?.Trim() ?? "0", "Redis数据库");
        return ApiResult.Success();
    }

    /// <summary>测试 Redis 连接</summary>
    [HttpPost("test")]
    public async Task<ApiResult> Test([FromBody] RedisConfigRequest req)
    {
        try
        {
            var connStr = $"{req.Host?.Trim()}:{req.Port?.Trim() ?? "6379"},password={req.Password?.Trim()},defaultDatabase={req.Db?.Trim() ?? "0"},abortConnect=false,connectTimeout=5000";
            var conn = await ConnectionMultiplexer.ConnectAsync(connStr);
            var pong = await conn.GetDatabase().PingAsync();
            await conn.CloseAsync();
            return ApiResult.Success($"连接成功（PING {pong.TotalMilliseconds}ms）");
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"连接失败: {ex.Message}");
        }
    }

    private async Task UpsertAsync(string key, string value, string name)
    {
        var entity = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (entity == null)
        {
            _db.SystemConfigs.Add(new Domain.Entities.SystemConfigEntity
            {
                ConfigKey = key, ConfigValue = value, ConfigName = name, ConfigGroup = "Redis",
                CreatedAt = DateTime.Now.ToString("o")
            });
        }
        else
        {
            entity.ConfigValue = value;
            entity.UpdatedAt = DateTime.Now.ToString("o");
        }
        await _db.SaveChangesAsync();
    }
}

public class RedisConfigRequest
{
    public string Host { get; set; } = "";
    public string Port { get; set; } = "6379";
    public string Password { get; set; } = "";
    public string Db { get; set; } = "0";
}
