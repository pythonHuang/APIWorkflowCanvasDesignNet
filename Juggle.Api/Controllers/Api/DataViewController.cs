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
[Route("api/report/dataview")]
[Authorize]
public class DataViewController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly ReportExecutionService _reportExec;

    public DataViewController(JuggleDbContext db, ReportExecutionService reportExec)
    {
        _db = db; _reportExec = reportExec;
    }

    [HttpPost("page")]
    public async Task<ApiResult> Page([FromBody] PageRequest req)
    {
        var query = _db.Set<DataViewEntity>().Where(d => d.Deleted == 0);
        if (!string.IsNullOrEmpty(req.Keyword))
            query = query.Where(d => d.Name!.Contains(req.Keyword) || d.GroupName!.Contains(req.Keyword));
        var total = await query.CountAsync();
        var list = await query.OrderByDescending(d => d.Id).Skip((req.PageNum - 1) * req.PageSize).Take(req.PageSize).ToListAsync();
        return ApiResult.Success(new { total, list });
    }

    [HttpPost("add")]
    public async Task<ApiResult> Add([FromBody] DataViewEntity entity)
    {
        entity.CreatedAt = DateTime.Now.ToString("o");
        _db.Set<DataViewEntity>().Add(entity);
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    [HttpPut("update")]
    public async Task<ApiResult> Update([FromBody] DataViewEntity entity)
    {
        var dv = await _db.Set<DataViewEntity>().FindAsync(entity.Id);
        if (dv == null) return ApiResult.Fail("数据视图不存在");
        dv.Name = entity.Name; dv.GroupName = entity.GroupName;
        dv.DataSourceId = entity.DataSourceId; dv.Sql = entity.Sql;
        dv.Parameters = entity.Parameters; dv.Remark = entity.Remark;
        dv.Status = entity.Status;
        dv.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var dv = await _db.Set<DataViewEntity>().FindAsync(id);
        if (dv == null) return ApiResult.Fail("不存在");
        dv.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpPost("preview")]
    public async Task<ApiResult> Preview([FromBody] DataViewPreviewRequest req)
    {
        try
        {
            long dsId; string sql;
            if (req.DataSourceId > 0 && !string.IsNullOrEmpty(req.Sql))
            {
                // 自定义SQL模式（报表设计器用）
                dsId = req.DataSourceId; sql = req.Sql!;
            }
            else
            {
                var dv = await _db.Set<DataViewEntity>().FindAsync(req.Id);
                if (dv == null) return ApiResult.Fail("数据视图不存在");
                dsId = dv.DataSourceId; sql = dv.Sql!;
            }
            var dt = await _reportExec.ExecuteQuery(dsId, sql, req.Params);
            var rows = new List<Dictionary<string, object?>>();
            foreach (System.Data.DataRow row in dt.Rows)
            {
                var dict = new Dictionary<string, object?>();
                foreach (System.Data.DataColumn col in dt.Columns)
                    dict[col.ColumnName] = row[col];
                rows.Add(dict);
            }
            return ApiResult.Success(new { columns = dt.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName), rows, total = rows.Count });
        }
        catch (Exception ex) { return ApiResult.Fail(ex.Message); }
    }
}

public class DataViewPreviewRequest
{
    public long Id { get; set; }
    public long DataSourceId { get; set; }
    public string? Sql { get; set; }
    public Dictionary<string, object?>? Params { get; set; }
}
