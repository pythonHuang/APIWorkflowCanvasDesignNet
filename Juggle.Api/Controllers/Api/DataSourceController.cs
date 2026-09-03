using Juggle.Application.Models.Request;
using Juggle.Application.Models.Response;
using Juggle.Application.Services;
using Juggle.Application.Services.Impl;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

/// <summary>
/// 数据源管理控制器
/// 提供数据源的增删改查、连接测试等功能
/// </summary>
[ApiController]
[Route("api/system/datasource")]
[Authorize]
public class DataSourceController : ControllerBase
{
    private readonly JuggleDbContext _db;
    private readonly DataSourceService _dsService;
    private readonly ITenantAccessor _tenant;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="db">数据库上下文</param>
    /// <param name="dsService">数据源服务</param>
    /// <param name="tenant">多租户访问器</param>
    public DataSourceController(JuggleDbContext db, DataSourceService dsService, ITenantAccessor tenant)
    {
        _db        = db;
        _dsService = dsService;
        _tenant    = tenant;
    }

    /// <summary>
    /// 新增数据源
    /// </summary>
    /// <param name="req">数据源信息</param>
    /// <returns>新增的数据源 ID</returns>
    [HttpPost("add")]
    public async Task<ApiResult> Add([FromBody] DataSourceAddRequest req)
    {
        var entity = new DataSourceEntity
        {
            DsName    = req.DsName,
            DsType    = req.DsType,
            Host      = req.Host,
            Port      = req.Port,
            DbName    = req.DbName,
            Username  = req.Username,
            Password  = req.Password,
            TenantId  = _tenant.TenantId,
            CreatedAt = DateTime.Now.ToString("o")
        };
        _db.DataSources.Add(entity);
        await _db.SaveChangesAsync();
        return ApiResult.Success(entity.Id);
    }

    [HttpDelete("delete/{id}")]
    public async Task<ApiResult> Delete(long id)
    {
        var entity = await _db.DataSources.FindAsync(id);
        if (entity == null) return ApiResult.Fail("数据源不存在");
        entity.Deleted = 1;
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpPut("update")]
    public async Task<ApiResult> Update([FromBody] DataSourceUpdateRequest req)
    {
        var entity = await _db.DataSources.FindAsync(req.Id);
        if (entity == null) return ApiResult.Fail("数据源不存在");
        entity.DsName    = req.DsName;
        entity.Host      = req.Host;
        entity.Port      = req.Port;
        entity.DbName    = req.DbName;
        entity.Username  = req.Username;
        entity.Password  = req.Password;
        entity.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpGet("list")]
    public async Task<ApiResult> List()
    {
        var list = await _db.DataSources
            .Where(d => d.Deleted == 0)
            .OrderByDescending(d => d.Id)
            .Select(d => new { dataSourceName = d.DsName, dataSourceType = d.DsType, id = d.Id })
            .ToListAsync();
        return ApiResult.Success(list);
    }

    /// <summary>测试数据源连接</summary>
    [HttpPost("test/{id}")]
    public async Task<ApiResult> Test(long id)
    {
        var (ok, msg) = await _dsService.TestConnectionAsync(id);
        return ok ? ApiResult.Success(msg) : ApiResult.Fail(msg);
    }

    [HttpPost("page")]
    public async Task<ApiResult> Page([FromBody] PageRequest req)
    {
        var query   = _db.DataSources.Where(d => d.Deleted == 0);
        var total   = await query.CountAsync();
        var records = await query
            .OrderByDescending(d => d.Id)
            .Skip((req.PageNum - 1) * req.PageSize)
            .Take(req.PageSize)
            .ToListAsync();
        return ApiResult.Success(new PageResult<DataSourceEntity>
        {
            Total = total, PageNum = req.PageNum, PageSize = req.PageSize, Records = records
        });
    }

    /// <summary>获取数据源的表/视图/存储过程列表（辅助 SQL 设计）</summary>
    [HttpPost("metadata")]
    public async Task<ApiResult> Metadata([FromBody] DataSourceMetadataRequest req)
    {
        try
        {
            var result = await _dsService.GetDbObjectsAsync(req.DataSourceName);
            return ApiResult.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail(ex.Message);
        }
    }

    /// <summary>获取表/视图的字段列表（辅助生成 SQL）</summary>
    [HttpPost("columns")]
    public async Task<ApiResult> Columns([FromBody] DataSourceColumnsRequest req)
    {
        try
        {
            var result = await _dsService.GetColumnsAsync(req.DataSourceName, req.TableName);
            return ApiResult.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail(ex.Message);
        }
    }

    /// <summary>单独测试 SQL：QUERY 返回前 100 行预览，UPDATE 事务执行后回滚</summary>
    [HttpPost("test-sql")]
    public async Task<ApiResult> TestSql([FromBody] DataSourceTestSqlRequest req)
    {
        try
        {
            var result = await _dsService.TestSqlAsync(req.DataSourceName, req.Sql, req.OperationType, req.Params);
            return ApiResult.Success(result);
        }
        catch (Exception ex)
        {
            return ApiResult.Fail(ex.Message);
        }
    }
}
