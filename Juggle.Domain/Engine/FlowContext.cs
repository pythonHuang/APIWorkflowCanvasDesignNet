namespace Juggle.Domain.Engine;

/// <summary>流程运行时上下文，持有变量容器 + 日志收集 + 静态变量引用</summary>
public class FlowContext
{
    public string FlowKey { get; set; } = "";
    public string Version { get; set; } = "";

    /// <summary>流程运行时变量（input_ / output_ / 中间变量）</summary>
    public Dictionary<string, object?> Variables { get; set; } = new();

    /// <summary>静态全局变量（VarCode → Value，流程内可读写，执行结束后写回 DB）</summary>
    public Dictionary<string, string?> StaticVariables { get; set; } = new();

    /// <summary>静态变量是否被修改（需要写回 DB）</summary>
    public HashSet<string> ModifiedStaticVarCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>节点执行日志列表（按执行顺序收集）</summary>
    public List<NodeLogEntry> NodeLogs { get; set; } = new();

    // ========== 流程变量操作 ==========

    public void SetVariable(string key, object? value)
    {
        Variables[key] = value;
    }

    public object? GetVariable(string key)
    {
        return Variables.TryGetValue(key, out var val) ? val : null;
    }
    
    /// <summary>设置输出参数值</summary>
    public void SetOutputParameter(string paramCode, object? value)
    {
        // 输出参数也存储在 Variables 中，但以 output_ 前缀区分
        Variables[$"output_{paramCode}"] = value;
    }
    
    /// <summary>获取输出参数值</summary>
    public object? GetOutputParameter(string paramCode)
    {
        return Variables.TryGetValue($"output_{paramCode}", out var val) ? val : null;
    }

    // ========== 静态变量操作 ==========

    /// <summary>读取静态变量（返回字符串原始值）</summary>
    public string? GetStaticVariable(string code)
    {
        return StaticVariables.TryGetValue(code, out var val) ? val : null;
    }

    /// <summary>写入静态变量（标记为已修改，执行结束后持久化）</summary>
    public void SetStaticVariable(string code, string? value)
    {
        StaticVariables[code] = value;
        ModifiedStaticVarCodes.Add(code);
    }

    // ========== 嵌套属性导航 ==========

    /// <summary>从变量中按路径取嵌套属性值。支持 JsonElement 和 Dictionary 类型。</summary>
    public object? GetNestedProperty(string variableCode, string path)
    {
        var obj = GetVariable(variableCode);
        if (obj == null || string.IsNullOrEmpty(path)) return obj;
        return NavigatePath(obj, path);
    }

    /// <summary>向变量的嵌套属性路径写入值。仅支持 Dictionary 类型（会原地修改）。</summary>
    public void SetNestedProperty(string variableCode, string path, object? value)
    {
        var container = GetVariable(variableCode);
        if (container is Dictionary<string, object?> dict)
        {
            SetByPath(dict, path, value);
        }
        // 如果容器为空或不是 Dictionary，创建一个新的 Dictionary
        else if (container == null)
        {
            var newDict = new Dictionary<string, object?>();
            SetByPath(newDict, path, value);
            SetVariable(variableCode, newDict);
        }
    }

    private static object? NavigatePath(object? obj, string path)
    {
        var parts = path.Split('.');
        foreach (var part in parts)
        {
            if (obj == null) return null;
            if (obj is System.Text.Json.JsonElement jsonEl && jsonEl.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                if (jsonEl.TryGetProperty(part, out var next))
                    obj = next;
                else
                    return null;
            }
            else if (obj is Dictionary<string, object?> dict)
            {
                if (dict.TryGetValue(part, out var next))
                    obj = next;
                else
                    return null;
            }
            else
            {
                return null;
            }
        }
        // 将 JsonElement 转为 .NET 原生类型
        if (obj is System.Text.Json.JsonElement finalEl)
        {
            return finalEl.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => finalEl.GetString(),
                System.Text.Json.JsonValueKind.Number => finalEl.TryGetInt64(out var l) ? l : finalEl.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Null => null,
                _ => obj
            };
        }
        return obj;
    }

    private static void SetByPath(Dictionary<string, object?> dict, string path, object? value)
    {
        var parts = path.Split('.');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (!dict.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nextDict)
            {
                nextDict = new Dictionary<string, object?>();
                dict[parts[i]] = nextDict;
            }
            dict = nextDict;
        }
        dict[parts[^1]] = value;
    }

    // ========== 节点日志操作 ==========

    /// <summary>添加一条简单的运行日志（非节点日志，用于通知等场景）</summary>
    public void AddLog(string message)
    {
        NodeLogs.Add(new NodeLogEntry
        {
            SeqNo = NodeLogs.Count + 1,
            NodeKey = "_system",
            NodeLabel = "系统日志",
            NodeType = "SYSTEM",
            StartTime = DateTime.Now,
            EndTime = DateTime.Now,
            Status = "SUCCESS",
            Detail = message
        });
    }

    public NodeLogEntry BeginNodeLog(string nodeKey, string nodeLabel, string nodeType)
    {
        var entry = new NodeLogEntry
        {
            SeqNo = NodeLogs.Count + 1,
            NodeKey = nodeKey,
            NodeLabel = nodeLabel,
            NodeType = nodeType,
            StartTime = DateTime.Now,
            Status = "RUNNING"
        };
        NodeLogs.Add(entry);
        return entry;
    }
}

/// <summary>节点执行日志条目（内存中收集，最后批量写 DB）</summary>
public class NodeLogEntry
{
    public int SeqNo { get; set; }
    public string NodeKey { get; set; } = "";
    public string NodeLabel { get; set; } = "";
    public string NodeType { get; set; } = "";
    public string Status { get; set; } = "RUNNING";  // RUNNING / SUCCESS / FAILED
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public long CostMs => EndTime.HasValue ? (long)(EndTime.Value - StartTime).TotalMilliseconds : 0;
    public string? InputSnapshot { get; set; }
    public string? OutputSnapshot { get; set; }
    public string? Detail { get; set; }
    public string? ErrorMessage { get; set; }

    public void Complete(string status = "SUCCESS", string? errorMsg = null, string? detail = null)
    {
        EndTime = DateTime.Now;
        Status = status;
        ErrorMessage = errorMsg;
        if (detail != null) Detail = detail;
    }
}

/// <summary>流程执行结果</summary>
public class FlowResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object?> OutputData { get; set; } = new();
    public string? InstanceId { get; set; }   // 异步流程实例ID
    public long? LogId { get; set; }           // 日志记录ID（方便前端查日志）
    public long CostMs { get; set; }           // 总耗时（毫秒）
    /// <summary>执行完的上下文，含节点日志 + 静态变量修改记录（供持久化使用，不序列化到响应）</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public FlowContext? Context { get; set; }
}
