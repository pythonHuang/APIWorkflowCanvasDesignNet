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
/// AI 技能管理：CRUD、分组/启停、单个/批量导入导出。
/// 技能可被大模型节点勾选，执行时拼入系统提示词。
/// </summary>
[ApiController]
[Route("api/skill")]
[Authorize]
public class SkillController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly ITenantAccessor _tenant;

    public SkillController(JuggleDbContext db, ITenantAccessor tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    /// <summary>技能列表（可选 group 筛选；仅启用的可加 enabled=1）</summary>
    [HttpGet("list")]
    public async Task<ApiResult> List([FromQuery] string? group, [FromQuery] int? enabled)
    {
        var query = _db.Skills.Where(s => s.Deleted == 0);
        if (!string.IsNullOrWhiteSpace(group)) query = query.Where(s => s.GroupName == group);
        if (enabled.HasValue) query = query.Where(s => s.Enabled == enabled.Value);
        return ApiResult.Success(await query.OrderBy(s => s.GroupName).ThenByDescending(s => s.Id).ToListAsync());
    }

    /// <summary>保存（新增/编辑）</summary>
    [HttpPost("save")]
    public async Task<ApiResult> Save([FromBody] SkillSaveRequest req)
    {
        SkillEntity entity;
        if (req.Id > 0)
        {
            entity = await _db.Skills.FindAsync(req.Id) ?? throw new Exception("技能不存在");
        }
        else
        {
            entity = new SkillEntity { CreatedAt = DateTime.Now.ToString("o"), TenantId = _tenant.TenantId };
            _db.Skills.Add(entity);
        }
        entity.SkillName = req.SkillName;
        entity.GroupName = req.GroupName;
        entity.Description = req.Description;
        entity.Content = req.Content;
        entity.Enabled = req.Enabled ? 1 : 0;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    /// <summary>删除</summary>
    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var entity = await _db.Skills.FindAsync(id);
        if (entity == null) return ApiResult.Fail("技能不存在");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>启用/禁用</summary>
    [HttpPost("toggle")]
    public async Task<ApiResult> Toggle([FromBody] SkillToggleRequest req)
    {
        var entity = await _db.Skills.FindAsync(req.Id);
        if (entity == null) return ApiResult.Fail("技能不存在");
        entity.Enabled = req.Enabled ? 1 : 0;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    /// <summary>
    /// 导入技能（单个）：JSON（skillName/groupName/description/content）
    /// 或纯 markdown（首个 "# 名称" 作为技能名，其余为内容）。
    /// </summary>
    [HttpPost("import")]
    public async Task<ApiResult> Import([FromBody] SkillImportRequest req)
    {
        try
        {
            var (name, group, desc, content) = ParseSkillImport(req.FileName ?? "", req.Content ?? "");
            var entity = new SkillEntity
            {
                SkillName = name,
                GroupName = string.IsNullOrWhiteSpace(group) ? "未分组" : group,
                Description = desc,
                Content = content,
                Enabled = 1,
                CreatedAt = DateTime.Now.ToString("o"),
                TenantId = _tenant.TenantId
            };
            _db.Skills.Add(entity);
            await _db.SaveChangesAsync();
            return ApiResult.Success(entity.Id);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>批量导入（数组，每项 {fileName, content}）</summary>
    [HttpPost("batch-import")]
    public async Task<ApiResult> BatchImport([FromBody] List<SkillImportRequest> req)
    {
        try
        {
            var count = 0;
            foreach (var item in req)
            {
                var (name, group, desc, content) = ParseSkillImport(item.FileName ?? "", item.Content ?? "");
                if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(content)) continue;
                _db.Skills.Add(new SkillEntity
                {
                    SkillName = string.IsNullOrWhiteSpace(name) ? item.FileName : name,
                    GroupName = string.IsNullOrWhiteSpace(group) ? "未分组" : group,
                    Description = desc,
                    Content = content,
                    Enabled = 1,
                    CreatedAt = DateTime.Now.ToString("o"),
                    TenantId = _tenant.TenantId
                });
                count++;
            }
            await _db.SaveChangesAsync();
            return ApiResult.Success(new { count });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>导出单个技能</summary>
    [HttpGet("export/{id}")]
    public async Task<ApiResult> Export(long id)
    {
        var entity = await _db.Skills.FindAsync(id);
        if (entity == null) return ApiResult.Fail("技能不存在");
        return ApiResult.Success(new { skillName = entity.SkillName, groupName = entity.GroupName, description = entity.Description, content = entity.Content });
    }

    /// <summary>批量导出（ids 逗号分隔）</summary>
    [HttpGet("batch-export")]
    public async Task<ApiResult> BatchExport([FromQuery] string ids)
    {
        var idList = (ids ?? "").Split(',').Where(s => long.TryParse(s, out _)).Select(long.Parse).ToList();
        var list = await _db.Skills.Where(s => idList.Contains(s.Id) && s.Deleted == 0).ToListAsync();
        return ApiResult.Success(list.Select(s => new { skillName = s.SkillName, groupName = s.GroupName, description = s.Description, content = s.Content }));
    }

    /// <summary>解析导入内容：JSON 或 markdown（# 名称 为首行标题）。</summary>
    private static (string Name, string Group, string Desc, string Content) ParseSkillImport(string fileName, string content)
    {
        var text = content?.Trim() ?? "";
        if (text.StartsWith("{"))
        {
            try
            {
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                string? Get(string p) => root.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return (Get("skillName") ?? Get("name") ?? "未命名技能",
                        Get("groupName") ?? Get("group") ?? "",
                        Get("description") ?? "",
                        Get("content") ?? "");
            }
            catch { /* 非 JSON 按 markdown 处理 */ }
        }
        // markdown：# 标题为名称
        var lines = text.Split('\n');
        var name = "未命名技能";
        var bodyStart = 0;
        if (lines.Length > 0 && lines[0].TrimStart().StartsWith("#"))
        {
            name = lines[0].TrimStart().TrimStart('#').Trim();
            bodyStart = 1;
        }
        if (name == "未命名技能" && !string.IsNullOrWhiteSpace(fileName))
            name = Path.GetFileNameWithoutExtension(fileName);
        return (name, "", "", string.Join('\n', lines.Skip(bodyStart)).Trim());
    }
}

public class SkillSaveRequest
{
    public long Id { get; set; }
    public string SkillName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Content { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class SkillToggleRequest
{
    public long Id { get; set; }
    public bool Enabled { get; set; }
}

public class SkillImportRequest
{
    public string? FileName { get; set; }
    public string? Content { get; set; }
}
