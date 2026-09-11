using Juggle.Application.Models.Response;
using Juggle.Application.Services.Impl;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

/// <summary>知识库：多库管理、文档上传解析、手动片段、切片配置、文本/向量检索、清洗、匹配记录。</summary>
[ApiController]
[Route("api/kb")]
[Authorize]
public class KnowledgeController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly KnowledgeService _kbService;

    public KnowledgeController(JuggleDbContext db, KnowledgeService kbService)
    {
        _db = db;
        _kbService = kbService;
    }

    // ==================== 知识库管理 ====================

    [HttpGet("list")]
    public async Task<ApiResult> List()
        => ApiResult.Success(await _db.KnowledgeBases.Where(k => k.Deleted == 0).OrderByDescending(k => k.Id).ToListAsync());

    [HttpPost("save")]
    public async Task<ApiResult> Save([FromBody] KnowledgeBaseEntity entity)
    {
        KnowledgeBaseEntity kb;
        if (entity.Id > 0)
        {
            kb = await _db.KnowledgeBases.FindAsync(entity.Id) ?? throw new Exception("知识库不存在");
        }
        else
        {
            kb = new KnowledgeBaseEntity { CreatedAt = DateTime.Now.ToString("o") };
            _db.KnowledgeBases.Add(kb);
        }
        kb.KbName = entity.KbName;
        kb.Description = entity.Description;
        kb.ChunkSize = entity.ChunkSize > 0 ? entity.ChunkSize : 500;
        kb.ChunkOverlap = entity.ChunkOverlap >= 0 ? entity.ChunkOverlap : 50;
        kb.RetrieveType = entity.RetrieveType == "vector" ? "vector" : "text";
        kb.VectorModel = entity.VectorModel;
        kb.Enabled = entity.Enabled;
        kb.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success(kb.Id);
    }

    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var kb = await _db.KnowledgeBases.FindAsync(id);
        if (kb == null) return ApiResult.Fail("知识库不存在");
        kb.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    // ==================== 文档与片段 ====================

    /// <summary>上传文档（word/excel/pdf/txt/markdown），自动解析并切片入库。</summary>
    [HttpPost("upload/{id}")]
    public async Task<ApiResult> Upload(long id, IFormFile file)
    {
        try
        {
            if (file == null || file.Length == 0) return ApiResult.Fail("未选择文件");
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var content = _kbService.ParseDocument(file.FileName, ms.ToArray());
            var ext = Path.GetExtension(file.FileName).TrimStart('.').ToLower();
            var result = await _kbService.AddDocumentAsync(id, file.FileName, ext, content);
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>手动增加片段</summary>
    [HttpPost("chunk/{id}")]
    public async Task<ApiResult> AddChunk(long id, [FromBody] KbChunkRequest req)
    {
        try
        {
            var result = await _kbService.AddChunkAsync(id, req.Content ?? "");
            return ApiResult.Success(result);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>知识库文档列表</summary>
    [HttpGet("documents/{id}")]
    public async Task<ApiResult> Documents(long id)
        => ApiResult.Success(await _db.KnowledgeDocuments.Where(d => d.KbId == id && d.Deleted == 0).OrderByDescending(d => d.Id).Take(200).ToListAsync());

    /// <summary>片段列表（分页）</summary>
    [HttpGet("chunks/{id}")]
    public async Task<ApiResult> Chunks(long id, int page = 1, int pageSize = 50)
        => ApiResult.Success(await _db.KnowledgeChunks
            .Where(c => c.KbId == id && c.Deleted == 0)
            .OrderBy(c => c.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(c => new { c.Id, c.DocId, c.SeqNo, c.Content })
            .ToListAsync());

    // ==================== 检索 / 清洗 / 匹配记录 ====================

    /// <summary>检索测试（记录匹配日志）</summary>
    [HttpPost("search/{id}")]
    public async Task<ApiResult> Search(long id, [FromBody] KbSearchRequest req)
    {
        try
        {
            var results = await _kbService.SearchAsync(id, req.Query ?? "", req.TopK > 0 ? req.TopK : 5);
            return ApiResult.Success(results);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>知识库清洗（去空/去重/超短）</summary>
    [HttpPost("clean/{id}")]
    public async Task<ApiResult> Clean(long id)
    {
        try
        {
            return ApiResult.Success(await _kbService.CleanAsync(id));
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>重新向量化（vector 模式）</summary>
    [HttpPost("revectorize/{id}")]
    public async Task<ApiResult> Revectorize(long id)
    {
        try
        {
            return ApiResult.Success(await _kbService.RevectorizeAsync(id));
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>匹配记录查询</summary>
    [HttpGet("match-logs/{id}")]
    public async Task<ApiResult> MatchLogs(long id)
        => ApiResult.Success(await _db.KnowledgeMatchLogs
            .Where(m => m.KbId == id && m.Deleted == 0)
            .OrderByDescending(m => m.Id)
            .Take(200)
            .Select(m => new { m.Id, m.Query, m.Score, m.CreatedAt, m.ResultsJson })
            .ToListAsync());
}

public class KbChunkRequest
{
    public string? Content { get; set; }
}

public class KbSearchRequest
{
    public string? Query { get; set; }
    public int TopK { get; set; } = 5;
}
