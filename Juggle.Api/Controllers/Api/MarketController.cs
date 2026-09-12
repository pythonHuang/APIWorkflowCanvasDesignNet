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
    private readonly IHttpClientFactory _httpClientFactory;

    public MarketController(JuggleDbContext db, ITenantAccessor tenant, IHttpClientFactory httpClientFactory)
    {
        _db = db;
        _tenant = tenant;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>官方市场 GitHub 仓库（market 目录）</summary>
    private const string MarketRepo = "https://raw.githubusercontent.com/pythonHuang/APIWorkflowCanvasDesignNet/main/market";

    /// <summary>本地 market 目录（发现下载的文件落盘位置）</summary>
    private static string MarketDir => Path.Combine(Directory.GetCurrentDirectory(), "market");

    /// <summary>发现官方市场：拉取 index.json 并把各类型 {id}.json 下载到本地 market 目录。</summary>
    [HttpGet("discover")]
    public async Task<ApiResult> Discover()
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(60);
        var downloaded = 0;
        var skipped = 0;
        var failed = new List<string>();

        string? indexJson;
        try
        {
            var indexResp = await client.GetAsync($"{MarketRepo}/index.json");
            if (!indexResp.IsSuccessStatusCode)
                return ApiResult.Fail($"官方市场 index.json 获取失败({(int)indexResp.StatusCode})：{MarketRepo}/index.json（仓库尚未建立 market 目录时请忽略）");
            indexJson = await indexResp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"官方市场访问失败：{ex.Message}");
        }

        Directory.CreateDirectory(MarketDir);
        await System.IO.File.WriteAllTextAsync(Path.Combine(MarketDir, "index.json"), indexJson);

        using var doc = JsonDocument.Parse(indexJson);
        var root = doc.RootElement;
        JsonElement entries = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty("items", out var it) ? it : default;

        if (entries.ValueKind != JsonValueKind.Array)
            return ApiResult.Success(new { downloaded = 0, skipped = 0, failed = new List<string>(), message = "index.json 中没有 items 列表" });

        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
            var type = entry.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "" : "";
            var file = entry.TryGetProperty("file", out var fileEl) ? fileEl.GetString() ?? "" : "";
            if (id == 0 || string.IsNullOrWhiteSpace(type)) continue;
            if (string.IsNullOrWhiteSpace(file)) file = $"{type}/{id}.json";

            var targetPath = Path.Combine(MarketDir, type, $"{id}.json");
            var url = $"{MarketRepo}/{file}";
            try
            {
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    failed.Add($"{type}/{id}.json");
                    continue;
                }
                var body = await resp.Content.ReadAsStringAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                await System.IO.File.WriteAllTextAsync(targetPath, body);
                downloaded++;
            }
            catch { failed.Add($"{type}/{id}.json"); }
        }

        return ApiResult.Success(new { downloaded, skipped, failed, message = $"发现完成：成功下载 {downloaded} 个条目到本地 market 目录" });
    }

    /// <summary>本地 market 目录条目列表（含元数据与是否已导入）。</summary>
    [HttpGet("local-files")]
    public async Task<ApiResult> ListLocalFiles()
    {
        var result = new List<object>();
        var imported = await _db.MarketItems.Where(m => m.Deleted == 0 && m.MarketItemId != null).ToListAsync();

        if (Directory.Exists(MarketDir))
        {
            foreach (var typeDir in Directory.GetDirectories(MarketDir))
            {
                var type = Path.GetFileName(typeDir);
                foreach (var file in Directory.GetFiles(typeDir, "*.json"))
                {
                    if (Path.GetFileName(file) == "index.json") continue;
                    try
                    {
                        var json = await System.IO.File.ReadAllTextAsync(file);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                        var marketId = root.TryGetProperty("id", out var idEl) ? (int?)idEl.GetInt32() : null;
                        var hit = imported.FirstOrDefault(m => m.MarketItemId == marketId && m.ItemType == type);
                        result.Add(new
                        {
                            marketId,
                            type,
                            name = Get("name") ?? Path.GetFileNameWithoutExtension(file),
                            description = Get("description"),
                            icon = Get("icon"),
                            author = Get("author"),
                            version = Get("version"),
                            updatedAt = Get("updatedAt"),
                            imported = hit != null,
                            localItemId = hit?.Id,
                            localVersion = hit?.Version
                        });
                    }
                    catch { /* 非法 JSON 跳过 */ }
                }
            }
        }

        return ApiResult.Success(result);
    }

    /// <summary>导入本地 market 目录条目到市场（market_item_id 相同则更新，否则新增）。</summary>
    [HttpPost("import-file")]
    public async Task<ApiResult> ImportFile([FromBody] MarketImportFileRequest req)
    {
        var filePath = Path.Combine(MarketDir, req.Type, $"{req.MarketId}.json");
        if (!System.IO.File.Exists(filePath)) return ApiResult.Fail($"本地文件不存在: {req.Type}/{req.MarketId}.json");

        string json;
        try { json = await System.IO.File.ReadAllTextAsync(filePath); }
        catch (Exception ex) { return ApiResult.Fail($"读取文件失败: {ex.Message}"); }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            var entity = await _db.MarketItems.FirstOrDefaultAsync(m =>
                m.MarketItemId == req.MarketId && m.ItemType == req.Type && m.Deleted == 0);
            var isUpdate = entity != null;
            entity ??= new MarketItemEntity { MarketItemId = req.MarketId, ItemType = req.Type, TenantId = null, Enabled = 1 };

            entity.ItemName = Get("name") ?? entity.ItemName;
            entity.Description = Get("description") ?? entity.Description;
            entity.Icon = Get("icon") ?? entity.Icon;
            entity.Author = Get("author") ?? entity.Author;
            entity.Version = Get("version") ?? entity.Version;
            entity.GroupName = Get("group") ?? entity.GroupName;
            if (root.TryGetProperty("content", out var contentEl))
                entity.ContentJson = contentEl.ToString();
            else
                entity.ContentJson = json;
            entity.UpdatedAt = Get("updatedAt") ?? DateTime.Now.ToString("o");

            if (entity.Id == 0) _db.MarketItems.Add(entity);
            await _db.SaveChangesAsync();
            return ApiResult.Success(new
            {
                id = entity.Id,
                message = isUpdate ? $"「{entity.ItemName}」已更新（市场条目 id={req.MarketId} 已存在）" : $"「{entity.ItemName}」已导入市场"
            });
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"解析/导入失败: {ex.Message}");
        }
    }

    /// <summary>市场列表（平台级条目 + 我的条目），可按类型筛选</summary>
    [HttpGet("list")]
    public async Task<ApiResult> List([FromQuery] string? type)
    {
        var query = _db.MarketItems.Where(m => m.Deleted == 0 && m.Enabled == 1);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.ItemType == type);
        var items = await query.OrderByDescending(m => m.DownloadCount).ThenByDescending(m => m.Id).ToListAsync();
        return ApiResult.Success(items.Select(m => new
        {
            m.Id, m.ItemType, m.ItemName, m.Description, m.GroupName, m.DownloadCount,
            m.MarketItemId, m.Icon, m.Author, m.Version, m.UpdatedAt, m.ContentJson
        }));
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

public class MarketImportFileRequest
{
    public string Type { get; set; } = "";
    public int MarketId { get; set; }
}
