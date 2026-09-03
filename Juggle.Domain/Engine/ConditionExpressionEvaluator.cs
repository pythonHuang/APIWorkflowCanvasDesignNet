using System.Text.Json;

namespace Juggle.Domain.Engine;

/// <summary>
/// 条件表达式求值器 — 递归下降解析。
/// 语法支持：
///   逻辑运算:  &&（且） ||（或） !（非） 括号改变优先级
///   比较运算:  == != > < >= <=
///   操作数:    字面量（数字 / '字符串' / "字符串" / true / false / null）
///              或变量路径（左右两边都可以是变量）
///   变量路径:  env_user.name（子属性）、input_list.length（数组长度）、
///              input_list[0].name（数组指定元素及其子属性，索引从 0 开始）
///   无比较符时按真值判断: 裸变量 / !变量 / 括号整体
/// </summary>
public class ConditionExpressionEvaluator
{
    private readonly string _expr;
    private int _pos;
    private readonly FlowContext _context;

    private ConditionExpressionEvaluator(string expression, FlowContext context)
    {
        _expr = expression;
        _context = context;
    }

    /// <summary>求值入口。解析失败或变量缺失时返回 false。</summary>
    public static bool Evaluate(string expression, FlowContext context)
    {
        if (string.IsNullOrWhiteSpace(expression)) return false;
        try
        {
            var parser = new ConditionExpressionEvaluator(expression, context);
            var val = parser.ParseOr();
            parser.SkipWhitespace();
            // 存在无法识别的残余字符视为表达式错误
            if (parser._pos < parser._expr.Length) return false;
            return IsTruthy(val);
        }
        catch
        {
            return false;
        }
    }

    // ===== 递归下降解析 =====

    private object? ParseOr()
    {
        var left = ParseAnd();
        SkipWhitespace();
        while (Peek() == '|' && Peek(1) == '|')
        {
            _pos += 2;
            var right = ParseAnd();
            left = IsTruthy(left) ? left : right;   // 短路
        }
        return left;
    }

    private object? ParseAnd()
    {
        var left = ParseUnary();
        SkipWhitespace();
        while (Peek() == '&' && Peek(1) == '&')
        {
            _pos += 2;
            var right = ParseUnary();
            left = IsTruthy(left) ? right : left;   // 短路
        }
        return left;
    }

    private object? ParseUnary()
    {
        SkipWhitespace();
        if (Peek() == '!')
        {
            _pos++;
            return !IsTruthy(ParseUnary());
        }
        return ParseCompare();
    }

    private object? ParseCompare()
    {
        var left = ParseOperand();
        SkipWhitespace();

        string? op = null;
        if (Peek() == '=' && Peek(1) == '=') { _pos += 2; op = "=="; }
        else if (Peek() == '!' && Peek(1) == '=') { _pos += 2; op = "!="; }
        else if (Peek() == '>' && Peek(1) == '=') { _pos += 2; op = ">="; }
        else if (Peek() == '<' && Peek(1) == '=') { _pos += 2; op = "<="; }
        else if (Peek() == '>') { _pos++; op = ">"; }
        else if (Peek() == '<') { _pos++; op = "<"; }
        else return left;   // 无比较符，按真值判断

        var right = ParseOperand();
        return Compare(left, right, op);
    }

    private object? ParseOperand()
    {
        SkipWhitespace();
        if (_pos >= _expr.Length) throw new InvalidOperationException("表达式不完整");

        var c = _expr[_pos];

        // 数字字面量
        if (char.IsDigit(c) || (c == '.' && _pos + 1 < _expr.Length && char.IsDigit(_expr[_pos + 1])))
        {
            var start = _pos;
            while (_pos < _expr.Length && (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.')) _pos++;
            var numStr = _expr[start.._pos];
            if (long.TryParse(numStr, out var l)) return l;
            if (double.TryParse(numStr, out var d)) return d;
            return null;
        }

        // 字符串字面量
        if (c == '\'' || c == '"')
        {
            var quote = _expr[_pos++];
            var start = _pos;
            while (_pos < _expr.Length && _expr[_pos] != quote) _pos++;
            var str = _expr[start.._pos];
            if (_pos < _expr.Length) _pos++;
            return str;
        }

        // 括号
        if (c == '(')
        {
            _pos++;
            var val = ParseOr();
            SkipWhitespace();
            if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
            else throw new InvalidOperationException("括号不匹配");
            return val;
        }

        // 变量路径
        var root = ParseIdentifier();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException($"无法识别的字符 '{c}'");

        return ResolvePath(root);
    }

    // ===== 变量路径解析 =====

    private object? ResolvePath(string root)
    {
        object? current = _context.GetVariable(root);

        while (true)
        {
            SkipWhitespace();

            // 数组索引 [n]
            if (_pos < _expr.Length && _expr[_pos] == '[')
            {
                _pos++;
                SkipWhitespace();
                var numStart = _pos;
                while (_pos < _expr.Length && char.IsDigit(_expr[_pos])) _pos++;
                if (numStart == _pos) throw new InvalidOperationException("数组索引必须为数字");
                var idx = int.Parse(_expr[numStart.._pos]);
                SkipWhitespace();
                if (_pos < _expr.Length && _expr[_pos] == ']') _pos++;
                current = GetArrayElement(current, idx);
                continue;
            }

            // 子属性 .prop / .length
            if (_pos < _expr.Length && _expr[_pos] == '.')
            {
                _pos++;
                SkipWhitespace();
                var prop = ParseIdentifier();
                if (string.IsNullOrEmpty(prop)) throw new InvalidOperationException("缺少属性名");
                current = prop.Equals("length", StringComparison.OrdinalIgnoreCase)
                    ? GetArrayLength(current)
                    : GetProperty(current, prop);
                continue;
            }

            break;
        }

        return current;
    }

    private static object? GetProperty(object? obj, string prop)
    {
        switch (obj)
        {
            case Dictionary<string, object?> dict:
                return dict.TryGetValue(prop, out var dv) ? dv : null;
            case JsonElement json when json.ValueKind == JsonValueKind.Object:
                return json.TryGetProperty(prop, out var next) ? ToNative(next) : null;
            default:
                return null;
        }
    }

    private static object? GetArrayElement(object? obj, int index)
    {
        switch (obj)
        {
            case List<object?> list:
                return index >= 0 && index < list.Count ? list[index] : null;
            case object[] arr:
                return index >= 0 && index < arr.Length ? arr[index] : null;
            case JsonElement json when json.ValueKind == JsonValueKind.Array:
            {
                var i = 0;
                foreach (var item in json.EnumerateArray())
                {
                    if (i++ == index) return ToNative(item);
                }
                return null;
            }
            default:
                return null;
        }
    }

    private static object? GetArrayLength(object? obj)
    {
        switch (obj)
        {
            case List<object?> list: return (long)list.Count;
            case object[] arr: return (long)arr.Length;
            case JsonElement json when json.ValueKind == JsonValueKind.Array: return (long)json.GetArrayLength();
            default: return null;
        }
    }

    /// <summary>将 JsonElement 转为 .NET 原生类型，便于比较运算</summary>
    private static object? ToNative(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el   // 对象/数组保留原 JsonElement，继续支持 .prop / [i] / .length
        };
    }

    // ===== 比较与真值 =====

    private static object Compare(object? left, object? right, string op)
    {
        // null 比较
        if (left == null || right == null)
            return op switch
            {
                "==" => left == null && right == null,
                "!=" => !(left == null && right == null),
                _ => false
            };

        // 布尔比较（布尔值或 "true"/"false" 字符串）
        if (TryAsBool(left, out var lb) && TryAsBool(right, out var rb))
            return op switch { "==" => lb == rb, "!=" => lb != rb, _ => false };

        // 数字比较（任意一边可解析为数字时按数值比较）
        if (IsNumeric(left) && IsNumeric(right))
        {
            var l = ToDouble(left);
            var r = ToDouble(right);
            return op switch
            {
                "==" => l == r,
                "!=" => l != r,
                ">" => l > r,
                "<" => l < r,
                ">=" => l >= r,
                "<=" => l <= r,
                _ => false
            };
        }

        // 字符串比较（==/!= 忽略大小写，保持历史行为）
        var ls = left.ToString() ?? "";
        var rs = right.ToString() ?? "";
        return op switch
        {
            "==" => string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
            ">" => string.Compare(ls, rs, StringComparison.Ordinal) > 0,
            "<" => string.Compare(ls, rs, StringComparison.Ordinal) < 0,
            ">=" => string.Compare(ls, rs, StringComparison.Ordinal) >= 0,
            "<=" => string.Compare(ls, rs, StringComparison.Ordinal) <= 0,
            _ => false
        };
    }

    private static bool IsTruthy(object? v)
    {
        switch (v)
        {
            case null: return false;
            case bool b: return b;
            case string s: return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
            case long l: return l != 0;
            case int i: return i != 0;
            case double d: return d != 0;
            case decimal m: return m != 0;
            default: return false;
        }
    }

    private static bool TryAsBool(object? v, out bool result)
    {
        if (v is bool b) { result = b; return true; }
        if (v is string s && bool.TryParse(s, out var parsed)) { result = parsed; return true; }
        result = false;
        return false;
    }

    private static bool IsNumeric(object? v)
    {
        return v switch
        {
            byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal => true,
            string s => double.TryParse(s, out _),
            _ => false
        };
    }

    private static double ToDouble(object? v)
    {
        return v switch
        {
            byte x => x, sbyte x => x, short x => x, ushort x => x,
            int x => x, uint x => x, long x => x, ulong x => x,
            float x => x, double x => x, decimal x => (double)x,
            string s => double.TryParse(s, out var d) ? d : 0,
            _ => 0
        };
    }

    // ===== 基础解析工具 =====

    private string ParseIdentifier()
    {
        var start = _pos;
        while (_pos < _expr.Length && (char.IsLetterOrDigit(_expr[_pos]) || _expr[_pos] == '_'))
            _pos++;
        return _expr[start.._pos];
    }

    private char Peek(int ahead = 0) => _pos + ahead < _expr.Length ? _expr[_pos + ahead] : '\0';

    private void SkipWhitespace()
    {
        while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos])) _pos++;
    }
}
