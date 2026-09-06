using System.Text.Json;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Models.Response;
using Juggle.Application.Models.Request;
using Juggle.Application.Services.Impl;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

[ApiController]
[Route("api/report")]
[Authorize]
public class ReportController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly ReportExecutionService _reportExec;

    public ReportController(JuggleDbContext db, ReportExecutionService reportExec)
    {
        _db = db; _reportExec = reportExec;
    }

    [HttpPost("page")]
    public async Task<ApiResult> Page([FromBody] PageRequest req)
    {
        var query = _db.Set<ReportEntity>().Where(r => r.Deleted == 0);
        if (!string.IsNullOrEmpty(req.Keyword))
            query = query.Where(r => r.Name!.Contains(req.Keyword) || r.GroupName!.Contains(req.Keyword));
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(r => r.Id).Skip((req.PageNum - 1) * req.PageSize).Take(req.PageSize).ToListAsync();
        return ApiResult.Success(new { total, list });
    }

    [HttpPost("add")]
    public async Task<ApiResult> Add([FromBody] ReportEntity entity)
    {
        entity.CreatedAt = DateTime.Now.ToString("o");
        _db.Set<ReportEntity>().Add(entity);
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    [HttpPut("update")]
    public async Task<ApiResult> Update([FromBody] ReportEntity entity)
    {
        var rpt = await _db.Set<ReportEntity>().FindAsync(entity.Id);
        if (rpt == null) return ApiResult.Fail("报表不存在");
        rpt.Name = entity.Name; rpt.GroupName = entity.GroupName;
        rpt.SourceType = entity.SourceType; rpt.SourceRef = entity.SourceRef;
        rpt.CustomSql = entity.CustomSql; rpt.ParamsConfig = entity.ParamsConfig;
        rpt.LayoutJson = entity.LayoutJson; rpt.Status = entity.Status;
        rpt.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var rpt = await _db.Set<ReportEntity>().FindAsync(id);
        if (rpt == null) return ApiResult.Fail("不存在");
        rpt.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpPost("preview")]
    public async Task<ApiResult> Preview([FromBody] ReportPreviewRequest req)
    {
        try
        {
            var rpt = await _db.Set<ReportEntity>().FindAsync(req.Id);
            if (rpt == null) return ApiResult.Fail("报表不存在");
            var html = await _reportExec.RenderToHtml(rpt.LayoutJson!, req.Params);
            return ApiResult.Success(new { html });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    /// <summary>报表详情（正式访问页用：名称/参数配置等）</summary>
    [HttpGet("info/{id}")]
    public async Task<ApiResult> Info(long id)
    {
        var rpt = await _db.Set<ReportEntity>().FindAsync(id);
        if (rpt == null) return ApiResult.Fail("报表不存在");
        // 参数配置：优先 paramsConfig 列；旧数据未写入时回退到 layoutJson.params
        var paramsConfig = rpt.ParamsConfig;
        if (string.IsNullOrWhiteSpace(paramsConfig))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(rpt.LayoutJson ?? "{}");
                var root = doc.RootElement;
                if (root.TryGetProperty("params", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Array)
                    paramsConfig = p.ToString();
            }
            catch { /* 忽略解析失败 */ }
        }
        return ApiResult.Success(new { rpt.Id, rpt.Name, rpt.GroupName, ParamsConfig = paramsConfig, rpt.Status });
    }

    /// <summary>查询参数选项绑定数据集：执行数据集 SQL 返回行数据（供下拉/单选组/筛选组选项）</summary>
    [HttpPost("dataset-options")]
    public async Task<ApiResult> DatasetOptions([FromBody] ReportDatasetOptionsRequest req)
    {
        try
        {
            var rows = await _reportExec.GetDatasetRowsAsync(req.Id, req.DatasetId);
            return ApiResult.Success(rows);
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }

    [HttpPost("export-pdf")]
    public async Task<IActionResult> ExportPdf([FromBody] ReportPreviewRequest req)
    {
        var rpt = await _db.Set<ReportEntity>().FindAsync(req.Id);
        if (rpt == null) return NotFound();
        var data = await _reportExec.ExportPdfAsync(rpt.LayoutJson!, req.Params);
        return File(data, "application/pdf", $"{rpt.Name}.pdf");
    }

    [HttpPost("export-excel")]
    public async Task<IActionResult> ExportExcel([FromBody] ReportPreviewRequest req)
    {
        var rpt = await _db.Set<ReportEntity>().FindAsync(req.Id);
        if (rpt == null) return NotFound();
        var data = await _reportExec.ExportExcelAsync(rpt.LayoutJson!, req.Params);
        return File(data, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{rpt.Name}.xlsx");
    }
}

public class ReportPreviewRequest
{
    public long Id { get; set; }
    public Dictionary<string, object?>? Params { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class ReportDatasetOptionsRequest
{
    public long Id { get; set; }
    public string? DatasetId { get; set; }
}
