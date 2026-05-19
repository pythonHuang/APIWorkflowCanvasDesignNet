using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Models.Request;
using Juggle.Application.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

[ApiController]
[Route("api/parameter")]
[Authorize]
public class ParameterController : ControllerBase
{
    private readonly JuggleDbContext _db;

    public ParameterController(JuggleDbContext db) => _db = db;

    /// <summary>保存参数列表（全量替换）</summary>
    [HttpPost("save")]
    public async Task<ApiResult> Save([FromBody] ParameterSaveRequest req)
    {
        // 删除旧参数
        var old = await _db.Parameters
            .Where(p => p.OwnerId == req.OwnerId && p.ParamType == req.ParamType && p.Deleted == 0)
            .ToListAsync();
        foreach (var p in old) { p.Deleted = 1; p.UpdatedAt = DateTime.Now.ToString("o"); }

        // 添加新参数
        int sort = 1;
        foreach (var item in req.Parameters)
        {
            _db.Parameters.Add(new ParameterEntity
            {
                OwnerId       = req.OwnerId,
                OwnerCode     = req.OwnerCode,
                ParamType     = req.ParamType,
                ParamCode     = item.ParamCode,
                ParamName     = item.ParamName,
                DataType      = item.DataType,
                ObjectCode    = item.ObjectCode,
                Required      = item.Required,
                DefaultValue  = item.DefaultValue,
                ParamPosition = item.ParamPosition,
                Description   = item.Description,
                SortNum       = sort++,
                CreatedAt     = DateTime.Now.ToString("o")
            });
        }
        await _db.SaveChangesAsync();
        return ApiResult.Success();
    }

    [HttpGet("list")]
    public async Task<ApiResult> List([FromQuery] long ownerId, [FromQuery] int paramType)
    {
        var list = await _db.Parameters
            .Where(p => p.OwnerId == ownerId && p.ParamType == paramType && p.Deleted == 0)
            .OrderBy(p => p.SortNum)
            .ToListAsync();
        return ApiResult.Success(list);
    }
}
