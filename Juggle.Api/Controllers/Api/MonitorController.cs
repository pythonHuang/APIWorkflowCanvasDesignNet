using System.Text.Json;
using Juggle.Infrastructure.Persistence;
using Juggle.Application.Models.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Api.Controllers.Api;

[ApiController]
[Route("api/monitor")]
[Authorize]
public class MonitorController : ControllerBase
{
    private readonly JuggleDbContext _db;
    public MonitorController(JuggleDbContext db) => _db = db;

    /// <summary>API 拓扑图数据</summary>
    [HttpGet("topology")]
    public async Task<ApiResult> GetTopology()
    {
        var apis = await _db.Apis.Where(a => a.Deleted == 0).ToListAsync();
        var flows = await _db.FlowVersions
            .Where(v => v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .ToListAsync();

        // 节点：按 host 聚合 API
        var hostMap = new Dictionary<string, List<object>>();
        foreach (var api in apis)
        {
            var host = ExtractHost(api.Url);
            if (!hostMap.ContainsKey(host)) hostMap[host] = new List<object>();
            hostMap[host].Add(new
            {
                api.Id, api.MethodCode, api.MethodName, api.RequestType,
                api.Url, api.MethodType, api.Status, api.ServiceAlias
            });
        }

        var nodes = hostMap.Select(kv => new
        {
            id = kv.Key,
            label = kv.Key,
            apiCount = kv.Value.Count,
            apis = kv.Value
        }).ToList();

        // 连线：从流程设计中取 METHOD 节点调用链
        var edges = new List<object>();
        foreach (var flowVer in flows)
        {
            if (string.IsNullOrEmpty(flowVer.FlowContent)) continue;
            try
            {
                var nodes_ = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                if (nodes_ == null) continue;
                // 找 METHOD 节点并按执行顺序（通过 outgoings 连接）构建调用链
                var methodNodes = nodes_.Where(n =>
                    n.TryGetProperty("elementType", out var et) && et.GetString() == "METHOD").ToList();
                for (int i = 0; i < methodNodes.Count; i++)
                {
                    var mn = methodNodes[i];
                    var key = mn.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    // 查找该 METHOD 节点的后继节点
                    var outgoings = new List<string>();
                    if (mn.TryGetProperty("outgoings", out var og) && og.ValueKind == JsonValueKind.Array)
                        foreach (var o in og.EnumerateArray()) outgoings.Add(o.GetString() ?? "");
                    // 找后继 METHOD 节点
                    foreach (var outKey in outgoings)
                    {
                        var nextMethod = methodNodes.FirstOrDefault(n =>
                            n.TryGetProperty("key", out var nk) && nk.GetString() == outKey);
                        if (nextMethod.ValueKind == JsonValueKind.Undefined) continue;
                        var srcHost = GetMethodHost(mn, apis);
                        var tgtHost = GetMethodHost(nextMethod, apis);
                        if (!string.IsNullOrEmpty(srcHost) && !string.IsNullOrEmpty(tgtHost) && srcHost != tgtHost)
                        {
                            var srcApi = GetMethodApiName(mn, apis);
                            var tgtApi = GetMethodApiName(nextMethod, apis);
                            edges.Add(new
                            {
                                source = srcHost, target = tgtHost,
                                label = $"{srcApi}→{tgtApi}",
                                flowKey = flowVer.FlowKey
                            });
                        }
                    }
                }
            }
            catch { }
        }

        // 去重连线
        var uniqueEdges = edges.GroupBy(e =>
        {
            dynamic d = e;
            return $"{d.source}→{d.target}";
        }).Select(g => g.First()).ToList();

        // 状态：检查最近调用日志
        var recentLogs = await _db.FlowLogs
            .Where(l => l.Deleted == 0)
            .OrderByDescending(l => l.Id)
            .Take(100)
            .ToListAsync();
        var failedHosts = new HashSet<string>();
        foreach (var log in recentLogs)
        {
            if (log.Status == "FAILED" && !string.IsNullOrEmpty(log.FlowKey))
            {
                // 查找该流程的METHOD节点涉及的hosts
                var flowVer = flows.FirstOrDefault(v => v.FlowKey == log.FlowKey);
                if (flowVer?.FlowContent != null)
                {
                    try
                    {
                        var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                        if (fNodes != null)
                        {
                            foreach (var n in fNodes)
                            {
                                if (n.TryGetProperty("elementType", out var et) && et.GetString() == "METHOD")
                                {
                                    var host = GetMethodHost(n, apis);
                                    if (!string.IsNullOrEmpty(host)) failedHosts.Add(host);
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
        }

        var nodeStatus = nodes.Select(n =>
        {
            var status = failedHosts.Contains(n.id) ? "warning" : "online";
            return new { n.id, n.label, n.apiCount, n.apis, status };
        }).ToList();

        return ApiResult.Success(new { nodes = nodeStatus, edges = uniqueEdges });
    }

    /// <summary>指定流程拓扑图</summary>
    [HttpGet("flow-topology/{flowKey}")]
    public async Task<ApiResult> GetFlowTopology(string flowKey)
    {
        var flowVer = await _db.FlowVersions
            .Where(v => v.FlowKey == flowKey && v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync();
        if (flowVer == null || string.IsNullOrEmpty(flowVer.FlowContent))
            return ApiResult.Fail("流程不存在或无内容");

        var apis = await _db.Apis.Where(a => a.Deleted == 0).ToListAsync();
        var nodes = new List<object>();
        var edges = new List<object>();

        try
        {
            var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
            if (fNodes != null)
            {
                foreach (var n in fNodes)
                {
                    var type = n.TryGetProperty("elementType", out var et) ? et.GetString() ?? "" : "";
                    var key_ = n.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    var label = n.TryGetProperty("label", out var lb) ? lb.GetString() ?? key_ : key_;
                    nodes.Add(new { id = key_, label, type });

                    if (type == "METHOD")
                    {
                        var host = GetMethodHost(n, apis);
                        if (!string.IsNullOrEmpty(host))
                            nodes.Add(new { id = $"{key_}_api", label = host, type = "API" });
                    }

                    if (n.TryGetProperty("outgoings", out var og) && og.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var o in og.EnumerateArray())
                            edges.Add(new { source = key_, target = o.GetString(), label = "" });
                    }
                }
            }
        }
        catch { }

        return ApiResult.Success(new { nodes, edges });
    }

    // ===== helpers =====

    private static string ExtractHost(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "unknown";
        try { var u = new Uri(url); return $"{u.Scheme}://{u.Host}:{u.Port}"; }
        catch { return url.Split('/').FirstOrDefault() ?? "unknown"; }
    }

    private static string GetMethodHost(JsonElement node, List<Domain.Entities.ApiEntity> apis)
    {
        if (!node.TryGetProperty("method", out var m)) return "";
        var suiteCode = m.TryGetProperty("suiteCode", out var sc) ? sc.GetString() ?? "" : "";
        var methodCode = m.TryGetProperty("methodCode", out var mc) ? mc.GetString() ?? "" : "";
        var api = apis.FirstOrDefault(a => a.SuiteCode == suiteCode && a.MethodCode == methodCode);
        return api != null ? ExtractHost(api.Url) : "";
    }

    private static string GetMethodApiName(JsonElement node, List<Domain.Entities.ApiEntity> apis)
    {
        if (!node.TryGetProperty("method", out var m)) return "?";
        var suiteCode = m.TryGetProperty("suiteCode", out var sc) ? sc.GetString() ?? "" : "";
        var methodCode = m.TryGetProperty("methodCode", out var mc) ? mc.GetString() ?? "" : "";
        var api = apis.FirstOrDefault(a => a.SuiteCode == suiteCode && a.MethodCode == methodCode);
        return api?.MethodName ?? methodCode;
    }
}
