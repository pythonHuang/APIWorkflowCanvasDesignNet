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

        // 访问统计：最近200条日志
        var recentLogs = await _db.FlowLogs
            .Where(l => l.Deleted == 0)
            .OrderByDescending(l => l.Id)
            .Take(200)
            .ToListAsync();

        // 节点：按 host 聚合 API + 统计
        var hostMap = new Dictionary<string, List<object>>();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        foreach (var api in apis)
        {
            var host = ExtractHost(api.Url);
            if (!hostMap.ContainsKey(host)) hostMap[host] = new List<object>();
            // API 级别统计
            var apiLogs = FindApiLogs(recentLogs, flows, api, host);
            hostMap[host].Add(new
            {
                api.Id, api.MethodCode, api.MethodName, api.RequestType,
                api.Url, api.MethodType, api.Status, api.ServiceAlias,
                callCount = apiLogs.calls, successCount = apiLogs.success, failCount = apiLogs.fail
            });
        }

        // 健康检查
        var hostHealth = new Dictionary<string, string>();
        foreach (var host in hostMap.Keys)
        {
            try
            {
                var resp = await httpClient.GetAsync(host, HttpCompletionOption.ResponseHeadersRead);
                hostHealth[host] = resp.IsSuccessStatusCode ? "online" : "warning";
            }
            catch { hostHealth[host] = "offline"; }
        }

        // 检查日志中的失败
        foreach (var log in recentLogs.Where(l => l.Status == "FAILED"))
        {
            var flowVer = flows.FirstOrDefault(v => v.FlowKey == log.FlowKey);
            if (flowVer?.FlowContent == null) continue;
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
                            if (!string.IsNullOrEmpty(host) && hostHealth.GetValueOrDefault(host) == "online")
                                hostHealth[host] = "warning";
                        }
                    }
                }
            }
            catch { }
        }

        var nodes = hostMap.Select(kv =>
        {
            var status = hostHealth.GetValueOrDefault(kv.Key, "online");
            var totalCalls = kv.Value.Sum(a => (int)((dynamic)a).callCount);
            var totalSuccess = kv.Value.Sum(a => (int)((dynamic)a).successCount);
            var totalFail = kv.Value.Sum(a => (int)((dynamic)a).failCount);
            return new
            {
                id = kv.Key, label = kv.Key,
                apiCount = kv.Value.Count, apis = kv.Value,
                status, totalCalls, totalSuccess, totalFail
            };
        }).ToList();

        // 连线
        var edges = new List<object>();
        foreach (var flowVer in flows)
        {
            if (string.IsNullOrEmpty(flowVer.FlowContent)) continue;
            try
            {
                var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                if (fNodes == null) continue;
                var methodNodes = fNodes.Where(n =>
                    n.TryGetProperty("elementType", out var et) && et.GetString() == "METHOD").ToList();
                for (int i = 0; i < methodNodes.Count; i++)
                {
                    var mn = methodNodes[i];
                    var outgoings = new List<string>();
                    if (mn.TryGetProperty("outgoings", out var og) && og.ValueKind == JsonValueKind.Array)
                        foreach (var o in og.EnumerateArray()) outgoings.Add(o.GetString() ?? "");
                    foreach (var outKey in outgoings)
                    {
                        var nextMethod = methodNodes.FirstOrDefault(n =>
                            n.TryGetProperty("key", out var nk) && nk.GetString() == outKey);
                        if (nextMethod.ValueKind == JsonValueKind.Undefined) continue;
                        var srcHost = GetMethodHost(mn, apis);
                        var tgtHost = GetMethodHost(nextMethod, apis);
                        if (srcHost == tgtHost || string.IsNullOrEmpty(srcHost) || string.IsNullOrEmpty(tgtHost)) continue;
                        var srcApi = GetMethodApiName(mn, apis);
                        var tgtApi = GetMethodApiName(nextMethod, apis);
                        var edgeStatus = hostHealth.GetValueOrDefault(tgtHost, "online");
                        edges.Add(new
                        {
                            source = srcHost, target = tgtHost,
                            label = $"{srcApi}→{tgtApi}",
                            flowKey = flowVer.FlowKey,
                            status = edgeStatus
                        });
                    }
                }
            }
            catch { }
        }

        var uniqueEdges = edges.GroupBy(e =>
        {
            dynamic d = e;
            return $"{(string)d.source}→{(string)d.target}";
        }).Select(g => g.First()).ToList();

        return ApiResult.Success(new { nodes, edges = uniqueEdges });
    }

    private static (int calls, int success, int fail) FindApiLogs(
        List<Domain.Entities.FlowLogEntity> logs, List<Domain.Entities.FlowVersionEntity> flows,
        Domain.Entities.ApiEntity api, string host)
    {
        int calls = 0, success = 0, fail = 0;
        foreach (var log in logs)
        {
            var flowVer = flows.FirstOrDefault(v => v.FlowKey == log.FlowKey);
            if (flowVer?.FlowContent == null) continue;
            try
            {
                var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                if (fNodes == null) continue;
                foreach (var n in fNodes)
                {
                    if (!n.TryGetProperty("elementType", out var et) || et.GetString() != "METHOD") continue;
                    if (!n.TryGetProperty("method", out var m)) continue;
                    var sc = m.TryGetProperty("suiteCode", out var scc) ? scc.GetString() ?? "" : "";
                    var mc = m.TryGetProperty("methodCode", out var mcc) ? mcc.GetString() ?? "" : "";
                    if (sc == api.SuiteCode && mc == api.MethodCode)
                    {
                        calls++;
                        if (log.Status == "SUCCESS") success++;
                        else fail++;
                    }
                }
            }
            catch { }
        }
        return (calls, success, fail);
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
