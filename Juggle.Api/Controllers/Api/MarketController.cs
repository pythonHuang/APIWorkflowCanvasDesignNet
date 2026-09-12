using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
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

    /// <summary>市场列表（平台级条目 + 我的条目），可按类型筛选，favorite=1 只看收藏</summary>
    [HttpGet("list")]
    public async Task<ApiResult> List([FromQuery] string? type, [FromQuery] int favorite = 0)
    {
        var tid = _tenant.TenantId;
        var query = _db.MarketItems.Where(m => m.Deleted == 0 && m.Enabled == 1);
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(m => m.ItemType == type);

        var favIds = (await _db.MarketFavorites
            .Where(f => f.Deleted == 0 && f.TenantId == tid)
            .Select(f => f.MarketItemId)
            .ToListAsync()).ToHashSet();
        if (favorite == 1)
            query = query.Where(m => favIds.Contains(m.Id));

        var items = await query.OrderByDescending(m => m.DownloadCount).ThenByDescending(m => m.Id).ToListAsync();
        return ApiResult.Success(items.Select(m => new
        {
            m.Id, m.ItemType, m.ItemName, m.Description, m.GroupName, m.DownloadCount,
            m.MarketItemId, m.Icon, m.Author, m.Version, m.UpdatedAt, m.ContentJson,
            favorited = favIds.Contains(m.Id)
        }));
    }

    /// <summary>市场文件 JSON 序列化选项（中文不转义）。</summary>
    private static readonly JsonSerializerOptions MarketFileJsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>生成分享文件：写入本地 market/{type}/{id}.json 并更新 market/index.json（同名条目复用原 id），返回条目 JSON 与索引条目（供 GitHub PR 使用）。</summary>
    [HttpPost("generate-share-file")]
    public async Task<ApiResult> GenerateShareFile([FromBody] MarketShareRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ItemType) || string.IsNullOrWhiteSpace(req.Name))
            return ApiResult.Fail("条目类型与名称不能为空");
        if (string.IsNullOrWhiteSpace(req.Author))
            return ApiResult.Fail("请填写作者");

        // 同名条目已分享过则复用原 id（重新分享即更新），否则生成新 id
        var id = FindExistingShareId(req.ItemType, req.Name)
                 ?? (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var updatedAt = DateTime.Now.ToString("yyyy-MM-dd");

        object? content;
        try { content = JsonSerializer.Deserialize<JsonElement>(string.IsNullOrWhiteSpace(req.ContentJson) ? "{}" : req.ContentJson); }
        catch { return ApiResult.Fail("内容 JSON 格式非法"); }

        var itemJson = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = req.ItemType,
            ["name"] = req.Name,
            ["description"] = req.Description ?? "",
            ["icon"] = string.IsNullOrWhiteSpace(req.Icon) ? "📦" : req.Icon,
            ["author"] = req.Author,
            ["version"] = string.IsNullOrWhiteSpace(req.Version) ? "1.0.0" : req.Version,
            ["updatedAt"] = updatedAt,
            ["content"] = content
        }, MarketFileJsonOpts);

        var indexEntry = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["type"] = req.ItemType,
            ["name"] = req.Name,
            ["description"] = req.Description ?? "",
            ["icon"] = string.IsNullOrWhiteSpace(req.Icon) ? "📦" : req.Icon,
            ["author"] = req.Author,
            ["version"] = string.IsNullOrWhiteSpace(req.Version) ? "1.0.0" : req.Version,
            ["updatedAt"] = updatedAt,
            ["file"] = $"{req.ItemType}/{id}.json"
        };

        try
        {
            var typeDir = Path.Combine(MarketDir, req.ItemType);
            Directory.CreateDirectory(typeDir);
            await System.IO.File.WriteAllTextAsync(Path.Combine(typeDir, $"{id}.json"), itemJson);

            var indexPath = Path.Combine(MarketDir, "index.json");
            var indexJson = await UpsertIndexEntryAsync(indexPath, id, req.ItemType, indexEntry);
            return ApiResult.Success(new { id, itemJson, indexEntry, indexJson, file = $"{req.ItemType}/{id}.json" });
        }
        catch (Exception ex)
        {
            return ApiResult.Fail($"生成本地分享文件失败: {ex.Message}");
        }
    }

    /// <summary>查找同类型同名条目的已分享 id（重新分享时更新原文件）。</summary>
    private int? FindExistingShareId(string type, string name)
    {
        var typeDir = Path.Combine(MarketDir, type);
        if (!Directory.Exists(typeDir)) return null;
        foreach (var file in Directory.GetFiles(typeDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(file));
                var root = doc.RootElement;
                var fName = root.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.Equals(fName, name, StringComparison.OrdinalIgnoreCase)
                    && root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
                    return idEl.GetInt32();
            }
            catch { /* 跳过损坏文件 */ }
        }
        return null;
    }

    /// <summary>更新本地 market/index.json（同 id+type 更新，否则追加），返回最新索引 JSON。</summary>
    private static async Task<string> UpsertIndexEntryAsync(string indexPath, int id, string type, Dictionary<string, object?> entry)
    {
        var entries = new List<Dictionary<string, object?>>();
        if (System.IO.File.Exists(indexPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(indexPath));
                var root = doc.RootElement;
                var items = root.ValueKind == JsonValueKind.Array ? root
                    : root.TryGetProperty("items", out var it) ? it : default;
                if (items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in items.EnumerateArray())
                    {
                        var d = new Dictionary<string, object?>();
                        foreach (var p in e.EnumerateObject()) d[p.Name] = p.Value.Clone();
                        entries.Add(d);
                    }
                }
            }
            catch { /* 损坏则重建 */ }
        }

        var idx = entries.FindIndex(e =>
            AsLong(e.GetValueOrDefault("id")) == id && string.Equals(AsString(e.GetValueOrDefault("type")), type, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0) entries[idx] = entry; else entries.Add(entry);

        var indexJson = JsonSerializer.Serialize(new Dictionary<string, object?> { ["items"] = entries }, MarketFileJsonOpts);
        await System.IO.File.WriteAllTextAsync(indexPath, indexJson);
        return indexJson;
    }

    private static long? AsLong(object? o) => o switch
    {
        JsonElement je when je.ValueKind == JsonValueKind.Number => je.GetInt64(),
        long l => l,
        int i => i,
        _ => null
    };

    private static string? AsString(object? o) => o switch
    {
        JsonElement je when je.ValueKind == JsonValueKind.String => je.GetString(),
        string s => s,
        _ => null
    };

    /// <summary>收藏市场条目（幂等）。</summary>
    [HttpPost("favorite/{id}")]
    public async Task<ApiResult> Favorite(long id)
    {
        var tid = _tenant.TenantId;
        var exists = await _db.MarketFavorites.AnyAsync(f => f.MarketItemId == id && f.TenantId == tid && f.Deleted == 0);
        if (exists) return ApiResult.Success(true);
        _db.MarketFavorites.Add(new MarketFavoriteEntity
        {
            MarketItemId = id,
            TenantId = tid,
            CreatedBy = tid,
            CreatedAt = DateTime.Now.ToString("o")
        });
        await _db.SaveChangesAsync();
        return ApiResult.Success(true);
    }

    /// <summary>取消收藏市场条目。</summary>
    [HttpPost("unfavorite/{id}")]
    public async Task<ApiResult> Unfavorite(long id)
    {
        var tid = _tenant.TenantId;
        var fav = await _db.MarketFavorites.FirstOrDefaultAsync(f => f.MarketItemId == id && f.TenantId == tid && f.Deleted == 0);
        if (fav == null) return ApiResult.Success(false);
        fav.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success(false);
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

public class MarketShareRequest
{
    public string ItemType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string ContentJson { get; set; } = "{}";
}
