using Juggle.Application.Models.Response;
using Juggle.Application.Services;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace Juggle.Api.Controllers.Api;

/// <summary>
/// Redis 实例配置：支持多个实例（一个默认），流程 REDIS_GET/REDIS_SET 节点按实例选择（0=默认）。
/// </summary>
[ApiController]
[Route("api/system/redis")]
[Authorize]
public class RedisConfigController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly ITenantAccessor _tenant;

    public RedisConfigController(JuggleDbContext db, ITenantAccessor tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>实例列表（含是否默认）</summary>
    [HttpGet("configs")]
    public async Task<ApiResult> List()
        => ApiResult.Success(await _db.RedisConfigs.Where(r => r.Deleted == 0).OrderByDescending(r => r.Id).ToListAsync());

    /// <summary>保存（新增/更新）实例</summary>
    [HttpPost("config/save")]
    public async Task<ApiResult> Save([FromBody] RedisConfigSaveRequest req)
    {
        RedisConfigEntity entity;
        if (req.Id > 0)
        {
            entity = await _db.RedisConfigs.FindAsync(req.Id) ?? throw new Exception("实例不存在");
        }
        else
        {
            entity = new RedisConfigEntity { CreatedAt = DateTime.Now.ToString("o"), TenantId = _tenant.TenantId };
            _db.RedisConfigs.Add(entity);
        }
        entity.ConfigName = req.ConfigName;
        entity.Host = req.Host;
        entity.Port = req.Port;
        entity.Password = req.Password;
        entity.Db = req.Db;
        entity.IsDefault = req.IsDefault ? 1 : 0;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();

        // 默认实例唯一
        if (entity.IsDefault == 1)
        {
            var others = await _db.RedisConfigs.Where(r => r.Deleted == 0 && r.Id != entity.Id && r.IsDefault == 1).ToListAsync();
            foreach (var o in others) o.IsDefault = 0;
            if (others.Count > 0) await _db.SaveChangesAsync();
        }
        return ApiResult.Success(entity.Id);
    }

    /// <summary>删除实例</summary>
    [HttpDelete("config/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var entity = await _db.RedisConfigs.FindAsync(id);
        if (entity == null) return ApiResult.Fail("实例不存在");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>测试连接</summary>
    [HttpPost("config/test")]
    public async Task<ApiResult> Test([FromBody] RedisConfigSaveRequest req)
    {
        try
        {
            var connStr = BuildConnStr(req.Host, req.Port, req.Password, req.Db);
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

    /// <summary>组装连接串（供节点执行与测试共用）</summary>
    public static string BuildConnStr(string? host, string? port, string? password, string? db)
        => $"{host?.Trim()}:{port?.Trim() ?? "6379"},password={password?.Trim()},defaultDatabase={db?.Trim() ?? "0"},abortConnect=false,connectTimeout=5000";
}

public class RedisConfigSaveRequest
{
    public long Id { get; set; }
    public string ConfigName { get; set; } = "";
    public string Host { get; set; } = "";
    public string Port { get; set; } = "6379";
    public string Password { get; set; } = "";
    public string Db { get; set; } = "0";
    public bool IsDefault { get; set; }
}
