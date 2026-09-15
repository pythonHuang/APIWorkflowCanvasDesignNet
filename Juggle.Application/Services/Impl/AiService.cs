using System.Text;
using System.Text.Json;
using Juggle.Domain.Engine.NodeExecutors;
using Juggle.Domain.Entities;
using Juggle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Juggle.Application.Services.Impl;

/// <summary>
/// AI 大模型服务 —— OpenAI 兼容的 chat/completions 接口
/// （支持 DeepSeek / 通义千问 / Kimi / OpenAI 等，通过系统配置设置 BaseUrl/ApiKey/Model）。
/// 核心能力：根据自然语言需求生成接口流程编排 JSON。
/// </summary>
public class AiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JuggleDbContext _db;

    public AiService(IHttpClientFactory httpClientFactory, JuggleDbContext db)
    {
        _httpClientFactory = httpClientFactory;
        _db = db;
    }

    // ==================== 配置（兼容旧版 ai.* 单配置） ====================

    /// <summary>读取旧版 AI 配置（系统配置表：ai.baseUrl / ai.apiKey / ai.model）。</summary>
    public async Task<AiConfig> GetConfigAsync()
    {
        var configs = await _db.SystemConfigs
            .Where(c => c.Deleted == 0 && c.ConfigKey!.StartsWith("ai."))
            .ToListAsync();
        string? Get(string key) => configs.FirstOrDefault(c => c.ConfigKey == key)?.ConfigValue;
        return new AiConfig
        {
            BaseUrl = Get("ai.baseUrl") ?? "",
            ApiKey  = Get("ai.apiKey") ?? "",
            Model   = Get("ai.model") ?? "deepseek-chat"
        };
    }

    /// <summary>保存旧版 AI 配置（按 key upsert）。</summary>
    public async Task SaveConfigAsync(string baseUrl, string apiKey, string model)
    {
        await UpsertAsync("ai.baseUrl", baseUrl?.Trim() ?? "", "AI接口地址");
        await UpsertAsync("ai.apiKey", apiKey?.Trim() ?? "", "AI密钥");
        await UpsertAsync("ai.model", model?.Trim() ?? "", "AI模型");
    }

    private async Task UpsertAsync(string key, string value, string name)
    {
        var entity = await _db.SystemConfigs.FirstOrDefaultAsync(c => c.ConfigKey == key);
        if (entity == null)
        {
            _db.SystemConfigs.Add(new SystemConfigEntity
            {
                ConfigKey = key, ConfigValue = value, ConfigName = name, ConfigGroup = "AI模型",
                CreatedAt = DateTime.Now.ToString("o")
            });
        }
        else
        {
            entity.ConfigValue = value;
            entity.UpdatedAt = DateTime.Now.ToString("o");
        }
        await _db.SaveChangesAsync();
    }

    // ==================== 供应商管理 ====================

    /// <summary>供应商列表（含启停状态）。</summary>
    public async Task<List<AiProviderEntity>> GetProvidersAsync()
        => await _db.AiProviders.Where(p => p.Deleted == 0).OrderByDescending(p => p.Id).ToListAsync();

    /// <summary>启用的供应商列表。</summary>
    public async Task<List<AiProviderEntity>> GetEnabledProvidersAsync()
        => await _db.AiProviders.Where(p => p.Deleted == 0 && p.Enabled == 1).OrderBy(p => p.Id).ToListAsync();

    /// <summary>按 ID 或第一个启用供应商解析对话配置。</summary>
    private async Task<AiConfig> ResolveConfigAsync(long? providerId)
    {
        AiProviderEntity? provider = null;
        if (providerId > 0)
        {
            provider = await _db.AiProviders.FirstOrDefaultAsync(p => p.Id == providerId && p.Deleted == 0);
        }
        provider ??= await _db.AiProviders.FirstOrDefaultAsync(p => p.Deleted == 0 && p.Enabled == 1);
        if (provider != null)
        {
            return new AiConfig { BaseUrl = provider.BaseUrl ?? "", ApiKey = provider.ApiKey ?? "", Model = provider.Model ?? "" };
        }
        // 兼容旧版单配置
        return await GetConfigAsync();
    }

    // ==================== 对话 ====================

    /// <summary>调用大模型对话（OpenAI 兼容接口）。providerId 指定供应商，0=第一个启用供应商；modelOverride 可在供应商可用模型内切换。</summary>
    public async Task<string> ChatAsync(string systemPrompt, string userPrompt, long providerId = 0, AiConfig? config = null, string? modelOverride = null,
        double? temperature = null, int? maxTokens = null, int? seed = null, bool enableThinking = false)
    {
        var messages = new List<(string Role, string Content)>
        {
            ("system", systemPrompt),
            ("user", userPrompt)
        };
        return await ChatMessagesAsync(messages, providerId, config, modelOverride, temperature, maxTokens, seed, enableThinking);
    }

    /// <summary>视觉对话：文本 + 图片（OpenAI 兼容多模态 content 数组）。</summary>
    public async Task<string> ChatWithImagesAsync(string systemPrompt, string userText, List<string> imageUrls,
        long providerId = 0, string? modelOverride = null)
    {
        var cfg = await ResolveConfigAsync(providerId > 0 ? providerId : null);
        if (!string.IsNullOrEmpty(modelOverride)) cfg.Model = modelOverride;
        if (string.IsNullOrEmpty(cfg.BaseUrl) || string.IsNullOrEmpty(cfg.ApiKey))
            throw new Exception("未配置 AI 大模型：请在系统设置 → 大模型设置中添加并启用供应商");

        var url = cfg.BaseUrl.TrimEnd('/');
        if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";

        // 多模态 content：[{type:text,text},{type:image_url,image_url:{url}}]
        var content = new List<object>();
        if (!string.IsNullOrWhiteSpace(userText))
            content.Add(new { type = "text", text = userText });
        foreach (var img in imageUrls)
            content.Add(new { type = "image_url", image_url = new { url = img } });

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
        reqMsg.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.ApiKey}");
        reqMsg.Content = new StringContent(JsonSerializer.Serialize(new
        {
            model = cfg.Model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = content.ToArray() }
            },
            temperature = 0.2
        }), Encoding.UTF8, "application/json");
        using var resp = await client.SendAsync(reqMsg);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"AI 调用失败({(int)resp.StatusCode}): {Truncate(body, 300)}");
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
    }

    /// <summary>多轮对话：完整消息列表（system + 历史消息）一次性发送（支持温度/最大输出/随机种子/深度思考等模型参数）。</summary>
    public async Task<string> ChatMessagesAsync(List<(string Role, string Content)> messages, long providerId = 0, AiConfig? config = null, string? modelOverride = null,
        double? temperature = null, int? maxTokens = null, int? seed = null, bool enableThinking = false)
    {
        var cfg = config ?? await ResolveConfigAsync(providerId > 0 ? providerId : null);
        if (!string.IsNullOrEmpty(modelOverride)) cfg.Model = modelOverride;
        if (string.IsNullOrEmpty(cfg.BaseUrl) || string.IsNullOrEmpty(cfg.ApiKey))
            throw new Exception("未配置 AI 大模型：请在系统设置 → 大模型设置中添加并启用供应商");

        var url = cfg.BaseUrl.TrimEnd('/');
        if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";

        var payload = new Dictionary<string, object?>
        {
            ["model"] = cfg.Model,
            ["messages"] = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            ["temperature"] = temperature ?? 0.2
        };
        if (maxTokens is > 0) payload["max_tokens"] = maxTokens.Value;
        if (seed.HasValue) payload["seed"] = seed.Value;
        if (enableThinking) payload["enable_thinking"] = true;

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
        reqMsg.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.ApiKey}");
        reqMsg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(reqMsg);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"AI 调用失败({(int)resp.StatusCode}): {Truncate(body, 300)}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return content?.Trim() ?? "";
    }

    /// <summary>按 AI 节点请求调用大模型（统一入口：支持图片输入、温度/最大输出字数/随机种子/深度思考等模型参数，以及函数调用 tools 与多轮历史）。</summary>
    public async Task<AiChatResult> ChatRequestAsync(AiChatRequest req)
    {
        var cfg = await ResolveConfigAsync(req.ProviderId > 0 ? req.ProviderId : null);
        if (!string.IsNullOrEmpty(req.Model)) cfg.Model = req.Model;
        if (string.IsNullOrEmpty(cfg.BaseUrl) || string.IsNullOrEmpty(cfg.ApiKey))
            throw new Exception("未配置 AI 大模型：请在系统设置 → 大模型设置中添加并启用供应商");

        var url = cfg.BaseUrl.TrimEnd('/');
        if (!url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            url += "/chat/completions";

        // 图片输入 → 多模态 content 数组
        object userContent = req.UserInput;
        if (req.Images is { Count: > 0 })
        {
            var content = new List<object>();
            if (!string.IsNullOrWhiteSpace(req.UserInput))
                content.Add(new { type = "text", text = req.UserInput });
            foreach (var img in req.Images)
                content.Add(new { type = "image_url", image_url = new { url = img } });
            userContent = content.ToArray();
        }

        // 消息：system + user + 历史（工具调用多轮回传）
        var messages = new List<object>
        {
            new Dictionary<string, object?> { ["role"] = "system", ["content"] = req.SystemPrompt },
            new Dictionary<string, object?> { ["role"] = "user", ["content"] = userContent }
        };
        if (req.History is { Count: > 0 }) messages.AddRange(req.History);

        var payload = new Dictionary<string, object?>
        {
            ["model"] = cfg.Model,
            ["messages"] = messages,
            ["temperature"] = req.Temperature ?? 0.2
        };
        if (req.MaxTokens is > 0) payload["max_tokens"] = req.MaxTokens.Value;
        if (req.Seed.HasValue) payload["seed"] = req.Seed.Value;
        if (req.EnableThinking) payload["enable_thinking"] = true;
        if (!string.IsNullOrWhiteSpace(req.ToolsJson))
        {
            try { payload["tools"] = JsonSerializer.Deserialize<JsonElement>(req.ToolsJson); }
            catch { /* 工具定义非法则忽略 */ }
        }

        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        using var reqMsg = new HttpRequestMessage(HttpMethod.Post, url);
        reqMsg.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cfg.ApiKey}");
        reqMsg.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await client.SendAsync(reqMsg);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"AI 调用失败({(int)resp.StatusCode}): {Truncate(body, 300)}");

        using var doc = JsonDocument.Parse(body);
        var msg = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
        var result = new AiChatResult();
        if (msg.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            result.Text = contentEl.GetString()?.Trim() ?? "";
        if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in tcs.EnumerateArray())
            {
                if (!tc.TryGetProperty("function", out var fn)) continue;
                result.ToolCalls.Add(new AiToolCall
                {
                    Id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                    Name = fn.GetProperty("name").GetString() ?? "",
                    Arguments = fn.TryGetProperty("arguments", out var argsEl) ? argsEl.GetString() ?? "{}" : "{}"
                });
            }
        }
        return result;
    }

    private static string Truncate(string s, int len) => s.Length <= len ? s : s[..len] + "...";

    // ==================== 流程生成 ====================

    /// <summary>根据自然语言需求生成接口流程编排。</summary>
    public async Task<object> GenerateFlowAsync(string requirement, List<Dictionary<string, object?>>? apis,
        List<Dictionary<string, object?>>? inputParams, List<Dictionary<string, object?>>? outputParams, long providerId = 0, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(requirement))
            throw new Exception("请先描述编排需求");

        var apiSummary = BuildApiSummary(apis);
        var systemPrompt = BuildSystemPrompt();
        var userPrompt = $"""
            请根据以下需求生成接口流程编排 JSON：

            ## 需求
            {requirement.Trim()}

            ## 可用接口清单
            {apiSummary}

            ## 流程入参
            {BuildParamsSummary(inputParams)}

            ## 流程出参
            {BuildParamsSummary(outputParams)}

            请只输出 JSON，不要输出任何解释文字。
            """;

        var content = await ChatAsync(systemPrompt, userPrompt, providerId, modelOverride: model);
        var flow = ExtractAndValidateFlow(content);
        return flow;
    }

    /// <summary>根据需求生成套件与接口（JSON），供前端预览确认。</summary>
    public async Task<object> GenerateApisAsync(string requirement, List<Dictionary<string, object?>>? existingSuites, long providerId = 0, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(requirement))
            throw new Exception("请先描述接口接入需求");

        var suitesSummary = existingSuites is { Count: > 0 }
            ? "已存在套件（尽量复用）:\n" + string.Join("\n", existingSuites.Select(s => $"- {s.GetValueOrDefault("suiteCode")}：{s.GetValueOrDefault("suiteName")}")) + "\n"
            : "";
        var systemPrompt = """
            你是接口接入助手。根据用户需求，生成需要接入的套件与接口定义 JSON。
            只输出 JSON（不要 markdown 围栏、不要解释），结构如下：
            {
              "suites": [
                { "suiteCode": "user", "suiteName": "用户中心", "suiteDesc": "用户相关接口" }
              ],
              "apis": [
                {
                  "suiteCode": "user",                // 所属套件（必须与 suites 中一致）
                  "methodCode": "getUserInfo",        // 接口 code（唯一）
                  "methodName": "获取用户信息",
                  "methodDesc": "根据用户id查询用户信息",
                  "url": "/api/user/info",            // 接口路径
                  "requestType": "POST",              // GET / POST
                  "inputParams": [                    // 接口入参
                    { "paramCode": "userId", "paramName": "用户id", "paramType": "string", "paramPosition": "body", "required": 1, "description": "用户唯一标识" }
                  ],
                  "headerParams": [                   // 接口 Header 配置（鉴权/内容类型等）
                    { "paramCode": "Authorization", "paramName": "授权令牌", "paramType": "string", "required": 1, "description": "Bearer 令牌" }
                  ],
                  "outputParams": [                   // 接口出参（响应字段）
                    { "paramCode": "name", "paramName": "姓名", "paramType": "string", "required": 0, "description": "用户姓名" }
                  ]
                }
              ]
            }
            规则：
            1. suiteCode/methodCode 用英文小驼峰或下划线，全局唯一。
            2. 每个接口必须有 methodCode、methodName、url、requestType。
            3. 根据接口功能合理设计 inputParams（POST 多为 body、GET 多为 query）、headerParams（需要鉴权时生成 Authorization 等，无需鉴权的接口可为空数组）与 outputParams（响应字段），paramType 取 string/integer/double/boolean/object/array。
            4. 接口数量与需求匹配，不要遗漏也不要过度添加。
            """;
        var userPrompt = $"""
            {suitesSummary}
            接入需求：
            {requirement.Trim()}
            """;

        var content = await ChatAsync(systemPrompt, userPrompt, providerId, modelOverride: model);
        return ExtractAndValidateApis(content);
    }

    private static object ExtractAndValidateApis(string content)
    {
        var json = ExtractJson(content);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var suites = new List<object>();
        var apis = new List<object>();

        if (root.TryGetProperty("suites", out var sEl) && sEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in sEl.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object) continue;
                suites.Add(new
                {
                    suiteCode = GetStr(s, "suiteCode"),
                    suiteName = GetStr(s, "suiteName"),
                    suiteDesc = GetStr(s, "suiteDesc")
                });
            }
        }
        if (root.TryGetProperty("apis", out var aEl) && aEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in aEl.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var code = GetStr(a, "methodCode");
                if (string.IsNullOrEmpty(code)) continue;
                apis.Add(new
                {
                    suiteCode = GetStr(a, "suiteCode"),
                    methodCode = code,
                    methodName = GetStr(a, "methodName"),
                    methodDesc = GetStr(a, "methodDesc"),
                    url = GetStr(a, "url"),
                    requestType = GetStr(a, "requestType") == "GET" ? "GET" : "POST",
                    inputParams = ExtractParamArray(a, "inputParams"),
                    headerParams = ExtractParamArray(a, "headerParams"),
                    outputParams = ExtractParamArray(a, "outputParams")
                });
            }
        }
        if (apis.Count == 0)
            throw new Exception("AI 未生成有效接口，请调整需求描述后重试");
        return new { suites, apis };
    }

    /// <summary>确认接入：将生成的套件与接口写入数据库（按 code 去重，已存在跳过）。</summary>
    public async Task<object> ApplyApisAsync(List<Dictionary<string, object?>>? suites, List<Dictionary<string, object?>>? apis)
    {
        var createdSuites = 0;
        var createdApis = 0;

        foreach (var s in suites ?? new())
        {
            var code = s.GetValueOrDefault("suiteCode")?.ToString() ?? "";
            if (string.IsNullOrEmpty(code)) continue;
            var exists = await _db.Suites.AnyAsync(x => x.SuiteCode == code && x.Deleted == 0);
            if (exists) continue;
            _db.Suites.Add(new SuiteEntity
            {
                SuiteCode = code,
                SuiteName = s.GetValueOrDefault("suiteName")?.ToString() ?? code,
                SuiteDesc = s.GetValueOrDefault("suiteDesc")?.ToString() ?? "",
                CreatedAt = DateTime.Now.ToString("o")
            });
            createdSuites++;
        }
        await _db.SaveChangesAsync();

        var createdParams = 0;
        foreach (var a in apis ?? new())
        {
            var code = a.GetValueOrDefault("methodCode")?.ToString() ?? "";
            if (string.IsNullOrEmpty(code)) continue;
            var exists = await _db.Apis.AnyAsync(x => x.MethodCode == code && x.Deleted == 0);
            if (exists) continue;
            var apiEntity = new ApiEntity
            {
                SuiteCode   = a.GetValueOrDefault("suiteCode")?.ToString() ?? "",
                MethodCode  = code,
                MethodName  = a.GetValueOrDefault("methodName")?.ToString() ?? code,
                MethodDesc  = a.GetValueOrDefault("methodDesc")?.ToString() ?? "",
                Url         = a.GetValueOrDefault("url")?.ToString() ?? "",
                RequestType = a.GetValueOrDefault("requestType")?.ToString() ?? "POST",
                MethodType  = "HTTP",
                Status      = 1,
                CreatedAt   = DateTime.Now.ToString("o")
            };
            _db.Apis.Add(apiEntity);
            await _db.SaveChangesAsync();
            createdApis++;

            // 写入接口入参/header/出参（paramType: 1=入参 4=header 2=出参）
            createdParams += await SaveApiParamsAsync(apiEntity.Id, code, 1, ToParamList(a.GetValueOrDefault("inputParams")));
            createdParams += await SaveApiParamsAsync(apiEntity.Id, code, 4, ToParamList(a.GetValueOrDefault("headerParams")));
            createdParams += await SaveApiParamsAsync(apiEntity.Id, code, 2, ToParamList(a.GetValueOrDefault("outputParams")));
        }
        return new { createdSuites, createdApis, createdParams };
    }

    /// <summary>参数列表形态转换：兼容 Dictionary 列表与 JsonElement 数组（前端传回与模型输出的两种形态）。</summary>
    private static List<Dictionary<string, object?>>? ToParamList(object? v)
    {
        if (v == null) return null;
        if (v is List<Dictionary<string, object?>> dicts) return dicts;
        if (v is JsonElement el && el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.EnumerateObject().ToDictionary(p => p.Name, p => CloneToObject(p.Value)))
                .ToList();
        }
        if (v is System.Collections.IEnumerable en)
        {
            var list = new List<Dictionary<string, object?>>();
            foreach (var item in en)
            {
                if (item is Dictionary<string, object?> d) list.Add(d);
                else if (item is JsonElement je && je.ValueKind == JsonValueKind.Object)
                    list.Add(je.EnumerateObject().ToDictionary(p => p.Name, p => CloneToObject(p.Value)));
            }
            return list;
        }
        return null;
    }

    private async Task<int> SaveApiParamsAsync(long ownerId, string ownerCode, int paramType, List<Dictionary<string, object?>>? @params)
    {
        var count = 0;
        var sort = 0;
        foreach (var p in @params ?? new())
        {
            var code = p.GetValueOrDefault("paramCode")?.ToString() ?? "";
            if (string.IsNullOrEmpty(code)) continue;
            _db.Parameters.Add(new ParameterEntity
            {
                OwnerId       = ownerId,
                OwnerCode     = ownerCode,
                ParamType     = paramType,
                ParamCode     = code,
                ParamName     = p.GetValueOrDefault("paramName")?.ToString() ?? code,
                DataType      = p.GetValueOrDefault("paramType")?.ToString() ?? "string",
                Required      = p.GetValueOrDefault("required") switch { int r => r, long rl => (int)rl, _ => 0 },
                DefaultValue  = p.GetValueOrDefault("defaultValue")?.ToString(),
                Description   = p.GetValueOrDefault("description")?.ToString(),
                ParamPosition = p.GetValueOrDefault("paramPosition")?.ToString(),
                SortNum       = sort++,
                CreatedAt     = DateTime.Now.ToString("o")
            });
            count++;
        }
        if (count > 0) await _db.SaveChangesAsync();
        return count;
    }

    /// <summary>确认生成流程：创建流程定义、写入编排内容与流程入参/出参。</summary>
    public async Task<object> ApplyFlowAsync(string flowName, string? flowDesc, string? groupName,
        List<Dictionary<string, object?>>? nodes, List<Dictionary<string, object?>>? inputParams = null,
        List<Dictionary<string, object?>>? outputParams = null)
    {
        if (string.IsNullOrWhiteSpace(flowName)) throw new Exception("请填写流程名称");
        if (nodes == null || nodes.Count == 0) throw new Exception("没有可生成的流程节点");

        var entity = new Juggle.Domain.Entities.FlowDefinitionEntity
        {
            FlowKey     = $"flow_{Guid.NewGuid():N}",
            FlowName    = flowName.Trim(),
            FlowDesc    = flowDesc ?? "",
            FlowType    = "sync",
            GroupName   = groupName ?? "",
            FlowContent = JsonSerializer.Serialize(nodes),
            Status      = 0,
            CreatedAt   = DateTime.Now.ToString("o")
        };
        _db.FlowDefinitions.Add(entity);
        await _db.SaveChangesAsync();

        // 写入流程入参/出参（paramType: 5=入参 6=出参）
        var createdParams = 0;
        createdParams += await SaveParamsAsync(entity.Id, entity.FlowKey, 5, inputParams);
        createdParams += await SaveParamsAsync(entity.Id, entity.FlowKey, 6, outputParams);

        return new { entity.Id, entity.FlowKey, entity.FlowName, createdParams };
    }

    private async Task<int> SaveParamsAsync(long ownerId, string ownerCode, int paramType, List<Dictionary<string, object?>>? @params)
    {
        var count = 0;
        var sort = 0;
        foreach (var p in @params ?? new())
        {
            var code = p.GetValueOrDefault("paramCode")?.ToString() ?? "";
            if (string.IsNullOrEmpty(code)) continue;
            _db.Parameters.Add(new ParameterEntity
            {
                OwnerId       = ownerId,
                OwnerCode     = ownerCode,
                ParamType     = paramType,
                ParamCode     = code,
                ParamName     = p.GetValueOrDefault("paramName")?.ToString() ?? code,
                DataType      = p.GetValueOrDefault("paramType")?.ToString() ?? "string",
                Required      = p.GetValueOrDefault("required") switch { int r => r, long rl => (int)rl, _ => 0 },
                DefaultValue  = p.GetValueOrDefault("defaultValue")?.ToString(),
                Description   = p.GetValueOrDefault("description")?.ToString(),
                ParamPosition = p.GetValueOrDefault("paramPosition")?.ToString(),
                SortNum       = sort++,
                CreatedAt     = DateTime.Now.ToString("o")
            });
            count++;
        }
        if (count > 0) await _db.SaveChangesAsync();
        return count;
    }

    private static string GetStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ==================== 报表智能生成 ====================

    /// <summary>
    /// 根据需求生成报表（数据集 + 查询参数 + 排版），返回与报表设计器一致的 layoutJson 结构。
    /// </summary>
    public async Task<object> GenerateReportAsync(string requirement, string? reportName,
        List<Dictionary<string, object?>>? dataViews, List<Dictionary<string, object?>>? dataSources,
        List<Dictionary<string, object?>>? flows, List<Dictionary<string, object?>>? apis,
        long providerId = 0, string? model = null)
    {
        if (string.IsNullOrWhiteSpace(requirement))
            throw new Exception("请先描述报表需求");

        var systemPrompt = """
            你是报表设计助手。根据用户需求生成报表定义 JSON（与报表设计器 layoutJson 结构一致）。
            只输出 JSON（不要 markdown 围栏、不要解释），结构如下：
            {
              "page": { "size": "A4", "orientation": "portrait", "margin": {}, "header": "", "footer": "", "bgImage": "" },
              "params": [                                  // 查询参数（用户可筛选，与数据集 SQL 中 @参数名 对应）
                { "name": "beginDate", "label": "开始日期", "type": "date", "default": "" },
                { "name": "status", "label": "状态", "type": "select", "options": "paid,pending", "default": "" }
              ],
              "datasets": [                                // 数据集（尽量复用已有数据视图）
                {
                  "id": "ds1",
                  "name": "订单数据",
                  "sourceType": "dataview",               // dataview=复用数据视图 / sql=自定义SQL / flow=流程 / api=接口
                  "sourceRef": "12",                      // dataview=视图id；flow=flowKey；api=methodCode
                  "customSql": "",                        // sql 类型时填 SQL（用 @参数名 引用查询参数，参数为空时不过滤）
                  "dataSourceId": 3,                      // sql 类型时填数据源 id（必须来自可用数据源清单）
                  "fields": []
                }
              ],
              "rows": [
                { "height": 35, "type": "title", "dataset": "", "expand": "" },
                { "height": 28, "type": "header", "dataset": "", "expand": "" },
                { "height": 25, "type": "data", "dataset": "ds1", "expand": "auto", "zebra": true },
                { "height": 25, "type": "footer", "dataset": "ds1", "expand": "" }
              ],
              "cols": [ { "width": 60 }, { "width": 150 }, { "width": 100 } ],
              "cells": [
                { "r": 0, "c": 0, "value": "销售报表", "colspan": 3, "style": { "bold": true, "align": "center", "fontSize": 16 } },
                { "r": 1, "c": 0, "value": "编号", "style": { "bold": true } },
                { "r": 1, "c": 1, "value": "名称" },
                { "r": 1, "c": 2, "value": "金额" },
                { "r": 2, "c": 0, "value": "${rowIndex}" },
                { "r": 2, "c": 1, "value": "${ds1.name}" },   // 数据行占位符：${字段} 或 ${数据集.字段}
                { "r": 2, "c": 2, "value": "${amount}" },
                { "r": 3, "c": 0, "value": "合计", "colspan": 2 },
                { "r": 3, "c": 2, "value": "${amount:SUM}", "style": { "bold": true } }
              ]
            }

            规则：
            1. 数据集优先复用「可用数据视图」：sourceType=dataview、sourceRef=视图 id，不得编造；视图不满足时用 sourceType=sql + 可用数据源 id 写自定义 SQL（注意数据源方言：sqlite/mysql/postgresql/sqlserver）；需要流程/接口数据时用 flow/api 类型并复用清单中的 flowKey/methodCode。
            2. 数据集 SQL 中需要筛选的字段用 @参数名 引用 params 中的查询参数（如 WHERE status = @status），参数类型取 text/number/date/select（select 配 options 逗号分隔）。
            3. 行结构固定：第 1 行 title（合并全部列）、第 2 行 header（列名）、数据行 type=data + dataset + expand=auto + zebra=true（可用多行）、最后 footer 行。
            4. 数据行单元格用 ${字段名} 或 ${数据集.字段} 占位符；序号列用 ${rowIndex}。
            5. 汇总/合计用聚合占位符：${金额:SUM}、${字段:AVG}、${字段:COUNT}、MIN/MAX；需要条件计算时用公式 =IF(条件, 真, 假)（如 =IF(${amount} > 100, '大额', '普通')）。
            6. cols 宽度按内容合理分配（序号 60、文本 150、数字 100 左右）。
            7. 标题行居中加粗 16 号，表头加粗；列名用中文。
            8. 只输出 JSON 本体。
            """;

        var userPrompt = $"""
            ## 报表需求
            {requirement.Trim()}

            ## 可用数据视图（dataview）
            {BuildSimpleList(dataViews, "id", "name", "groupName", "sql")}

            ## 可用数据源（自定义SQL用）
            {BuildSimpleList(dataSources, "id", "dataSourceName", "dataSourceType", null)}

            ## 已发布流程（flow）
            {BuildSimpleList(flows, "flowKey", "flowName", null, null)}

            ## 可用接口（api）
            {BuildSimpleList(apis, "methodCode", "methodName", "suiteCode", "url")}

            请生成报表 JSON。
            """;

        var content = await ChatAsync(systemPrompt, userPrompt, providerId, modelOverride: model);
        return ExtractAndValidateReport(content, reportName);
    }

    /// <summary>提取报表查询参数（name/label/type/options/default）。</summary>
    private static List<Dictionary<string, object?>> ExtractReportParams(JsonElement root)
    {
        var list = new List<Dictionary<string, object?>>();
        if (!root.TryGetProperty("params", out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var name = GetStr(p, "name");
            if (string.IsNullOrEmpty(name)) continue;
            list.Add(new Dictionary<string, object?>
            {
                ["name"] = name,
                ["label"] = GetStr(p, "label") is { Length: > 0 } l ? l : name,
                ["type"] = GetStr(p, "type") is { Length: > 0 } t ? t : "text",
                ["options"] = GetStr(p, "options"),
                ["default"] = GetStr(p, "default")
            });
        }
        return list;
    }

    private static string BuildSimpleList(List<Dictionary<string, object?>>? items, string code, string name, string? extra, string? extra2)
    {
        if (items == null || items.Count == 0) return "（无）";
        var sb = new StringBuilder();
        foreach (var it in items)
        {
            sb.Append($"- {code}={it.GetValueOrDefault(code)} {name}={it.GetValueOrDefault(name)}");
            if (extra != null) sb.Append($" {extra}={it.GetValueOrDefault(extra)}");
            if (extra2 != null)
            {
                var v = it.GetValueOrDefault(extra2)?.ToString();
                if (!string.IsNullOrEmpty(v) && v.Length > 60) v = v[..60] + "...";
                sb.Append($" {extra2}={v}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>报表 JSON 校验归一化：结构完整 + 数据行展开标记补全。</summary>
    private static object ExtractAndValidateReport(string content, string? reportName)
    {
        var json = ExtractJson(content);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var layout = new Dictionary<string, object?>();

        // page
        if (root.TryGetProperty("page", out var pg) && pg.ValueKind == JsonValueKind.Object)
            layout["page"] = CloneToObject(pg);
        else
            layout["page"] = new Dictionary<string, object?> { ["size"] = "A4", ["orientation"] = "portrait" };

        // params（报表查询参数：name/label/type/options/default）
        layout["params"] = ExtractReportParams(root);
        var datasets = new List<object?>();
        if (root.TryGetProperty("datasets", out var dsArr) && dsArr.ValueKind == JsonValueKind.Array)
            datasets.AddRange(dsArr.EnumerateArray().Select(CloneToObject));
        layout["datasets"] = datasets;

        // rows：数据行绑定数据集时补 expand=auto
        var rows = new List<object?>();
        if (root.TryGetProperty("rows", out var rowsArr) && rowsArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in rowsArr.EnumerateArray())
            {
                if (r.ValueKind != JsonValueKind.Object) continue;
                var row = r.EnumerateObject().ToDictionary(p => p.Name, p => CloneToObject(p.Value));
                var type = row.GetValueOrDefault("type")?.ToString() ?? "";
                if (type == "data" && !string.IsNullOrEmpty(row.GetValueOrDefault("dataset")?.ToString())
                    && string.IsNullOrEmpty(row.GetValueOrDefault("expand")?.ToString()))
                    row["expand"] = "auto";
                rows.Add(row);
            }
        }
        layout["rows"] = rows;

        // cols / cells
        layout["cols"] = root.TryGetProperty("cols", out var colsArr) && colsArr.ValueKind == JsonValueKind.Array
            ? colsArr.EnumerateArray().Select(CloneToObject).ToList()
            : new List<object?>();
        layout["cells"] = root.TryGetProperty("cells", out var cellsArr) && cellsArr.ValueKind == JsonValueKind.Array
            ? cellsArr.EnumerateArray().Select(CloneToObject).ToList()
            : new List<object?>();

        if (rows.Count == 0 || ((List<object?>)layout["cols"]!).Count == 0)
            throw new Exception("AI 返回的报表缺少行/列定义，请调整需求重试");

        var layoutJson = JsonSerializer.Serialize(layout);
        return new
        {
            reportName = reportName ?? "",
            layoutJson,
            paramsList = layout["params"],
            datasets = datasets,
            rows = rows.Count,
            cols = ((List<object?>)layout["cols"]!).Count
        };
    }

    // ==================== 自定义助手 ====================

    /// <summary>
    /// 运行自定义助手：系统提示词 + 输入参数（+补充说明）→ 大模型。
    /// 配置了输出参数时要求模型返回 JSON 并按参数名解析，解析失败保留原文。
    /// </summary>
    public async Task<object> RunAssistantAsync(AiAssistantEntity assistant, Dictionary<string, object?>? inputs,
        string? extraText, long providerId = 0, string? model = null)
    {
        var inputParams = ParseParamList(assistant.InputParams);
        var outputParams = ParseParamList(assistant.OutputParams);

        var sb = new StringBuilder();
        if (inputParams.Count > 0)
        {
            sb.AppendLine("## 输入参数");
            foreach (var p in inputParams)
            {
                var name = p.GetValueOrDefault("name")?.ToString() ?? "";
                var label = p.GetValueOrDefault("label")?.ToString() ?? name;
                sb.AppendLine($"- {label}：{inputs?.GetValueOrDefault(name) ?? ""}");
            }
        }
        if (!string.IsNullOrWhiteSpace(extraText))
        {
            sb.AppendLine("## 补充说明");
            sb.AppendLine(extraText.Trim());
        }
        if (outputParams.Count > 0)
        {
            var names = string.Join(", ", outputParams.Select(p => $"\"{p.GetValueOrDefault("name")}\""));
            var descs = string.Join("、", outputParams.Select(p =>
                $"\"{p.GetValueOrDefault("name")}\" 表示{p.GetValueOrDefault("label") ?? p.GetValueOrDefault("name")}"));
            sb.AppendLine("## 输出要求");
            sb.AppendLine($"请只输出 JSON（不要解释、不要 markdown 围栏），字段：{names}，其中 {descs}。");
        }

        var content = await ChatAsync(assistant.SystemPrompt ?? "你是一个智能助手。", sb.ToString(),
            providerId > 0 ? providerId : assistant.ProviderId,
            modelOverride: string.IsNullOrWhiteSpace(model) ? assistant.Model : model,
            temperature: assistant.Temperature, maxTokens: assistant.MaxTokens,
            seed: assistant.Seed, enableThinking: assistant.EnableThinking == 1);

        var outputs = new Dictionary<string, object?>();
        if (outputParams.Count > 0)
        {
            try
            {
                var json = ExtractJson(content);
                using var doc = JsonDocument.Parse(json);
                foreach (var p in outputParams)
                {
                    var name = p.GetValueOrDefault("name")?.ToString() ?? "";
                    if (name.Length > 0 && doc.RootElement.TryGetProperty(name, out var v))
                        outputs[name] = CloneToObject(v);
                }
            }
            catch { /* 模型未按 JSON 输出时保留原文 */ }
        }
        return new { reply = content, outputs };
    }

    private static List<Dictionary<string, object?>> ParseParamList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<Dictionary<string, object?>>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<Dictionary<string, object?>>();
            return doc.RootElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.EnumerateObject().ToDictionary(p => p.Name, p => CloneToObject(p.Value)))
                .ToList();
        }
        catch { return new List<Dictionary<string, object?>>(); }
    }

    // ==================== 多轮对话会话 ====================

    /// <summary>开启新对话：快照助手配置，输入参数并入系统上下文。</summary>
    public async Task<AiConversationEntity> StartConversationAsync(AiAssistantEntity assistant,
        Dictionary<string, object?>? inputs, long providerId, string? model)
    {
        var conv = new AiConversationEntity
        {
            AssistantId    = assistant.Id,
            AssistantName  = assistant.AssistantName,
            SystemPrompt   = assistant.SystemPrompt,
            InputParams    = assistant.InputParams,
            OutputParams   = assistant.OutputParams,
            ProviderId     = providerId > 0 ? providerId : assistant.ProviderId,
            Model          = string.IsNullOrWhiteSpace(model) ? assistant.Model : model,
            Temperature    = assistant.Temperature,
            MaxTokens      = assistant.MaxTokens,
            Seed           = assistant.Seed,
            EnableThinking = assistant.EnableThinking,
            Messages       = "[]",
            Status         = 0,
            CreatedAt      = DateTime.Now.ToString("o")
        };
        // 输入参数并入系统上下文（每轮对话可见）
        var inputParams = ParseParamList(assistant.InputParams);
        if (inputParams.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 输入参数");
            foreach (var p in inputParams)
            {
                var name  = p.GetValueOrDefault("name")?.ToString() ?? "";
                var label = p.GetValueOrDefault("label")?.ToString() ?? name;
                sb.AppendLine($"- {label}：{inputs?.GetValueOrDefault(name) ?? ""}");
            }
            conv.SystemPrompt = (conv.SystemPrompt ?? "") + "\n" + sb;
        }
        _db.AiConversations.Add(conv);
        await _db.SaveChangesAsync();
        return conv;
    }

    /// <summary>多轮对话：用户消息入历史，携带完整历史调用模型，回复回写历史。</summary>
    public async Task<string> ConversationChatAsync(AiConversationEntity conv, string userContent, long providerId = 0, string? model = null)
    {
        var history = ParseMessages(conv.Messages);
        history.Add(("user", userContent));
        var messages = new List<(string, string)> { ("system", conv.SystemPrompt ?? "") };
        messages.AddRange(history);
        var reply = await ChatMessagesAsync(messages, providerId, null, model,
            conv.Temperature, conv.MaxTokens, conv.Seed, conv.EnableThinking == 1);
        history.Add(("assistant", reply));
        conv.Messages = JsonSerializer.Serialize(history.Select(m => new { role = m.Role, content = m.Content }));
        if (string.IsNullOrEmpty(conv.Title))
            conv.Title = userContent.Length > 30 ? userContent[..30] : userContent;
        conv.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return reply;
    }

    /// <summary>结束对话：有输出参数时要求模型基于对话生成最终 JSON 结果并解析，会话标记已结束。</summary>
    public async Task<object> EndConversationAsync(AiConversationEntity conv, long providerId = 0, string? model = null)
    {
        var outputParams = ParseParamList(conv.OutputParams);
        var finalReply = "";
        var outputs = new Dictionary<string, object?>();
        if (outputParams.Count > 0)
        {
            var names = string.Join(", ", outputParams.Select(p => $"\"{p.GetValueOrDefault("name")}\""));
            var descs = string.Join("、", outputParams.Select(p =>
                $"\"{p.GetValueOrDefault("name")}\" 表示{p.GetValueOrDefault("label") ?? p.GetValueOrDefault("name")}"));
            var messages = new List<(string, string)> { ("system", conv.SystemPrompt ?? "") };
            messages.AddRange(ParseMessages(conv.Messages));
            messages.Add(("user", $"请根据以上对话生成最终结果，只输出 JSON（不要解释、不要 markdown 围栏），字段：{names}，其中 {descs}。"));
            finalReply = await ChatMessagesAsync(messages, providerId, null, model,
                conv.Temperature, conv.MaxTokens, conv.Seed, conv.EnableThinking == 1);
            try
            {
                var json = ExtractJson(finalReply);
                using var doc = JsonDocument.Parse(json);
                foreach (var p in outputParams)
                {
                    var name = p.GetValueOrDefault("name")?.ToString() ?? "";
                    if (name.Length > 0 && doc.RootElement.TryGetProperty(name, out var v))
                        outputs[name] = CloneToObject(v);
                }
            }
            catch { /* 模型未按 JSON 输出时保留原文 */ }
        }
        conv.Status = 1;
        conv.Outputs = JsonSerializer.Serialize(outputs);
        conv.UpdatedAt = DateTime.Now.ToString("o");
        await _db.SaveChangesAsync();
        return new { reply = finalReply, outputs };
    }

    private static List<(string Role, string Content)> ParseMessages(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<(string, string)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var list = new List<(string, string)>();
            foreach (var m in doc.RootElement.EnumerateArray())
            {
                var role = m.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
                var content = m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                if (role.Length > 0) list.Add((role, content));
            }
            return list;
        }
        catch { return new List<(string, string)>(); }
    }

    /// <summary>流程生成的系统提示词：描述与设计器一致的节点格式。</summary>
    private static string BuildSystemPrompt()
    {
        return """
            你是接口编排平台的智能编排助手。根据用户需求与可用接口清单，生成接口流程编排 JSON。

            输出 JSON 结构（nodes 数组，节点间通过 outgoings 数组连线；inputParams/outputParams 为流程入参/出参定义）：
            {
              "nodes": [ ... ],
              "inputParams": [
                { "paramCode": "userId", "paramName": "用户id", "paramType": "string", "required": 1, "description": "用户唯一标识" }
              ],
              "outputParams": [
                { "paramCode": "userName", "paramName": "用户名称", "paramType": "string", "required": 0, "description": "返回的用户名称" }
              ]
            }

            nodes 结构：
            [
              {
                "key": "n1",                       // 唯一节点 key（n1、n2、n3...）
                "elementType": "START",            // 节点类型
                "label": "开始",
                "x": 100, "y": 100,                // 画布坐标
                "outgoings": ["n2"]                // 下一节点 key 列表
              },
              {
                "key": "n2",
                "elementType": "METHOD",           // 调用接口
                "label": "获取用户信息",
                "x": 320, "y": 100,
                "method": {
                  "suiteCode": "user",             // 接口所属套件 code（必须来自可用接口清单）
                  "methodCode": "getUserInfo",     // 接口 code（必须来自可用接口清单）
                  "url": "/api/user/info",
                  "method": "POST"
                },
                "inputFillRules": [                // 入参填充：来源 → 接口入参
                  { "sourceType": "INPUT", "source": "userId", "target": "userId" }
                ],
                "outputFillRules": [               // 输出映射：接口响应字段 → 变量
                  { "source": "data.name", "targetType": "VARIABLE", "target": "env_user_name" }
                ],
                "outgoings": ["n3"]
              },
              {
                "key": "n3",
                "elementType": "CONDITION",        // 条件分支
                "label": "判断",
                "x": 540, "y": 100,
                "conditions": [
                  { "conditionName": "分支1", "conditionType": "CUSTOM", "expression": "env_user_name == '张三'", "outgoing": "n4" },
                  { "conditionName": "默认", "conditionType": "DEFAULT", "outgoing": "n5" }
                ]
              },
              {
                "key": "n4",
                "elementType": "END",
                "label": "结束",
                "x": 760, "y": 40,
                "outgoings": []
              }
            ]

            规则：
            1. elementType 只能使用：START / END / METHOD / CONDITION / MERGE / ASSIGN / CODE / MYSQL / LOOP / DELAY / PARALLEL / NOTIFY。
            2. 每个流程必须有且只有一个 START 和一个 END；节点 key 从 n1 开始递增，连线必须能从头走到 END。
            3. METHOD 节点的 suiteCode 与 methodCode 必须严格取自可用接口清单，不得编造；method.url 与 method.method 与清单一致。
            4. 入参来源 sourceType 用 INPUT（流程入参，对应 inputParams 中的 paramCode）或 CONSTANT（常量）或 VARIABLE（前面节点产出的 env_xxx 变量）；target 填接口真实入参名。
            5. 输出映射 source 填接口响应 JSON 字段路径，targetType 用 VARIABLE 且 target 以 env_ 开头（如 env_user_name），或 OUTPUT 对应流程出参。
            6. inputParams 必须覆盖所有 METHOD 节点引用的 INPUT 来源；outputParams 必须覆盖所有 targetType=OUTPUT 的映射；paramCode 用英文小驼峰，paramType 取 string/integer/double/boolean/object/array。
            7. 需要分支判断时用 CONDITION 节点（conditions 数组，含一个 DEFAULT 分支）；多分支汇聚用 MERGE 节点。
            8. 涉及数据库操作时用 MYSQL 节点：{ "elementType":"MYSQL", "mysqlConfig": { "dataSourceName":"数据源名", "sql":"SELECT ...", "operationType":"QUERY", "outputVariable":"env_xxx", "outputTargetType":"VARIABLE" } }，SQL 参数用 ${变量}。
            9. 坐标 x 从 100 开始、每层 +220，y 从 100 开始、分支 +150，保证画布整齐不重叠。
            10. 只输出 JSON 本体，不要 markdown 代码块，不要解释。
            """;
    }

    private static string BuildApiSummary(List<Dictionary<string, object?>>? apis)
    {
        if (apis == null || apis.Count == 0) return "（无可用接口，请使用 MYSQL/CODE 等节点实现）";
        var sb = new StringBuilder();
        foreach (var a in apis)
        {
            sb.AppendLine($"- 套件[{a.GetValueOrDefault("suiteCode")}] 接口[{a.GetValueOrDefault("methodCode")}] {a.GetValueOrDefault("methodName")}：{a.GetValueOrDefault("method")} {a.GetValueOrDefault("url")}（{a.GetValueOrDefault("methodDesc")}）");
        }
        return sb.ToString();
    }

    private static string BuildParamsSummary(List<Dictionary<string, object?>>? @params)
    {
        if (@params == null || @params.Count == 0) return "（无）";
        var sb = new StringBuilder();
        foreach (var p in @params)
            sb.AppendLine($"- {p.GetValueOrDefault("code")}（{p.GetValueOrDefault("name")}）");
        return sb.ToString();
    }

    /// <summary>从模型输出中提取 JSON 并校验归一化（容错：去掉代码块围栏/多余文字）。</summary>
    private static object ExtractAndValidateFlow(string content)
    {
        var json = ExtractJson(content);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("nodes", out var nodesEl) || nodesEl.ValueKind != JsonValueKind.Array)
            throw new Exception("AI 返回的流程缺少 nodes 数组，请重试");

        var result = new List<JsonElement>();
        foreach (var node in nodesEl.EnumerateArray())
            result.Add(node);

        // 提取流程入参/出参定义
        var inputParams = ExtractParamArray(root, "inputParams");
        var outputParams = ExtractParamArray(root, "outputParams");

        // 归一化：保证有 START 与 END、节点 key 唯一、坐标兜底
        var normalized = (Dictionary<string, object?>)NormalizeFlow(result);
        normalized["inputParams"] = inputParams;
        normalized["outputParams"] = outputParams;
        return normalized;
    }

    /// <summary>提取参数数组（paramCode/paramName/paramType/required/description），兼容缺省。</summary>
    private static List<Dictionary<string, object?>> ExtractParamArray(JsonElement root, string prop)
    {
        var list = new List<Dictionary<string, object?>>();
        if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object) continue;
            var code = GetStr(p, "paramCode");
            if (string.IsNullOrEmpty(code)) continue;
            list.Add(new Dictionary<string, object?>
            {
                ["paramCode"] = code,
                ["paramName"] = GetStr(p, "paramName") is { Length: > 0 } n ? n : code,
                ["paramType"] = GetStr(p, "paramType") is { Length: > 0 } t ? t : "string",
                ["required"] = p.TryGetProperty("required", out var re) && re.ValueKind == JsonValueKind.Number ? re.GetInt32() : 0,
                ["description"] = GetStr(p, "description"),
                ["paramPosition"] = GetStr(p, "paramPosition")
            });
        }
        return list;
    }

    private static string ExtractJson(string content)
    {
        var text = content.Trim();
        // 去掉 ```json ... ``` 围栏
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }
        // 截取首个 { 到最后一个 }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new Exception("AI 返回内容不是合法 JSON，请重试");
        return text[start..(end + 1)];
    }

    /// <summary>节点归一化：key 唯一化、坐标兜底、outgoings/conditions 结构补全。</summary>
    private static Dictionary<string, object?> NormalizeFlow(List<JsonElement> nodes)
    {
        var seenKeys = new HashSet<string>();
        var outputs = new List<Dictionary<string, object?>>();
        int idx = 0;

        foreach (var node in nodes)
        {
            if (node.ValueKind != JsonValueKind.Object) continue;
            var item = new Dictionary<string, object?>();

            // key 唯一化
            var key = node.TryGetProperty("key", out var kEl) && kEl.ValueKind == JsonValueKind.String
                ? kEl.GetString()! : "";
            if (string.IsNullOrEmpty(key) || !seenKeys.Add(key))
            {
                key = $"n{idx + 1}";
                while (!seenKeys.Add(key)) key = $"n{++idx + 1}";
            }
            item["key"] = key;

            var elementType = node.TryGetProperty("elementType", out var etEl) && etEl.ValueKind == JsonValueKind.String
                ? etEl.GetString()!.ToUpper() : "METHOD";
            item["elementType"] = elementType;
            item["label"] = node.TryGetProperty("label", out var lbEl) && lbEl.ValueKind == JsonValueKind.String
                ? lbEl.GetString()! : elementType;

            // 坐标兜底（每层 +220 横向，同层 +150 纵向）
            var x = node.TryGetProperty("x", out var xEl) && xEl.ValueKind == JsonValueKind.Number ? xEl.GetDouble() : 100 + (idx % 4) * 220;
            var y = node.TryGetProperty("y", out var yEl) && yEl.ValueKind == JsonValueKind.Number ? yEl.GetDouble() : 100 + (idx / 4) * 150;
            item["x"] = x; item["y"] = y;

            // 各类型配置透传
            foreach (var prop in new[] { "method", "conditions", "inputFillRules", "outputFillRules", "headerFillRules", "mockJson", "mysqlConfig", "assignRules", "codeConfig", "subFlowConfig", "loopConfig", "delayConfig", "parallelConfig", "notifyConfig", "transformConfig", "timeout", "retryCount" })
            {
                if (node.TryGetProperty(prop, out var pEl)) item[prop] = CloneToObject(pEl);
            }

            // outgoings 补全
            if (node.TryGetProperty("outgoings", out var ogEl) && ogEl.ValueKind == JsonValueKind.Array)
            {
                item["outgoings"] = ogEl.EnumerateArray().Select(e => (object?)e.GetString()).Where(s => s != null).ToList();
            }
            else
            {
                item["outgoings"] = new List<object?>();
            }

            outputs.Add(item);
            idx++;
        }

        // 没有 START / END 时补一个
        if (!outputs.Any(o => string.Equals(o["elementType"]?.ToString(), "START", StringComparison.OrdinalIgnoreCase)))
        {
            var startKey = $"n{outputs.Count + 1}";
            outputs.Insert(0, new Dictionary<string, object?>
            {
                ["key"] = startKey, ["elementType"] = "START", ["label"] = "开始",
                ["x"] = 100d, ["y"] = 100d,
                ["outgoings"] = outputs.Count > 0 ? new List<object?> { outputs[0]["key"] } : new List<object?>()
            });
        }
        if (!outputs.Any(o => string.Equals(o["elementType"]?.ToString(), "END", StringComparison.OrdinalIgnoreCase)))
        {
            var endKey = $"n{outputs.Count + 1}";
            var last = outputs.Last();
            if (last["outgoings"] is List<object?> list && list.Count == 0 && last["elementType"]?.ToString() != "END")
                list.Add(endKey);
            outputs.Add(new Dictionary<string, object?>
            {
                ["key"] = endKey, ["elementType"] = "END", ["label"] = "结束",
                ["x"] = 100 + (outputs.Count % 4) * 220, ["y"] = 100 + (outputs.Count / 4) * 150,
                ["outgoings"] = new List<object?>()
            });
        }

        return new Dictionary<string, object?>
        {
            ["nodes"] = outputs,
            ["inputParams"] = new List<Dictionary<string, object?>>(),
            ["outputParams"] = new List<Dictionary<string, object?>>()
        };
    }

    /// <summary>JsonElement → 可序列化对象（Dictionary / List / 原生值）。</summary>
    private static object? CloneToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => CloneToObject(p.Value)),
            JsonValueKind.Array => el.EnumerateArray().Select(CloneToObject).ToList(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

/// <summary>AI 模型配置</summary>
public class AiConfig
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "deepseek-chat";
}
