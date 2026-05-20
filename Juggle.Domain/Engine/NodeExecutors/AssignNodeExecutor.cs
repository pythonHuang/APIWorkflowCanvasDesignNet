using System.Text.Json;

namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// ASSIGN 赋值节点执行器：根据赋值规则将常量或变量值写入目标变量
/// sourceType 支持：
///   CONSTANT        — 常量值
///   VARIABLE        — 流程变量
///   STATIC          — 全局静态变量
///   INPUT           — 流程入参
///   SUB_PROPERTY    — 对象的子属性
///   ARRAY_OPERATION — 数组操作（分页/取第n个/插入/转JSON）
/// targetType 支持：
///   VARIABLE        — 流程变量
///   STATIC          — 写入全局静态变量（执行后持久化）
///   OUTPUT          — 写入输出参数（流程执行结果）
///   INPUT           — 写入流程入参
///   SUB_PROPERTY    — 写入对象的子属性
///   ARRAY_OPERATION — 数组操作（分页/取第n个/转JSON）
/// </summary>
public class AssignNodeExecutor : INodeExecutor
{
    public Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        if (node.AssignRules != null)
        {
            foreach (var rule in node.AssignRules)
            {
                // ==== 读取来源值 ====
                object? value;
                var srcType = (rule.SourceType ?? "VARIABLE").ToUpper();
                switch (srcType)
                {
                    case "CONSTANT":
                        value = ParseConstant(rule.Source, rule.DataType);
                        break;
                    case "STATIC":
                        value = context.GetStaticVariable(rule.Source);
                        break;
                    case "SUB_PROPERTY":
                        value = context.GetNestedProperty(rule.Source, rule.SourcePath);
                        break;
                    case "ARRAY_OPERATION":
                        value = ApplyArrayOperation(context.GetVariable(rule.Source), rule.ArrayOpType, rule.ArrayOpPageNum, rule.ArrayOpPageSize, rule.ArrayOpIndex);
                        break;
                    default:
                        // VARIABLE / INPUT
                        value = context.GetVariable(rule.Source);
                        break;
                }

                // ==== 写入目标 ====
                var tgtType = (rule.TargetType ?? "VARIABLE").ToUpper();
                switch (tgtType)
                {
                    case "STATIC":
                        context.SetStaticVariable(rule.Target, value?.ToString());
                        break;
                    case "OUTPUT":
                        context.SetOutputParameter(rule.Target, value);
                        break;
                    case "SUB_PROPERTY":
                        context.SetNestedProperty(rule.Target, rule.TargetPath, value);
                        break;
                    case "ARRAY_OPERATION":
                        var tgtResult = ApplyArrayOperation(context.GetVariable(rule.Target), rule.ArrayOpType, rule.ArrayOpPageNum, rule.ArrayOpPageSize, rule.ArrayOpIndex);
                        context.SetVariable(rule.Target, tgtResult);
                        break;
                    default:
                        // VARIABLE / INPUT 等
                        context.SetVariable(rule.Target, value);
                        break;
                }
            }
        }

        return Task.FromResult(node.Outgoings.FirstOrDefault());
    }

    /// <summary>对数组执行操作并返回结果</summary>
    private static object? ApplyArrayOperation(object? source, string? opType, string? pageNum, string? pageSize, string? index)
    {
        if (source == null) return null;

        // 尝试将 source 转为 List
        List<object?>? list = null;
        if (source is JsonElement jEl && jEl.ValueKind == JsonValueKind.Array)
        {
            list = jEl.EnumerateArray().Select(e => (object?)JsonElementToObject(e)).ToList();
        }
        else if (source is List<object?> l)
        {
            list = l;
        }
        else if (source is System.Collections.IEnumerable enumerable and not string)
        {
            list = enumerable.Cast<object?>().ToList();
        }

        if (list == null) return source;

        var op = (opType ?? "").ToUpper();
        switch (op)
        {
            case "PAGINATE":
                int.TryParse(pageNum, out var pn);
                int.TryParse(pageSize, out var ps);
                if (ps <= 0) ps = 10;
                var skip = pn * ps;
                return list.Skip(skip).Take(ps).ToList();

            case "GET_INDEX":
                int.TryParse(index, out var idx);
                if (idx >= 0 && idx < list.Count)
                    return list[idx];
                return null;

            case "TO_JSON":
                return JsonSerializer.Serialize(list);

            default:
                return list;
        }
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => el.EnumerateObject().ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            _ => el.ToString()
        };
    }

    private static object? ParseConstant(string source, string? dataType)
    {
        if (string.IsNullOrEmpty(source)) return null;
        return (dataType?.ToLower()) switch
        {
            "integer" or "int" => int.TryParse(source, out var i) ? i : (object?)source,
            "double" or "float" or "decimal" => double.TryParse(source, out var d) ? d : (object?)source,
            "boolean" or "bool" => bool.TryParse(source, out var b) ? b : (object?)source,
            _ => source
        };
    }
}
