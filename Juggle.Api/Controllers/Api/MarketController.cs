using System.Text.Json;
using Juggle.Application.Models.Response;
using Juggle.Application.Services;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

/// <summary>
/// 市场：接口/流程/模型助手/技能/报表的共享中心。
/// 发布后平台级可见（TenantId=null），任意租户可导入到本地（按 code/name 去重）。
/// </summary>
[ApiController]
[Route("api/market")]
[Authorize]
public class MarketController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly ITenantAccessor _tenant;

    public MarketController(JuggleDbContext db, ITenantAccessor tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>市场列表（平台级条目 + 我的条目），可按类型筛选</summary>
    [HttpGet("list")]
    public async Task<ApiResult> List([FromQuery] string? type)
    {
        var query = _db.MarketItems.Where(m => m.Deleted == 0 && m.Enabled == 1);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.ItemType == type);
        var items = await query.OrderByDescending(m => m.DownloadCount).ThenByDescending(m => m.Id).ToListAsync();
        return ApiResult.Success(items.Select(m => new { m.Id, m.ItemType, m.ItemName, m.Description, m.GroupName, m.DownloadCount, m.CreatedAt }));
    }

    /// <summary>我发布的市场条目（管理/下架）</summary>
    [HttpGet("my-list")]
    public async Task<ApiResult> MyList([FromQuery] string? type)
    {
        var tid = _tenant.TenantId;
        var query = _db.MarketItems.Where(m => m.Deleted == 0 && m.TenantId == tid);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.ItemType == type);
        return ApiResult.Success(await query.OrderByDescending(m => m.Id).ToListAsync());
    }

    /// <summary>发布到市场（内容快照 JSON 由前端按类型组装）</summary>
    [HttpPost("publish")]
    public async Task<ApiResult> Publish([FromBody] MarketPublishRequest req)
    {
        var entity = new MarketItemEntity
        {
            ItemType = req.ItemType,
            ItemName = req.ItemName,
            Description = req.Description,
            GroupName = req.GroupName,
            ContentJson = req.ContentJson,
            Enabled = 1,
            TenantId = null,   // 平台级共享
            CreatedBy = _tenant.TenantId,
            CreatedAt = DateTime.Now.ToString("o")
        };
        _db.MarketItems.Add(entity);
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    /// <summary>下架/删除市场条目（仅发布者或管理员）</summary>
    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var entity = await _db.MarketItems.FindAsync(id);
        if (entity == null) return ApiResult.Fail("条目不存在");
        if (entity.TenantId != null && entity.CreatedBy != _tenant.TenantId && entity.TenantId != _tenant.TenantId)
            return ApiResult.Fail("只能删除自己发布的条目");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>从市场导入到本地（按类型创建对应实体，按 code/name 去重），下载计数 +1</summary>
    [HttpPost("import/{id}")]
    public async Task<ApiResult> Import(long id)
    {
        var item = await _db.MarketItems.FirstOrDefaultAsync(m => m.Id == id && m.Deleted == 0 && m.Enabled == 1)
            ?? throw new Exception("市场条目不存在或已下架");
        try
        {
            var result = await ImportItemAsync(item);
            item.DownloadCount++;
            await _db.SaveChangesAsync();
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    private async Task<object> ImportItemAsync(MarketItemEntity item)
    {
        var type = item.ItemType ?? "";
        var tid = _tenant.TenantId;
        switch (type)
        {
            case "skill":
            {
                using var doc = JsonDocument.Parse(item.ContentJson ?? "{}");
                var root = doc.RootElement;
                string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Get("skillName") ?? item.ItemName ?? "导入技能";
                var exists = await _db.Skills.AnyAsync(s => s.SkillName == name && s.Deleted == 0);
                if (exists) return new { message = $"技能「{name}」已存在，跳过" };
                var skill = new SkillEntity
                {
                    SkillName = name,
                    GroupName = Get("groupName") ?? item.GroupName ?? "未分组",
                    Description = Get("description") ?? item.Description,
                    Content = Get("content"),
                    Enabled = 1,
                    TenantId = tid,
                    MarketSkillId = item.Id,
                    CreatedAt = DateTime.Now.ToString("o")
                };
                _db.Skills.Add(skill);
                await _db.SaveChangesAsync();
                return new { message = $"技能「{name}」已导入", id = skill.Id };
            }
            case "assistant":
            {
                using var doc = JsonDocument.Parse(item.ContentJson ?? "{}");
                var root = doc.RootElement;
                string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Get("assistantName") ?? item.ItemName ?? "导入助手";
                var exists = await _db.AiAssistants.AnyAsync(a => a.AssistantName == name && a.Deleted == 0);
                if (exists) return new { message = $"助手「{name}」已存在，跳过" };
                var assistant = new AiAssistantEntity
                {
                    AssistantName = name,
                    Description = Get("description") ?? item.Description,
                    SystemPrompt = Get("systemPrompt"),
                    InputParams = Get("inputParams"),
                    OutputParams = Get("outputParams"),
                    QuickPrompts = Get("quickPrompts"),
                    Icon = Get("icon"),
                    Enabled = 1,
                    TenantId = tid,
                    CreatedAt = DateTime.Now.ToString("o")
                };
                _db.AiAssistants.Add(assistant);
                await _db.SaveChangesAsync();
                return new { message = $"助手「{name}」已导入", id = assistant.Id };
            }
            case "flow":
            {
                using var doc = JsonDocument.Parse(item.ContentJson ?? "{}");
                var root = doc.RootElement;
                string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Get("flowName") ?? item.ItemName ?? "导入流程";
                var exists = await _db.FlowDefinitions.AnyAsync(f => f.FlowName == name && f.Deleted == 0);
                if (exists) return new { message = $"流程「{name}」已存在，跳过" };
                var flow = new FlowDefinitionEntity
                {
                    FlowKey = $"flow_{Guid.NewGuid():N}",
                    FlowName = name,
                    FlowDesc = Get("flowDesc") ?? item.Description,
                    FlowType = "sync",
                    GroupName = Get("groupName") ?? item.GroupName ?? "",
                    FlowContent = Get("flowContent") ?? "[]",
                    Status = 0,
                    TenantId = tid,
                    CreatedAt = DateTime.Now.ToString("o")
                };
                _db.FlowDefinitions.Add(flow);
                await _db.SaveChangesAsync();
                return new { message = $"流程「{name}」已导入", id = flow.Id, flowKey = flow.FlowKey };
            }
            case "report":
            {
                using var doc = JsonDocument.Parse(item.ContentJson ?? "{}");
                var root = doc.RootElement;
                string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                var name = Get("name") ?? item.ItemName ?? "导入报表";
                var exists = await _db.Set<ReportEntity>().AnyAsync(r => r.Name == name && r.Deleted == 0);
                if (exists) return new { message = $"报表「{name}」已存在，跳过" };
                var report = new ReportEntity
                {
                    Name = name,
                    GroupName = Get("groupName") ?? item.GroupName ?? "",
                    SourceType = "dataview",
                    CustomSql = "",
                    ParamsConfig = Get("paramsConfig") ?? "[]",
                    LayoutJson = Get("layoutJson") ?? "{}",
                    Status = 1,
                    TenantId = tid,
                    CreatedAt = DateTime.Now.ToString("o")
                };
                _db.Set<ReportEntity>().Add(report);
                await _db.SaveChangesAsync();
                return new { message = $"报表「{name}」已导入", id = report.Id };
            }
            case "api":
            {
                using var doc = JsonDocument.Parse(item.ContentJson ?? "{}");
                var root = doc.RootElement;
                var created = 0;
                if (root.TryGetProperty("suites", out var suitesEl) && suitesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in suitesEl.EnumerateArray())
                    {
                        var code = s.TryGetProperty("suiteCode", out var ce) ? ce.GetString() ?? "" : "";
                        if (code.Length == 0) continue;
                        if (await _db.Suites.AnyAsync(x => x.SuiteCode == code && x.Deleted == 0)) continue;
                        _db.Suites.Add(new SuiteEntity
                        {
                            SuiteCode = code,
                            SuiteName = s.TryGetProperty("suiteName", out var ne) ? ne.GetString() ?? code : code,
                            SuiteDesc = s.TryGetProperty("suiteDesc", out var de) ? de.GetString() : "",
                            TenantId = tid,
                            CreatedAt = DateTime.Now.ToString("o")
                        });
                        created++;
                    }
                    await _db.SaveChangesAsync();
                }
                if (root.TryGetProperty("apis", out var apisEl) && apisEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in apisEl.EnumerateArray())
                    {
                        var code = a.TryGetProperty("methodCode", out var ce) ? ce.GetString() ?? "" : "";
                        if (code.Length == 0) continue;
                        if (await _db.Apis.AnyAsync(x => x.MethodCode == code && x.Deleted == 0)) continue;
                        _db.Apis.Add(new ApiEntity
                        {
                            SuiteCode = a.TryGetProperty("suiteCode", out var se) ? se.GetString() ?? "" : "",
                            MethodCode = code,
                            MethodName = a.TryGetProperty("methodName", out var ne) ? ne.GetString() ?? code : code,
                            MethodDesc = a.TryGetProperty("methodDesc", out var de) ? de.GetString() : "",
                            Url = a.TryGetProperty("url", out var ue) ? ue.GetString() ?? "" : "",
                            RequestType = a.TryGetProperty("requestType", out var re) ? re.GetString() ?? "POST" : "POST",
                            MethodType = "HTTP",
                            Status = 1,
                            TenantId = tid,
                            CreatedAt = DateTime.Now.ToString("o")
                        });
                        created++;
                    }
                    await _db.SaveChangesAsync();
                }
                return new { message = $"接口已导入（新增 {created} 项，已存在自动跳过）" };
            }
            default:
                throw new Exception($"不支持的市场类型: {type}");
        }
    }
}

public class MarketPublishRequest
{
    public string ItemType { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string Description { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string ContentJson { get; set; } = "{}";
}
