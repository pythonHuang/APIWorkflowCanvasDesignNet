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

    /// <summary>API 拓扑图数据（含 API 节点 + 数据库节点）</summary>
    [HttpGet("topology")]
    public async Task<ApiResult> GetTopology()
    {
        var apis = await _db.Apis.Where(a => a.Deleted == 0).ToListAsync();
        var dataSources = await _db.DataSources.Where(d => d.Deleted == 0).ToListAsync();
        var flows = await _db.FlowVersions
            .Where(v => v.Status == 1 && v.Deleted == 0)
            .OrderByDescending(v => v.Id)
            .ToListAsync();
        var recentLogs = await _db.FlowLogs
            .Where(l => l.Deleted == 0)
            .OrderByDescending(l => l.Id)
            .Take(200)
            .ToListAsync();

        var allNodes = new List<object>();
        var edges = new List<object>();
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        // ===== API 节点 =====
        var hostMap = new Dictionary<string, List<object>>();
        foreach (var api in apis)
        {
            var host = ExtractHost(api.Url);
            if (!hostMap.ContainsKey(host)) hostMap[host] = new List<object>();
            var apiLogs = FindApiLogs(recentLogs, flows, api);
            hostMap[host].Add(new
            {
                type = "api",
                api.Id, api.MethodCode, api.MethodName, api.RequestType,
                api.Url, api.MethodType, api.Status, api.ServiceAlias,
                callCount = apiLogs.calls, successCount = apiLogs.success, failCount = apiLogs.fail
            });
        }

        // API 节点健康检查
        var healthMap = new Dictionary<string, string>();
        foreach (var host in hostMap.Keys)
        {
            try
            {
                var resp = await httpClient.GetAsync(host, HttpCompletionOption.ResponseHeadersRead);
                healthMap[host] = resp.IsSuccessStatusCode ? "online" : "warning";
            }
            catch { healthMap[host] = "offline"; }
        }

        foreach (var kv in hostMap)
        {
            var status = healthMap.GetValueOrDefault(kv.Key, "online");
            var totalCalls = kv.Value.Sum(a => (int)((dynamic)a).callCount);
            var totalS = kv.Value.Sum(a => (int)((dynamic)a).successCount);
            var totalF = kv.Value.Sum(a => (int)((dynamic)a).failCount);
            allNodes.Add(new
            {
                id = kv.Key, label = kv.Key, nodeType = "api",
                apiCount = kv.Value.Count, apis = kv.Value,
                status, totalCalls, totalSuccess = totalS, totalFail = totalF
            });
        }

        // ===== 数据库节点 =====
        foreach (var ds in dataSources)
        {
            var dbId = $"db_{ds.DsName}";
            var dbHost = !string.IsNullOrEmpty(ds.Host) ? $"{ds.Host}:{ds.Port}" : ds.DsName;
            var dbLabel = $"{ds.DsName}({ds.DsType})";
            // DB 连接健康检查
            var dbStatus = "online";
            try
            {
                if (!string.IsNullOrEmpty(ds.Host) && ds.Port > 0)
                    using (var tcp = new System.Net.Sockets.TcpClient())
                    { await tcp.ConnectAsync(ds.Host!, ds.Port); dbStatus = "online"; }
                else
                    dbStatus = "unknown";
            }
            catch { dbStatus = "offline"; }

            // DB 调用统计
            int dbCalls = 0, dbSuccess = 0, dbFail = 0;
            foreach (var log in recentLogs)
            {
                var flowVer = flows.FirstOrDefault(v => v.FlowKey == log.FlowKey);
                if (flowVer?.FlowContent == null) continue;
                try
                {
                    var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                    if (fNodes == null) continue;
                    foreach (var n in fNodes)
                    {
                        if (!n.TryGetProperty("elementType", out var et)) continue;
                        var eType = et.GetString() ?? "";
                        if (eType != "MYSQL" && eType != "DB") continue;
                        if (!n.TryGetProperty("dbConfig", out var dbCfg)) continue;
                        var dsName = dbCfg.TryGetProperty("dataSourceName", out var dsn) ? dsn.GetString() ?? "" : "";
                        if (dsName == ds.DsName)
                        {
                            dbCalls++;
                            if (log.Status == "SUCCESS") dbSuccess++; else dbFail++;
                        }
                    }
                }
                catch { }
            }

            allNodes.Add(new
            {
                id = dbId, label = dbLabel, nodeType = "db",
                dbName = ds.DsName, dbType = ds.DsType, dbHost,
                apiCount = 0, apis = new List<object>(),
                status = dbStatus, totalCalls = dbCalls, totalSuccess = dbSuccess, totalFail = dbFail
            });
        }

        // ===== 检查日志失败更新状态 =====
        foreach (var log in recentLogs.Where(l => l.Status == "FAILED"))
        {
            var flowVer = flows.FirstOrDefault(v => v.FlowKey == log.FlowKey);
            if (flowVer?.FlowContent == null) continue;
            try
            {
                var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                if (fNodes == null) continue;
                foreach (var n in fNodes)
                {
                    if (!n.TryGetProperty("elementType", out var et)) continue;
                    var eType = et.GetString() ?? "";
                    if (eType == "METHOD")
                    {
                        var host = GetMethodHost(n, apis);
                        if (!string.IsNullOrEmpty(host) && healthMap.GetValueOrDefault(host) == "online")
                            healthMap[host] = "warning";
                    }
                    else if (eType == "MYSQL" || eType == "DB")
                    {
                        if (n.TryGetProperty("dbConfig", out var dbCfg))
                        {
                            var dsName = dbCfg.TryGetProperty("dataSourceName", out var dsn) ? dsn.GetString() ?? "" : "";
                            var dbId = $"db_{dsName}";
                            if (!string.IsNullOrEmpty(dsName))
                            {
                                var dbNode = allNodes.FirstOrDefault(nd => ((dynamic)nd).id == dbId);
                                if (dbNode != null && ((dynamic)dbNode).status == "online")
                                {
                                    ((dynamic)dbNode).status = "warning";
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // Sync healthMap back to API nodes
        foreach (var n in allNodes.Where(n => ((dynamic)n).nodeType == "api"))
        {
            dynamic dn = n;
            string nodeId = dn.id;
            if (healthMap.TryGetValue(nodeId, out var hs) && hs == "warning")
                dn.status = "warning";
        }

        // ===== 连线：API→API + API→DB =====
        foreach (var flowVer in flows)
        {
            if (string.IsNullOrEmpty(flowVer.FlowContent)) continue;
            try
            {
                var fNodes = JsonSerializer.Deserialize<List<JsonElement>>(flowVer.FlowContent);
                if (fNodes == null) continue;
                var businessNodes = fNodes.Where(n =>
                    n.TryGetProperty("elementType", out var et) &&
                    (et.GetString() == "METHOD" || et.GetString() == "MYSQL" || et.GetString() == "DB")).ToList();

                for (int i = 0; i < businessNodes.Count; i++)
                {
                    var src = businessNodes[i];
                    var srcType = src.TryGetProperty("elementType", out var set) ? set.GetString() ?? "" : "";
                    var outgoings = new List<string>();
                    if (src.TryGetProperty("outgoings", out var og) && og.ValueKind == JsonValueKind.Array)
                        foreach (var o in og.EnumerateArray()) outgoings.Add(o.GetString() ?? "");

                    foreach (var outKey in outgoings)
                    {
                        var tgt = businessNodes.FirstOrDefault(n =>
                            n.TryGetProperty("key", out var nk) && nk.GetString() == outKey);
                        if (tgt.ValueKind == JsonValueKind.Undefined) continue;
                        var tgtType = tgt.TryGetProperty("elementType", out var tet) ? tet.GetString() ?? "" : "";

                        string? srcHost = null, tgtHost = null;
                        string srcLabel = "", tgtLabel = "";

                        // Source
                        if (srcType == "METHOD")
                        {
                            srcHost = GetMethodHost(src, apis);
                            srcLabel = GetMethodApiName(src, apis);
                        }
                        else // DB
                        {
                            var dsName = GetDbDataSourceName(src);
                            srcHost = $"db_{dsName}";
                            srcLabel = dsName;
                        }

                        // Target
                        if (tgtType == "METHOD")
                        {
                            tgtHost = GetMethodHost(tgt, apis);
                            tgtLabel = GetMethodApiName(tgt, apis);
                        }
                        else
                        {
                            var dsName = GetDbDataSourceName(tgt);
                            tgtHost = $"db_{dsName}";
                            tgtLabel = dsName;
                        }

                        if (srcHost == tgtHost || string.IsNullOrEmpty(srcHost) || string.IsNullOrEmpty(tgtHost)) continue;

                        edges.Add(new
                        {
                            source = srcHost, target = tgtHost,
                            label = $"{srcLabel}→{tgtLabel}",
                            flowKey = flowVer.FlowKey,
                            status = "online"
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

        return ApiResult.Success(new { nodes = allNodes, edges = uniqueEdges });
    }

    private static string GetDbDataSourceName(JsonElement node)
    {
        if (node.TryGetProperty("dbConfig", out var cfg) &&
            cfg.TryGetProperty("dataSourceName", out var dsn))
            return dsn.GetString() ?? "";
        return "";
    }

    private static (int calls, int success, int fail) FindApiLogs(
        List<Domain.Entities.FlowLogEntity> logs, List<Domain.Entities.FlowVersionEntity> flows,
        Domain.Entities.ApiEntity api)
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
