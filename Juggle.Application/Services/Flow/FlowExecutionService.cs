using System.Text.Json;
using Juggle.Application.Services;
using Juggle.Application.Services.Impl;
using Juggle.Domain.Engine;
using Juggle.Domain.Engine.NodeExecutors;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Application.Services.Flow;

/// <summary>
/// 流程执行核心服务 —— 统一封装数据源加载、静态变量快照/回写、日志持久化、引擎执行。
/// 消除 FlowDefinitionController 与 FlowOpenController 中的代码重复。
/// </summary>
public class FlowExecutionService
{
    private readonly JuggleDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITenantAccessor? _tenant;
    private readonly AiService _aiService;
    private readonly KnowledgeService _kbService;

    public FlowExecutionService(JuggleDbContext db, IHttpClientFactory httpClientFactory, AiService aiService,
        KnowledgeService kbService, ITenantAccessor? tenant = null)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _tenant = tenant;
        _aiService = aiService;
        _kbService = kbService;
    }

    /// <summary>读取 Redis 连接配置（系统配置表），未配置返回 null。</summary>
    /// <summary>读取全部 Redis 实例连接（实例ID → 连接串，0=默认实例）。</summary>
    private async Task<Dictionary<long, string>> GetRedisConnStrsAsync()
    {
        var map = new Dictionary<long, string>();
        var configs = await _db.RedisConfigs.Where(r => r.Deleted == 0).ToListAsync();
        foreach (var c in configs)
        {
            if (string.IsNullOrWhiteSpace(c.Host)) continue;
            var connStr = $"{(c.Host ?? "").Trim()}:{c.Port?.Trim() ?? "6379"},password={c.Password?.Trim()},defaultDatabase={c.Db?.Trim() ?? "0"},abortConnect=false,connectTimeout=5000";
            map[c.Id] = connStr;
            if (c.IsDefault == 1) map[0] = connStr;   // 0=默认实例
        }
        return map;
    }

    /// <summary>按技能 ID 列表拼接技能提示词（AI 节点 skills 使用）。</summary>
    private async Task<string> BuildSkillsPromptAsync(List<long> ids)
    {
        var skills = await _db.Skills
            .Where(s => ids.Contains(s.Id) && s.Deleted == 0 && s.Enabled == 1)
            .ToListAsync();
        if (skills.Count == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var s in skills)
        {
            sb.AppendLine($"## 技能：{s.SkillName}");
            sb.AppendLine(s.Content);
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    /// <summary>构建引擎（注入大模型对话/视觉函数、Redis 多实例连接、知识库检索与技能解析，供 AI/文件解析/Redis/KB_SEARCH 节点使用）。</summary>
    private async Task<FlowEngine> BuildEngineAsync(Dictionary<string, DataSourceInfo> dsInfos,
        Dictionary<string, string?> staticVars, Func<string, Task<string?>> flowContentLoader)
        => new FlowEngine(_httpClientFactory, dsInfos, staticVars, flowContentLoader,
            aiChatFunc: req => req.Images.Count > 0
                ? _aiService.ChatWithImagesAsync(req.SystemPrompt, req.UserInput, req.Images, req.ProviderId, req.Model)
                : _aiService.ChatAsync(req.SystemPrompt, req.UserInput, req.ProviderId, modelOverride: req.Model),
            redisConnStrs: await GetRedisConnStrsAsync(),
            kbSearchFunc: (kbId, query, topK) => _kbService.SearchAsContextAsync(kbId, query, topK),
            skillResolver: BuildSkillsPromptAsync);

    // ────────────────────────────────────────────────────────────────
    // 数据源
    // ────────────────────────────────────────────────────────────────

    /// <summary>从数据库加载全部数据源，构建名称 → DataSourceInfo 字典。</summary>
    public async Task<Dictionary<string, DataSourceInfo>> BuildDataSourceInfosAsync()
    {
        var dataSources = await _db.DataSources.Where(d => d.Deleted == 0).ToListAsync();
        var map = new Dictionary<string, DataSourceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var ds in dataSources)
        {
            if (string.IsNullOrEmpty(ds.DsName)) continue;
            map[ds.DsName] = new DataSourceInfo
            {
                DsType  = (ds.DsType ?? "sqlite").ToLower(),
                ConnStr = BuildConnectionString(ds),
                DsName  = ds.DsName
            };
        }
        return map;
    }

    /// <summary>根据数据源实体构建连接字符串（单条）。</summary>
    public static string BuildConnectionString(DataSourceEntity ds)
    {
        var dsType = (ds.DsType ?? "sqlite").ToLower();
        return dsType switch
        {
            "sqlite"                       => $"Data Source={(string.IsNullOrEmpty(ds.DbName) ? "juggle.db" : ds.DbName)}",
            "mysql"                        => $"Server={ds.Host};Port={ds.Port};Database={ds.DbName};User={ds.Username};Password={ds.Password};CharSet=utf8mb4;",
            "postgresql" or "postgres"     => $"Host={ds.Host};Port={ds.Port};Database={ds.DbName};Username={ds.Username};Password={ds.Password};No Reset On Close=true;",
            "sqlserver"  or "mssql"        => $"Server={ds.Host},{ds.Port};Database={ds.DbName};User Id={ds.Username};Password={ds.Password};TrustServerCertificate=True;",
            "oracle"                       => $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={ds.Host})(PORT={ds.Port}))(CONNECT_DATA=(SID={ds.DbName})));User Id={ds.Username};Password={ds.Password};",
            "dm"                           => $"Server={ds.Host};Port={ds.Port};Database={ds.DbName};User Id={ds.Username};PWD={ds.Password};",
            _                              => $"Data Source={(string.IsNullOrEmpty(ds.DbName) ? "juggle.db" : ds.DbName)}"
        };
    }

    // ────────────────────────────────────────────────────────────────
    // 静态变量
    // ────────────────────────────────────────────────────────────────

    /// <summary>加载全部静态变量，返回 VarCode → Value 快照。</summary>
    public async Task<Dictionary<string, string?>> BuildStaticVariableSnapshotAsync()
    {
        var vars = await _db.StaticVariables.Where(v => v.Deleted == 0).ToListAsync();
        return vars.ToDictionary(v => v.VarCode!, v => v.Value, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>将流程执行中被修改的静态变量回写到数据库。</summary>
    public async Task FlushStaticVariablesAsync(FlowContext context)
    {
        if (context.ModifiedStaticVarCodes.Count == 0) return;
        var codes    = context.ModifiedStaticVarCodes.ToList();
        var entities = await _db.StaticVariables
            .Where(v => v.Deleted == 0 && codes.Contains(v.VarCode!))
            .ToListAsync();
        foreach (var entity in entities)
        {
            if (context.StaticVariables.TryGetValue(entity.VarCode!, out var newVal))
            {
                entity.Value     = newVal;
                entity.UpdatedAt = DateTime.Now.ToString("o");
            }
        }
        await _db.SaveChangesAsync();
    }

    // ────────────────────────────────────────────────────────────────
    // 流程日志
    // ────────────────────────────────────────────────────────────────

    /// <summary>将流程执行结果持久化为日志记录（含节点明细）。</summary>
    /// <param name="definition">流程定义实体（用于填写 flowKey/flowName）</param>
    /// <param name="triggerType">触发来源标识：debug / open / version</param>
    /// <param name="version">版本号，为空时从 result.Context 中读取</param>
    public async Task<long> SaveFlowLogAsync(
        FlowDefinitionEntity definition,
        string               triggerType,
        FlowResult           result,
        DateTime             startTime,
        string               inputJson,
        string               version = "")
    {
        var logVersion = string.IsNullOrEmpty(version)
            ? (result.Context?.Version ?? triggerType)
            : version;

        var log = new FlowLogEntity
        {
            FlowKey      = definition.FlowKey,
            FlowName     = definition.FlowName,
            Version      = logVersion,
            TriggerType  = triggerType,
            Status       = result.Success ? "SUCCESS" : "FAILED",
            StartTime    = startTime.ToString("o"),
            EndTime      = DateTime.Now.ToString("o"),
            CostMs       = result.CostMs,
            ErrorMessage = result.ErrorMessage,
            InputJson    = inputJson,
            OutputJson   = JsonSerializer.Serialize(result.OutputData),
            TenantId     = _tenant?.TenantId ?? definition.TenantId,
            CreatedAt    = startTime.ToString("o")
        };
        _db.FlowLogs.Add(log);
        await _db.SaveChangesAsync();

        // 保存节点明细
        if (result.Context?.NodeLogs.Count > 0)
        {
            var nodeLogs = result.Context.NodeLogs.Select(nl => new FlowNodeLogEntity
            {
                FlowLogId      = log.Id,
                NodeKey        = nl.NodeKey,
                NodeLabel      = nl.NodeLabel,
                NodeType       = nl.NodeType,
                SeqNo          = nl.SeqNo,
                Status         = nl.Status,
                StartTime      = nl.StartTime.ToString("o"),
                EndTime        = nl.EndTime?.ToString("o"),
                CostMs         = nl.CostMs,
                InputSnapshot  = nl.InputSnapshot,
                OutputSnapshot = nl.OutputSnapshot,
                Detail         = nl.Detail,
                ErrorMessage   = nl.ErrorMessage,
                TenantId       = log.TenantId,
                CreatedAt      = nl.StartTime.ToString("o")
            }).ToList();
            _db.FlowNodeLogs.AddRange(nodeLogs);
            await _db.SaveChangesAsync();
        }

        return log.Id;
    }

    // ────────────────────────────────────────────────────────────────
    // 端到端执行
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 端到端执行流程：加载数据源 + 静态变量 → 运行引擎 → 保存日志 → 回写静态变量。
    /// </summary>
    /// <param name="definition">流程定义（提供 flowKey/flowName/flowContent）</param>
    /// <param name="flowContent">实际运行的节点 JSON（debug 时用 definition.FlowContent，open/version 时用版本快照）</param>
    /// <param name="inputParams">入参字典</param>
    /// <param name="triggerType">debug / open / version</param>
    /// <param name="version">版本号（debug 时传空字符串）</param>
    /// <returns>执行结果（含 LogId、CostMs、Context）</returns>
    public async Task<FlowResult> RunAsync(
        FlowDefinitionEntity            definition,
        string                          flowContent,
        Dictionary<string, object?>     inputParams,
        string                          triggerType,
        string                          version = "")
    {
        var dsInfos     = await BuildDataSourceInfosAsync();
        var staticVars  = await BuildStaticVariableSnapshotAsync();

        // 子流程加载器：根据 flowKey 取最新已发布版本的 flowContent
        async Task<string?> FlowContentLoader(string flowKey)
        {
            var ver = await _db.FlowVersions
                .Where(v => v.FlowKey == flowKey && v.Status == 1 && v.Deleted == 0)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync();
            return ver?.FlowContent;
        }

        var engine      = await BuildEngineAsync(dsInfos, staticVars, FlowContentLoader);

        var inputJson  = JsonSerializer.Serialize(inputParams);
        var startTime  = DateTime.Now;

        var result = await engine.ExecuteAsync(
            flowContent, inputParams, definition.FlowKey!, triggerType);

        // 持久化日志
        var logId = await SaveFlowLogAsync(definition, triggerType, result, startTime, inputJson, version);
        result.LogId = logId;

        // 回写被修改的静态变量
        if (result.Context != null)
            await FlushStaticVariablesAsync(result.Context);

        // 全局告警：流程失败时检查告警配置并通知
        if (!result.Success && triggerType != "debug")
        {
            try { await SendAlertAsync(definition, result, logId); } catch { /* 告警失败不影响主流程 */ }
        }

        return result;
    }

    // ────────────────────────────────────────────────────────────────
    // 全局告警
    // ────────────────────────────────────────────────────────────────

    private async Task SendAlertAsync(FlowDefinitionEntity definition, FlowResult result, long logId)
    {
        // 读取告警配置
        var configKeys = new[] { "alert.enabled", "alert.webhook.url", "alert.webhook.secret",
                                  "alert.email.to", "alert.on.fail.enabled" };
        var configs = await _db.SystemConfigs
            .Where(c => c.Deleted == 0 && configKeys.Contains(c.ConfigKey))
            .ToListAsync();
        var cfgMap = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue ?? "");

        if (!cfgMap.TryGetValue("alert.enabled", out var enabled) || enabled != "true") return;
        if (!cfgMap.TryGetValue("alert.on.fail.enabled", out var failAlert) || failAlert != "true") return;

        var webhookUrl = cfgMap.GetValueOrDefault("alert.webhook.url", "");
        var emailTo    = cfgMap.GetValueOrDefault("alert.email.to", "");

        var alertBody = new
        {
            eventType    = "FLOW_FAILED",
            flowKey      = definition.FlowKey,
            flowName     = definition.FlowName,
            logId,
            errorMessage = result.ErrorMessage,
            time         = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        // Webhook 告警
        if (!string.IsNullOrEmpty(webhookUrl))
        {
            try
            {
                var client  = _httpClientFactory.CreateClient();
                var json    = System.Text.Json.JsonSerializer.Serialize(alertBody);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                await client.PostAsync(webhookUrl, content);
            }
            catch { /* webhook 发送失败不中断 */ }
        }

        // Email 告警（如果配置了 SMTP 和收件人）
        if (!string.IsNullOrEmpty(emailTo))
        {
            try { await SendEmailAlertAsync(definition, result, logId, emailTo); } catch { }
        }
    }

    private async Task SendEmailAlertAsync(FlowDefinitionEntity definition, FlowResult result, long logId, string emailTo)
    {
        var configKeys = new[] { "email.smtp.host", "email.smtp.port", "email.smtp.ssl",
                                  "email.smtp.username", "email.smtp.password",
                                  "email.from.address", "email.from.name" };
        var configs = await _db.SystemConfigs
            .Where(c => c.Deleted == 0 && configKeys.Contains(c.ConfigKey))
            .ToListAsync();
        var cfgMap = configs.ToDictionary(c => c.ConfigKey, c => c.ConfigValue ?? "");

        var smtpHost = cfgMap.GetValueOrDefault("email.smtp.host", "");
        if (string.IsNullOrEmpty(smtpHost)) return;

        int.TryParse(cfgMap.GetValueOrDefault("email.smtp.port", "465"), out int smtpPort);
        bool.TryParse(cfgMap.GetValueOrDefault("email.smtp.ssl", "true"), out bool useSsl);
        var smtpUser = cfgMap.GetValueOrDefault("email.smtp.username", "");
        var smtpPwd  = cfgMap.GetValueOrDefault("email.smtp.password", "");
        var fromAddr = cfgMap.GetValueOrDefault("email.from.address", smtpUser);
        var fromName = cfgMap.GetValueOrDefault("email.from.name", "Juggle告警");

        // 使用 MailKit 发送（若不可用则跳过）
        // 注：此处使用 System.Net.Mail 作为备用，实际项目可替换为 MailKit
        using var client = new System.Net.Mail.SmtpClient(smtpHost, smtpPort)
        {
            EnableSsl           = useSsl,
            Credentials         = new System.Net.NetworkCredential(smtpUser, smtpPwd),
            DeliveryMethod      = System.Net.Mail.SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false
        };
        var subject = $"[Juggle告警] 流程执行失败: {definition.FlowName}";
        var body    = $"流程Key: {definition.FlowKey}\n流程名: {definition.FlowName}\n" +
                      $"LogId: {logId}\n错误信息: {result.ErrorMessage}\n时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        var msg = new System.Net.Mail.MailMessage(
            new System.Net.Mail.MailAddress(fromAddr, fromName),
            new System.Net.Mail.MailAddress(emailTo))
        {
            Subject = subject,
            Body    = body
        };
        await client.SendMailAsync(msg);
    }

    // ────────────────────────────────────────────────────────────────
    // 异步执行（预写 RUNNING 日志 + 后台执行后更新）
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 异步执行前预写一条 RUNNING 状态的主日志，返回 logId。
    /// 调用方可立即将 logId 返回给外部，外部通过 logId 轮询结果。
    /// </summary>
    public async Task<long> CreateRunningLogAsync(
        FlowDefinitionEntity        definition,
        string                      version,
        Dictionary<string, object?> inputParams)
    {
        var log = new FlowLogEntity
        {
            FlowKey     = definition.FlowKey,
            FlowName    = definition.FlowName,
            Version     = version,
            TriggerType = "open_async",
            Status      = "RUNNING",
            StartTime   = DateTime.Now.ToString("o"),
            InputJson   = JsonSerializer.Serialize(inputParams),
            TenantId    = _tenant?.TenantId ?? definition.TenantId,
            CreatedAt   = DateTime.Now.ToString("o")
        };
        _db.FlowLogs.Add(log);
        await _db.SaveChangesAsync();
        return log.Id;
    }

    /// <summary>
    /// 后台执行流程，执行完毕后将结果更新到已有的 RUNNING 日志记录（通过 logId 定位）。
    /// </summary>
    public async Task RunAsyncWithLog(
        FlowDefinitionEntity        definition,
        string                      flowContent,
        Dictionary<string, object?> inputParams,
        string                      triggerType,
        string                      version,
        long                        logId)
    {
        var dsInfos    = await BuildDataSourceInfosAsync();
        var staticVars = await BuildStaticVariableSnapshotAsync();

        async Task<string?> FlowContentLoader(string flowKey)
        {
            var ver = await _db.FlowVersions
                .Where(v => v.FlowKey == flowKey && v.Status == 1 && v.Deleted == 0)
                .OrderByDescending(v => v.Id)
                .FirstOrDefaultAsync();
            return ver?.FlowContent;
        }

        var engine    = await BuildEngineAsync(dsInfos, staticVars, FlowContentLoader);
        var startTime = DateTime.Now;
        FlowResult result;
        try
        {
            result = await engine.ExecuteAsync(flowContent, inputParams, definition.FlowKey!, triggerType);
        }
        catch (Exception ex)
        {
            result = new FlowResult { Success = false, ErrorMessage = ex.Message };
        }

        // 更新已有的 RUNNING 日志为最终状态
        var log = await _db.FlowLogs.FindAsync(logId);
        if (log != null)
        {
            log.Status       = result.Success ? "SUCCESS" : "FAILED";
            log.EndTime      = DateTime.Now.ToString("o");
            log.CostMs       = result.CostMs > 0 ? result.CostMs : (long)(DateTime.Now - startTime).TotalMilliseconds;
            log.ErrorMessage = result.ErrorMessage;
            log.OutputJson   = JsonSerializer.Serialize(result.OutputData);
            log.UpdatedAt    = DateTime.Now.ToString("o");

            // 保存节点明细
            if (result.Context?.NodeLogs.Count > 0)
            {
                var nodeLogs = result.Context.NodeLogs.Select(nl => new FlowNodeLogEntity
                {
                    FlowLogId      = log.Id,
                    NodeKey        = nl.NodeKey,
                    NodeLabel      = nl.NodeLabel,
                    NodeType       = nl.NodeType,
                    SeqNo          = nl.SeqNo,
                    Status         = nl.Status,
                    StartTime      = nl.StartTime.ToString("o"),
                    EndTime        = nl.EndTime?.ToString("o"),
                    CostMs         = nl.CostMs,
                    InputSnapshot  = nl.InputSnapshot,
                    OutputSnapshot = nl.OutputSnapshot,
                    Detail         = nl.Detail,
                    ErrorMessage   = nl.ErrorMessage,
                    TenantId       = log.TenantId,
                    CreatedAt      = nl.StartTime.ToString("o")
                }).ToList();
                _db.FlowNodeLogs.AddRange(nodeLogs);
            }

            await _db.SaveChangesAsync();
        }

        // 回写被修改的静态变量
        if (result.Context != null)
            await FlushStaticVariablesAsync(result.Context);
    }
}
