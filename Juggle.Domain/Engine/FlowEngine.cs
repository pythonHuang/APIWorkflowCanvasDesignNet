using System.Text.Json;
using Juggle.Domain.Engine.NodeExecutors;

namespace Juggle.Domain.Engine;

/// <summary>
/// 流程执行引擎：解析流程 JSON，按节点拓扑顺序执行，维护变量上下文
/// 支持节点类型：START / END / METHOD / CONDITION / ASSIGN / CODE / MYSQL(DB) / MERGE / SUB_FLOW / LOOP / DELAY / PARALLEL / NOTIFY / TRANSFORM
/// 支持功能：节点执行日志收集、静态全局变量读写、子流程递归调用
/// </summary>
public class FlowEngine
{
    private readonly IHttpClientFactory _httpClientFactory;
    /// <summary>数据源名称 → DataSourceInfo 映射</summary>
    private readonly Dictionary<string, DataSourceInfo> _dataSources;
    /// <summary>静态变量初始值（VarCode → Value）</summary>
    private readonly Dictionary<string, string?> _staticVarSnapshot;
    /// <summary>根据 flowKey 加载最新已发布流程内容（供 SUB_FLOW 节点调用），可为 null（不支持子流程）</summary>
    private readonly Func<string, Task<string?>>? _flowContentLoader;
    /// <summary>大模型对话函数（systemPrompt, userInput → reply），供 AI 节点调用，可为 null（未接入大模型）</summary>
    private readonly Func<string, string, Task<string>>? _aiChatFunc;
    /// <summary>Redis 连接串（供 REDIS_GET/REDIS_SET 节点使用），可为 null（未配置 Redis）</summary>
    private readonly string? _redisConnStr;

    public FlowEngine(IHttpClientFactory httpClientFactory,
                      Dictionary<string, DataSourceInfo>? dataSources = null,
                      Dictionary<string, string?>? staticVariables = null,
                      Func<string, Task<string?>>? flowContentLoader = null,
                      Func<string, string, Task<string>>? aiChatFunc = null,
                      string? redisConnStr = null)
    {
        _httpClientFactory  = httpClientFactory;
        _dataSources        = dataSources ?? new();
        _staticVarSnapshot  = staticVariables ?? new(StringComparer.OrdinalIgnoreCase);
        _flowContentLoader  = flowContentLoader;
        _aiChatFunc         = aiChatFunc;
        _redisConnStr       = redisConnStr;
    }

    public async Task<FlowResult> ExecuteAsync(
        string flowContent,
        Dictionary<string, object?> inputParams,
        string flowKey = "",
        string version = "")
    {
        var result = new FlowResult { Success = true };
        var startTime = DateTime.Now;

        // 1. 解析流程节点
        List<FlowNode>? nodes;
        try
        {
            nodes = JsonSerializer.Deserialize<List<FlowNode>>(flowContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            return new FlowResult { Success = false, ErrorMessage = $"流程内容解析失败: {ex.Message}" };
        }

        if (nodes == null || nodes.Count == 0)
            return new FlowResult { Success = false, ErrorMessage = "流程内容为空" };

        // 2. 建立节点索引
        var nodeMap = nodes.ToDictionary(n => n.Key, n => n);

        // 3. 初始化上下文，写入输入变量 + 静态变量
        var context = new FlowContext { FlowKey = flowKey, Version = version };
        foreach (var kv in inputParams)
            context.SetVariable(kv.Key, kv.Value);
        // 注入静态变量（只读副本，修改时标记 ModifiedStaticVarCodes）
        foreach (var kv in _staticVarSnapshot)
            context.StaticVariables[kv.Key] = kv.Value;

        // 4. 找到 START 节点
        var startNode = nodes.FirstOrDefault(n => n.ElementType == "START");
        if (startNode == null)
            return new FlowResult { Success = false, ErrorMessage = "找不到 START 节点" };

        // 5. 按拓扑顺序执行（支持 CONDITION 多分支 + MERGE 聚合）
        try
        {
            await ExecuteFromNode(startNode.Key, nodeMap, context);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        // 6. 收集所有输出变量（以 output_ 开头的变量）
        foreach (var kv in context.Variables)
        {
            if (kv.Key.StartsWith("output_"))
                result.OutputData[kv.Key] = kv.Value;
        }

        // 7. 挂载上下文和耗时到结果（供外层持久化日志）
        result.Context = context;
        result.CostMs = (long)(DateTime.Now - startTime).TotalMilliseconds;

        return result;
    }

    /// <summary>从指定节点开始执行，遇到 MERGE 或 END 时停止并返回下一节点 Key</summary>
    private async Task<string?> ExecuteFromNode(
        string startKey,
        Dictionary<string, FlowNode> nodeMap,
        FlowContext context,
        HashSet<string>? visitedMerge = null)
    {
        var currentKey = startKey;
        var visited = new HashSet<string>();
        const int maxSteps = 1000;
        var steps = 0;

        while (!string.IsNullOrEmpty(currentKey) && steps++ < maxSteps)
        {
            if (!nodeMap.TryGetValue(currentKey, out var currentNode))
                break;

            // MERGE 节点：等待所有分支汇聚，返回 merge 之后的节点
            if (currentNode.ElementType == "MERGE")
                return currentKey;

            // END 节点：执行后结束
            if (currentNode.ElementType == "END")
            {
                var endLog = context.BeginNodeLog(currentNode.Key, currentNode.Label ?? "END", "END");
                endLog.InputSnapshot = SnapshotVariables(context.Variables);
                await new EndNodeExecutor().ExecuteAsync(currentNode, context);
                endLog.Complete("SUCCESS");
                endLog.OutputSnapshot = endLog.InputSnapshot;
                return null;
            }

            // 检测死循环（CONDITION 节点不计入 visited）
            if (currentNode.ElementType != "CONDITION" && !visited.Add(currentKey))
                throw new InvalidOperationException($"检测到死循环，节点: {currentKey}");

            INodeExecutor executor = currentNode.ElementType switch
            {
                "START"         => new StartNodeExecutor(),
                "METHOD"        => new MethodNodeExecutor(_httpClientFactory),
                "ASSIGN"        => new AssignNodeExecutor(),
                "CODE"          => new CodeNodeExecutor(),
                "MYSQL" or "DB" => new MysqlNodeExecutor(_dataSources),
                "CONDITION"     => new ConditionNodeExecutor(),
                "SUB_FLOW"      => new SubFlowNodeExecutor(
                    _flowContentLoader ?? throw new InvalidOperationException("当前引擎未配置 flowContentLoader，无法执行 SUB_FLOW 节点"),
                    _httpClientFactory, _dataSources, _staticVarSnapshot),
                "LOOP"          => new LoopNodeExecutor(),
                "DELAY" or "WAIT" => new DelayNodeExecutor(),
                "PARALLEL"      => new ParallelNodeExecutor(),
                "NOTIFY"        => new NotifyNodeExecutor(_httpClientFactory),
                "TRANSFORM"     => new TransformNodeExecutor(),
                "AI"            => new AiNodeExecutor(
                    _aiChatFunc ?? throw new InvalidOperationException("流程引擎未接入大模型，无法执行 AI 节点")),
                "FILE_PARSE"    => new FileParseNodeExecutor(),
                "EXCEL_READ"    => new ExcelReadNodeExecutor(),
                "FILE_WRITE"    => new FileWriteNodeExecutor(),
                "REDIS_GET"     => new RedisGetNodeExecutor(
                    _redisConnStr ?? throw new InvalidOperationException("未配置 Redis（系统设置 → Redis 配置），无法执行 Redis 查询节点")),
                "REDIS_SET"     => new RedisSetNodeExecutor(
                    _redisConnStr ?? throw new InvalidOperationException("未配置 Redis（系统设置 → Redis 配置），无法执行 Redis 设置节点")),
                _ => throw new InvalidOperationException($"未知节点类型: {currentNode.ElementType}")
            };

            if (currentNode.ElementType == "CONDITION")
            {
                // CONDITION：选出要走的分支 key
                var condLog = context.BeginNodeLog(currentNode.Key, currentNode.Label ?? "CONDITION", "CONDITION");
                condLog.InputSnapshot = SnapshotVariables(context.Variables);

                string? branchKey;
                try
                {
                    branchKey = await executor.ExecuteAsync(currentNode, context);
                    condLog.Complete("SUCCESS", detail: $"走分支: {branchKey}");
                    condLog.OutputSnapshot = SnapshotVariables(context.Variables);
                }
                catch (Exception ex)
                {
                    condLog.Complete("FAILED", errorMsg: ex.Message);
                    throw;
                }

                if (string.IsNullOrEmpty(branchKey)) break;

                // 查找是否有 MERGE 节点
                var mergeKey = FindMergeNode(currentNode, nodeMap);

                if (mergeKey != null && nodeMap.ContainsKey(mergeKey))
                {
                    await ExecuteFromNode(branchKey, nodeMap, context);
                    var mergeNode = nodeMap[mergeKey];
                    currentKey = mergeNode.Outgoings.FirstOrDefault() ?? "";
                }
                else
                {
                    currentKey = branchKey;
                }
                continue;
            }

            // PARALLEL：并行执行所有 outgoings 分支
            if (currentNode.ElementType == "PARALLEL")
            {
                var paraLog = context.BeginNodeLog(currentNode.Key, currentNode.Label ?? "PARALLEL", "PARALLEL");
                paraLog.InputSnapshot = SnapshotVariables(context.Variables);

                var waitMode = currentNode.ParallelConfig?.WaitMode ?? "ALL_WAIT";
                var paraTimeout = currentNode.ParallelConfig?.Timeout ?? 0;
                var branchKeys = currentNode.Outgoings.Where(k => !string.IsNullOrEmpty(k)).ToList();

                if (branchKeys.Count == 0)
                {
                    paraLog.Complete("SUCCESS", detail: "无分支可执行");
                    break;
                }

                try
                {
                    if (waitMode == "ANY_FAST")
                    {
                        // ANY_FAST：任一分支完成即继续
                        var branchTasks = branchKeys.Select(k => ExecuteBranchAsync(k, nodeMap, context, paraLog)).ToList();

                        if (paraTimeout > 0)
                        {
                            var completedTask = await System.Threading.Tasks.Task.WhenAny(
                                System.Threading.Tasks.Task.WhenAll(branchTasks),
                                System.Threading.Tasks.Task.Delay(paraTimeout));
                            if (completedTask != System.Threading.Tasks.Task.WhenAll(branchTasks))
                            {
                                paraLog.Complete("SUCCESS", detail: $"ANY_FAST超时({paraTimeout}ms)，已完成部分分支");
                                currentKey = currentNode.Outgoings.FirstOrDefault() ?? "";
                                continue;
                            }
                        }

                        await System.Threading.Tasks.Task.WhenAll(branchTasks);
                        paraLog.Complete("SUCCESS", detail: $"ANY_FAST: {branchKeys.Count}个分支并行完成");
                    }
                    else
                    {
                        // ALL_WAIT：等待所有分支完成
                        var branchTasks = branchKeys.Select(k => ExecuteBranchAsync(k, nodeMap, context, paraLog)).ToList();

                        if (paraTimeout > 0)
                        {
                            var completedTask = await System.Threading.Tasks.Task.WhenAny(
                                System.Threading.Tasks.Task.WhenAll(branchTasks),
                                System.Threading.Tasks.Task.Delay(paraTimeout));
                            if (completedTask != System.Threading.Tasks.Task.WhenAll(branchTasks))
                            {
                                throw new TimeoutException($"PARALLEL node [{currentNode.Key}] 超时({paraTimeout}ms)，部分分支未完成");
                            }
                        }

                        await System.Threading.Tasks.Task.WhenAll(branchTasks);
                        paraLog.Complete("SUCCESS", detail: $"ALL_WAIT: {branchKeys.Count}个分支并行完成");
                    }

                    paraLog.OutputSnapshot = SnapshotVariables(context.Variables);

                    // PARALLEL 之后继续执行（走到下一个节点或结束）
                    // 如果有 MERGE 节点则用它汇聚，否则直接走 PARALLEL 的 outgoing
                    var mergeKey = FindMergeNode(currentNode, nodeMap);
                    if (mergeKey != null && nodeMap.ContainsKey(mergeKey))
                    {
                        currentKey = nodeMap[mergeKey].Outgoings.FirstOrDefault() ?? "";
                    }
                    else
                    {
                        // 无 MERGE，所有分支完成后执行 PARALLEL 节点之后的节点
                        // 对于 PARALLEL，我们需要查找所有分支最终汇聚的节点
                        var finalNode = FindParallelEndNode(branchKeys, nodeMap, new HashSet<string>());
                        currentKey = finalNode ?? "";
                    }
                }
                catch (Exception ex)
                {
                    paraLog.Complete("FAILED", errorMsg: ex.Message);
                    throw;
                }
                continue;
            }

            // 普通节点执行（带日志 + 超时 + 重试）
            var nodeLog = context.BeginNodeLog(currentNode.Key, currentNode.Label ?? currentNode.ElementType, currentNode.ElementType);
            nodeLog.InputSnapshot = SnapshotVariables(context.Variables);

            string? nextKey = null;
            var retryCount = currentNode.RetryCount > 0 ? currentNode.RetryCount : 0;
            var retryInterval = currentNode.RetryInterval > 0 ? currentNode.RetryInterval : 1000;
            var timeout = currentNode.Timeout > 0 ? currentNode.Timeout : 0;
            var attempts = 0;

            while (attempts <= retryCount)
            {
                try
                {
                    if (timeout > 0)
                    {
                        // 带超时执行
                        var cts = new System.Threading.CancellationTokenSource(timeout);
                        var task = executor.ExecuteAsync(currentNode, context);
                        var completedTask = await System.Threading.Tasks.Task.WhenAny(task, System.Threading.Tasks.Task.Delay(timeout, cts.Token));
                        if (completedTask != task)
                        {
                            throw new TimeoutException($"节点 {currentNode.Key} 执行超时（{timeout}ms）");
                        }
                        nextKey = await task;
                    }
                    else
                    {
                        nextKey = await executor.ExecuteAsync(currentNode, context);
                    }
                    nodeLog.Complete("SUCCESS");
                    nodeLog.OutputSnapshot = SnapshotVariables(context.Variables);
                    break;
                }
                catch (Exception ex)
                {
                    attempts++;
                    if (attempts <= retryCount)
                    {
                        nodeLog.Detail = $"第{attempts}次执行失败，{retryInterval}ms后重试: {ex.Message}";
                        await System.Threading.Tasks.Task.Delay(retryInterval);
                    }
                    else
                    {
                        nodeLog.Complete("FAILED", errorMsg: ex.Message);
                        throw;
                    }
                }
            }

            if (nextKey == null) break;
            currentKey = nextKey;
        }

        return null;
    }

    /// <summary>序列化变量快照（过滤空值，防止 JSON 过大）</summary>
    private static string SnapshotVariables(Dictionary<string, object?> vars)
    {
        try
        {
            return JsonSerializer.Serialize(vars, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
        }
        catch
        {
            return "{}";
        }
    }

    /// <summary>在 CONDITION 节点的所有分支中，找到共同的 MERGE 节点</summary>
    private static string? FindMergeNode(FlowNode conditionNode, Dictionary<string, FlowNode> nodeMap)
    {
        if (conditionNode.Conditions == null || conditionNode.Conditions.Count == 0)
            return null;

        foreach (var cond in conditionNode.Conditions)
        {
            if (string.IsNullOrEmpty(cond.Outgoing)) continue;
            var mergeKey = TraverseFindMerge(cond.Outgoing, nodeMap, new HashSet<string>());
            if (mergeKey != null) return mergeKey;
        }
        return null;
    }

    private static string? TraverseFindMerge(string nodeKey, Dictionary<string, FlowNode> nodeMap, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(nodeKey) || !visited.Add(nodeKey)) return null;
        if (!nodeMap.TryGetValue(nodeKey, out var node)) return null;
        if (node.ElementType == "MERGE") return nodeKey;
        foreach (var next in node.Outgoings)
        {
            var found = TraverseFindMerge(next, nodeMap, visited);
            if (found != null) return found;
        }
        if (node.Conditions != null)
        {
            foreach (var cond in node.Conditions)
            {
                if (!string.IsNullOrEmpty(cond.Outgoing))
                {
                    var found = TraverseFindMerge(cond.Outgoing, nodeMap, visited);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    /// <summary>执行一个并行分支（从起始节点到 MERGE/END）</summary>
    private async Task ExecuteBranchAsync(
        string branchStartKey,
        Dictionary<string, FlowNode> nodeMap,
        FlowContext context,
        NodeLogEntry paraLog)
    {
        try
        {
            await ExecuteFromNode(branchStartKey, nodeMap, context);
        }
        catch (Exception ex)
        {
            paraLog.Detail = (paraLog.Detail ?? "") + $"\n分支 [{branchStartKey}] 失败: {ex.Message}";
        }
    }

    /// <summary>查找并行分支最终的汇聚节点（所有分支最终到达的同一个节点）</summary>
    private static string? FindParallelEndNode(List<string> branchKeys, Dictionary<string, FlowNode> nodeMap, HashSet<string> visited)
    {
        // 简化实现：取第一个分支的终点
        // 对于 PARALLEL 后接 MERGE 的场景，FindMergeNode 已经处理
        // 这里处理无 MERGE 的情况，直接找分支链末尾的下一个节点
        if (branchKeys.Count == 0) return null;

        var endKeys = new HashSet<string>();
        foreach (var bk in branchKeys)
        {
            var end = FindBranchEnd(bk, nodeMap, new HashSet<string>());
            if (end != null) endKeys.Add(end);
        }

        // 如果所有分支汇聚到同一个节点，返回它
        if (endKeys.Count == 1) return endKeys.First();
        return null;
    }

    private static string? FindBranchEnd(string nodeKey, Dictionary<string, FlowNode> nodeMap, HashSet<string> visited)
    {
        if (string.IsNullOrEmpty(nodeKey) || !visited.Add(nodeKey)) return null;
        if (!nodeMap.TryGetValue(nodeKey, out var node)) return null;
        if (node.ElementType == "END" || node.ElementType == "MERGE") return nodeKey;
        if (node.ElementType == "CONDITION")
        {
            // CONDITION 有多分支，取第一个
            if (node.Conditions != null && node.Conditions.Count > 0)
            {
                var firstCond = node.Conditions.FirstOrDefault(c => !string.IsNullOrEmpty(c.Outgoing));
                if (firstCond != null) return FindBranchEnd(firstCond.Outgoing!, nodeMap, visited);
            }
            return nodeKey;
        }
        if (node.Outgoings.Count > 0) return FindBranchEnd(node.Outgoings[0], nodeMap, visited);
        return nodeKey;
    }
}
