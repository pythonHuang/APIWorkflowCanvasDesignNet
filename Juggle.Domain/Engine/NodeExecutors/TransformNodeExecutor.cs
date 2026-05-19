using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Juggle.Domain.Engine.NodeExecutors;

/// <summary>
/// 模板转换节点执行器
/// 将模板文本中的 ${varName} 占位符替换为变量/参数值，支持管道转换，结果赋值到目标
/// 语法: ${varName.sub.path | transform1 | transform2(arg)}
///   - 管道分隔，按顺序调用
///   - transform 含 '.' 则调用静态方法（如 Math.Parse）
///   - transform 不含 '.' 则调用实例方法（如 ToFix(2)）
/// </summary>
public class TransformNodeExecutor : INodeExecutor
{
    public Task<string?> ExecuteAsync(FlowNode node, FlowContext context)
    {
        var config = node.TransformConfig ?? throw new InvalidOperationException("TransformConfig 未配置");
        if (string.IsNullOrEmpty(config.Template))
            return Task.FromResult(node.Outgoings.FirstOrDefault());

        var result = ProcessTemplate(config.Template, context);
        ApplyResult(config.TargetType, config.TargetCode, result, context);

        return Task.FromResult(node.Outgoings.FirstOrDefault());
    }

    /// <summary>处理模板，替换 ${...} 占位符</summary>
    internal static string ProcessTemplate(string template, FlowContext context)
    {
        // 匹配 ${...} 占位符（支持嵌套花括号内的内容直到匹配的 }）
        return Regex.Replace(template, @"\$\{([^}]+)\}", match =>
        {
            var expr = match.Groups[1].Value.Trim();
            var parts = expr.Split('|');
            var path = parts[0].Trim();

            // 读取变量值
            var value = GetVariableValue(path, context);

            // 应用管道转换（跳过第一个 path 元素）
            for (int i = 1; i < parts.Length; i++)
            {
                var transform = parts[i].Trim();
                if (string.IsNullOrEmpty(transform)) continue;
                value = ApplyTransform(value, transform);
            }

            return value?.ToString() ?? "";
        });
    }

    /// <summary>从上下文中读取变量值，支持点路径（如 userInfo.name）</summary>
    private static object? GetVariableValue(string path, FlowContext context)
    {
        // 尝试从上下文变量中查找（逐级匹配点路径）
        var segments = path.Split('.');
        var rootKey = segments[0];

        object? value = null;
        if (context.Variables.TryGetValue(rootKey, out var varValue))
            value = varValue;
        else if (context.StaticVariables.TryGetValue(rootKey, out var staticVal))
            value = staticVal;

        // 如果有多级路径，逐级解析
        if (value != null && segments.Length > 1)
        {
            var current = value;
            for (int i = 1; i < segments.Length; i++)
            {
                current = AccessProperty(current, segments[i]);
                if (current == null) return null;
            }
            return current;
        }

        return value;
    }

    /// <summary>访问对象属性，支持 JSON 元素访问</summary>
    private static object? AccessProperty(object? obj, string prop)
    {
        if (obj == null) return null;

        // 先尝试用 JsonElement 方式（处理从 JSON 反序列化的对象）
        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(prop, out var v))
                return JsonElementToObject(v);
            if (je.ValueKind == JsonValueKind.Array && int.TryParse(prop, out var idx))
            {
                var items = je.EnumerateArray().ToList();
                if (idx >= 0 && idx < items.Count)
                    return JsonElementToObject(items[idx]);
            }
            return null;
        }

        // 泛型反射访问
        var type = obj.GetType();
        var propInfo = type.GetProperty(prop,
            System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (propInfo != null)
            return propInfo.GetValue(obj);

        // 尝试字典访问
        if (obj is IDictionary<string, object?> dict && dict.TryGetValue(prop, out var dictVal))
            return dictVal;

        return null;
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? (object)l : (object)el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.Object => el,
            _ => el.GetRawText()
        };
    }

    /// <summary>应用单个管道转换</summary>
    private static object? ApplyTransform(object? value, string transform)
    {
        if (value == null) return null;

        // 解析转换名和参数：ToFix(2) → method=ToFix, args=[2]
        var parenIdx = transform.IndexOf('(');
        string methodName;
        string[] args;

        if (parenIdx > 0 && transform.EndsWith(")"))
        {
            methodName = transform[..parenIdx].Trim();
            var argsStr = transform[(parenIdx + 1)..^1].Trim();
            args = string.IsNullOrEmpty(argsStr) ? Array.Empty<string>()
                : argsStr.Split(',').Select(a => a.Trim().Trim('\'').Trim('"')).ToArray();
        }
        else
        {
            methodName = transform.Trim();
            args = Array.Empty<string>();
        }

        // 含 '.' 的为静态调用
        if (methodName.Contains('.'))
        {
            return ApplyStaticTransform(value, methodName, args);
        }

        // 不含 '.' 的为实例方法
        return ApplyInstanceTransform(value, methodName, args);
    }

    private static object? ApplyStaticTransform(object? value, string fullName, string[] args)
    {
        var lastDot = fullName.LastIndexOf('.');
        var typeName = fullName[..lastDot];
        var methodName = fullName[(lastDot + 1)..];

        try
        {
            switch (typeName.ToLowerInvariant())
            {
                case "math":
                    var num = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return methodName.ToLowerInvariant() switch
                    {
                        "parse" => num,
                        "floor" => Math.Floor(num),
                        "ceil" => Math.Ceiling(num),
                        "round" => Math.Round(num),
                        "abs" => Math.Abs(num),
                        _ => value
                    };
                case "json":
                    var str = value?.ToString() ?? "";
                    return methodName.ToLowerInvariant() switch
                    {
                        "parse" => JsonSerializer.Deserialize<object>(str),
                        "stringify" => JsonSerializer.Serialize(value),
                        _ => value
                    };
                case "convert":
                    return methodName.ToLowerInvariant() switch
                    {
                        "tostring" => value?.ToString(),
                        "toint" => Convert.ToInt32(value, CultureInfo.InvariantCulture),
                        "todouble" => Convert.ToDouble(value, CultureInfo.InvariantCulture),
                        "toboolean" => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
                        _ => value
                    };
                case "string" or "system.string":
                    return methodName.ToLowerInvariant() switch
                    {
                        "format" => args.Length > 0 ? string.Format(value?.ToString() ?? "", args) : value,
                        _ => value
                    };
                default:
                    return value;
            }
        }
        catch
        {
            return value;
        }
    }

    private static object? ApplyInstanceTransform(object? value, string methodName, string[] args)
    {
        try
        {
            var str = value?.ToString() ?? "";

            switch (methodName.ToLowerInvariant())
            {
                case "toupper":
                    return str.ToUpper();
                case "tolower":
                    return str.ToLower();
                case "trim":
                    return str.Trim();
                case "trimstart":
                    return str.TrimStart();
                case "trimend":
                    return str.TrimEnd();
                case "substring":
                    if (args.Length >= 1 && int.TryParse(args[0], out var start))
                    {
                        var len = args.Length >= 2 && int.TryParse(args[1], out var l) ? l : str.Length - start;
                        return str.Substring(Math.Min(start, str.Length), Math.Min(len, str.Length - start));
                    }
                    return str;
                case "replace":
                    if (args.Length >= 2)
                        return str.Replace(args[0], args[1]);
                    return str;
                case "split":
                    if (args.Length >= 1)
                        return str.Split(args[0]);
                    return str;
                case "tofix":
                    if (value is double d || double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
                    {
                        var dec = args.Length >= 1 && int.TryParse(args[0], out var n) ? n : 2;
                        return d.ToString($"F{dec}");
                    }
                    return str;
                case "parseint":
                    return int.TryParse(str, out var i) ? i : 0;
                case "parsedouble":
                    return double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var db) ? db : 0.0;
                case "parsebool":
                    return bool.TryParse(str, out var b) ? b : false;
                case "tostring":
                    return str;
                default:
                    return value;
            }
        }
        catch
        {
            return value;
        }
    }

    /// <summary>将模板结果赋值到目标</summary>
    private static void ApplyResult(string targetType, string targetCode, string result, FlowContext context)
    {
        if (string.IsNullOrEmpty(targetCode)) return;

        switch (targetType.ToUpperInvariant())
        {
            case "VARIABLE":
                context.SetVariable(targetCode, result);
                break;
            case "STATIC":
                context.StaticVariables[targetCode] = result;
                context.ModifiedStaticVarCodes.Add(targetCode);
                break;
            case "INPUT":
                // 入参：写入 input_ 前缀变量
                context.SetVariable(targetCode.StartsWith("input_") ? targetCode : "input_" + targetCode, result);
                break;
            case "OUTPUT":
                // 出参：写入 output_ 前缀变量
                context.SetVariable(targetCode.StartsWith("output_") ? targetCode : "output_" + targetCode, result);
                break;
        }
    }
}
