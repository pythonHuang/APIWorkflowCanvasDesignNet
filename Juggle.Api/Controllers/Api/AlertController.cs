using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

[ApiController]
[Route("api/alert")]
[Authorize]
public class AlertController : ControllerBase
{
    private readonly JuggleDbContext _db;
    public AlertController(JuggleDbContext db) => _db = db;

    // ===== 告警规则 CRUD =====

    [HttpPost("rule/add")]
    public async Task<ApiResult> AddRule([FromBody] AlertRuleEntity rule)
    {
        rule.CreatedAt = DateTime.Now.ToString("o");
        _db.AlertRules.Add(rule);
        await _db.SaveChangesAsync();
        return ApiResult.Success(rule.Id);
    }

    [HttpPut("rule/update")]
    public async Task<ApiResult> UpdateRule([FromBody] AlertRuleEntity rule)
    {
        var entity = await _db.AlertRules.FindAsync(rule.Id);
        if (entity == null) return ApiResult.Fail("规则不存在");
        entity.Name = rule.Name;
        entity.MetricType = rule.MetricType;
        entity.Condition = rule.Condition;
        entity.Threshold = rule.Threshold;
        entity.Channel = rule.Channel;
        entity.Recipients = rule.Recipients;
        entity.Description = rule.Description;
        entity.Status = rule.Status;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpDelete("rule/delete/{id}")]
    public async Task<ApiResult> DeleteRule(long id)
    {
        var entity = await _db.AlertRules.FindAsync(id);
        if (entity == null) return ApiResult.Fail("规则不存在");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpGet("rule/list")]
    public async Task<ApiResult> ListRules()
    {
        var list = await _db.AlertRules.Where(r => r.Deleted == 0).OrderByDescending(r => r.Id).ToListAsync();
        return ApiResult.Success(list);
    }

    // ===== 告警记录 =====

    [HttpGet("record/list")]
    public async Task<ApiResult> ListRecords([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _db.AlertRecords.Where(r => r.Deleted == 0).OrderByDescending(r => r.Id);
        var total = await query.CountAsync();
        var list = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return ApiResult.Success(new { total, list });
    }
}
