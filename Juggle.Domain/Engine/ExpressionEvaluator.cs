using System.Globalization;
using System.Text.Json;

namespace Juggle.Domain.Engine;

/// <summary>
/// 表达式求值器 — 递归下降解析。条件节点（布尔判断）和赋值节点（EXPRESSION 来源）共用。
///
/// 语法：
///   逻辑:      &amp;&amp; || ! 括号
///   比较:      == != > < >= <=
///   算术:      + - * / % （+ 两边都是数字时做加法，否则做字符串拼接；* 会把数字字符串转为数字）
///   字面量:    数字 / '字符串' / "字符串" / true / false / null
///   变量路径:  env_user.name（子属性）、input_list.length（数组/字符串长度）、
///              input_list[0].name（数组元素及其子属性，索引从 0 开始）
///   字符串操作: + 拼接、[..5] / [2..5] / [2..] 切片截取（含数组切片）、
///              replace('旧','新') 替换、[0] 取单个字符
///   格式化:     .toString("#.0##") 数字格式、.toString("yyyy-MM-dd HH:mm:ss") 日期格式
///   未声明的标识符（非变量）按字符串字面量处理，兼容旧表达式如 env_status == paid
/// </summary>
public class ExpressionEvaluator
{
    private readonly string _expr;
    private int _pos;
    private readonly FlowContext _context;

    private ExpressionEvaluator(string expression, FlowContext context)
    {
        _expr = expression;
        _context = context;
    }

    /// <summary>布尔求值入口（条件节点使用）。解析失败返回 false。</summary>
    public static bool Evaluate(string expression, FlowContext context)
        => IsTruthy(EvaluateValue(expression, context));

    /// <summary>值求值入口（赋值节点 EXPRESSION 使用）。解析失败返回 null。</summary>
    public static object? EvaluateValue(string expression, FlowContext context)
    {
        if (string.IsNullOrWhiteSpace(expression)) return null;
        try
        {
            var parser = new ExpressionEvaluator(expression, context);
            var val = parser.ParseOr();
            parser.SkipWhitespace();
            // 存在无法识别的残余字符视为表达式错误
            if (parser._pos < parser._expr.Length) return null;
            return val;
        }
        catch
        {
            return null;
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
        var left = ParseAddSub();
        SkipWhitespace();

        string? op = null;
        if (Peek() == '=' && Peek(1) == '=') { _pos += 2; op = "=="; }
        else if (Peek() == '!' && Peek(1) == '=') { _pos += 2; op = "!="; }
        else if (Peek() == '>' && Peek(1) == '=') { _pos += 2; op = ">="; }
        else if (Peek() == '<' && Peek(1) == '=') { _pos += 2; op = "<="; }
        else if (Peek() == '>') { _pos++; op = ">"; }
        else if (Peek() == '<') { _pos++; op = "<"; }
        else return left;   // 无比较符，按真值判断

        var right = ParseAddSub();
        return Compare(left, right, op);
    }

    private object? ParseAddSub()
    {
        var left = ParseMulDiv();
        SkipWhitespace();
        while (Peek() == '+' || Peek() == '-')
        {
            var op = _expr[_pos++];
            var right = ParseMulDiv();
            left = op == '+' ? Add(left, right) : Arithmetic(left, right, op);
        }
        return left;
    }

    private object? ParseMulDiv()
    {
        var left = ParseUnaryMinus();
        SkipWhitespace();
        while (Peek() == '*' || Peek() == '/' || Peek() == '%')
        {
            var op = _expr[_pos++];
            var right = ParseUnaryMinus();
            left = Arithmetic(left, right, op);
        }
        return left;
    }

    private object? ParseUnaryMinus()
    {
        SkipWhitespace();
        if (Peek() == '-')
        {
            _pos++;
            return Negate(ParseUnaryMinus());
        }
        return ParsePostfix();
    }

    /// <summary>后缀操作：.prop / [i] / 切片 / .length / .toString() / .replace()</summary>
    private object? ParsePostfix()
    {
        var val = ParsePrimary();

        while (true)
        {
            SkipWhitespace();

            if (_pos < _expr.Length && _expr[_pos] == '.')
            {
                _pos++;
                SkipWhitespace();
                var ident = ParseIdentifier();
                if (string.IsNullOrEmpty(ident)) throw new InvalidOperationException("缺少属性名");
                if (ident.Equals("length", StringComparison.OrdinalIgnoreCase)) { val = GetLength(val); continue; }
                if (ident.Equals("toString", StringComparison.OrdinalIgnoreCase)) { val = ToStringOp(val, ParseMethodArgs()); continue; }
                if (ident.Equals("replace", StringComparison.OrdinalIgnoreCase))
                {
                    var args = ParseMethodArgs();
                    val = ReplaceOp(val, args.Count > 0 ? args[0] : null, args.Count > 1 ? args[1] : null);
                    continue;
                }
                val = GetProperty(val, ident);
                continue;
            }

            if (_pos < _expr.Length && _expr[_pos] == '[')
            {
                val = ParseIndexOrSlice(val);
                continue;
            }

            break;
        }

        return val;
    }

    private object? ParsePrimary()
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
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
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

        // 变量 / 关键字 / 未声明的字符串字面量
        var root = ParseIdentifier();
        if (string.IsNullOrEmpty(root))
            throw new InvalidOperationException($"无法识别的字符 '{c}'");

        if (root.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (root.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (root.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;

        // 已声明的变量 → 取变量值；未声明 → 按字符串字面量处理（兼容 env_status == paid 写法）
        return _context.Variables.TryGetValue(root, out var varVal)
            ? ToNative(varVal)
            : root;
    }

    // ===== 索引 / 切片 =====

    private object? ParseIndexOrSlice(object? val)
    {
        _pos++; // 跳过 '['
        SkipWhitespace();

        int? start = null, end = null;
        var isSlice = false;

        if (_pos < _expr.Length && _expr[_pos] == '.' && Peek(1) == '.')
        {
            // [..5]
            _pos += 2;
            isSlice = true;
            SkipWhitespace();
            var numStart = _pos;
            while (_pos < _expr.Length && char.IsDigit(_expr[_pos])) _pos++;
            if (_pos > numStart) end = int.Parse(_expr[numStart.._pos]);
        }
        else if (_pos < _expr.Length && char.IsDigit(_expr[_pos]))
        {
            // [n] 或 [n..m] 或 [n..]
            var numStart = _pos;
            while (_pos < _expr.Length && char.IsDigit(_expr[_pos])) _pos++;
            start = int.Parse(_expr[numStart.._pos]);
            SkipWhitespace();
            if (_pos < _expr.Length && _expr[_pos] == '.' && Peek(1) == '.')
            {
                _pos += 2;
                isSlice = true;
                SkipWhitespace();
                numStart = _pos;
                while (_pos < _expr.Length && char.IsDigit(_expr[_pos])) _pos++;
                if (_pos > numStart) end = int.Parse(_expr[numStart.._pos]);
            }
        }
        else
        {
            throw new InvalidOperationException("索引/切片必须为数字");
        }

        SkipWhitespace();
        if (_pos < _expr.Length && _expr[_pos] == ']') _pos++;

        return isSlice ? Slice(val, start, end) : GetArrayElement(val, start ?? 0);
    }

    private static object? Slice(object? val, int? start, int? end)
    {
        switch (val)
        {
            case string s:
            {
                var len = s.Length;
                var sIdx = Math.Clamp(start ?? 0, 0, len);
                var eIdx = Math.Clamp(end ?? len, 0, len);
                return eIdx <= sIdx ? "" : s.Substring(sIdx, eIdx - sIdx);
            }
            case List<object?> list:
            {
                var len = list.Count;
                var sIdx = Math.Clamp(start ?? 0, 0, len);
                var eIdx = Math.Clamp(end ?? len, 0, len);
                return eIdx <= sIdx ? new List<object?>() : list.GetRange(sIdx, eIdx - sIdx);
            }
            case object[] arr:
            {
                var len = arr.Length;
                var sIdx = Math.Clamp(start ?? 0, 0, len);
                var eIdx = Math.Clamp(end ?? len, 0, len);
                return eIdx <= sIdx ? Array.Empty<object?>() : arr[sIdx..eIdx];
            }
            case JsonElement json when json.ValueKind == JsonValueKind.Array:
            {
                var list = json.EnumerateArray().Select(e => (object?)ToNative(e)).ToList();
                return Slice(list, start, end);
            }
            default:
                return null;
        }
    }

    // ===== 属性导航 / 数组元素 / 长度 =====

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
            case string s:
                return index >= 0 && index < s.Length ? s.Substring(index, 1) : null;
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

    private static object? GetLength(object? obj)
    {
        switch (obj)
        {
            case string s: return (long)s.Length;
            case List<object?> list: return (long)list.Count;
            case object[] arr: return (long)arr.Length;
            case JsonElement json when json.ValueKind == JsonValueKind.Array: return (long)json.GetArrayLength();
            default: return null;
        }
    }

    // ===== 方法：toString / replace =====

    /// <summary>解析方法调用参数列表：('a','b') 或 (#.0##)，返回字符串参数</summary>
    private List<object?> ParseMethodArgs()
    {
        SkipWhitespace();
        if (_pos >= _expr.Length || _expr[_pos] != '(') throw new InvalidOperationException("方法需要括号调用");
        _pos++;

        var args = new List<object?>();
        while (true)
        {
            SkipWhitespace();
            if (_pos >= _expr.Length) throw new InvalidOperationException("括号不匹配");
            if (_expr[_pos] == ')') { _pos++; break; }

            // 字符串参数
            if (_expr[_pos] == '\'' || _expr[_pos] == '"')
            {
                var quote = _expr[_pos++];
                var start = _pos;
                while (_pos < _expr.Length && _expr[_pos] != quote) _pos++;
                args.Add(_expr[start.._pos]);
                if (_pos < _expr.Length) _pos++;
            }
            else
            {
                // 裸参数（如数字格式 #.0##）
                var start = _pos;
                while (_pos < _expr.Length && _expr[_pos] != ',' && _expr[_pos] != ')' && !char.IsWhiteSpace(_expr[_pos]))
                    _pos++;
                args.Add(_expr[start.._pos]);
            }

            SkipWhitespace();
            if (_pos < _expr.Length && _expr[_pos] == ',') { _pos++; continue; }
        }
        return args;
    }

    /// <summary>toString("格式")：数字用自定义格式（#.0##），日期用日期格式（yyyy-MM-dd HH:mm:ss）</summary>
    private static object? ToStringOp(object? val, List<object?> args)
    {
        var format = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
        if (val == null) return "";

        if (val is DateTime dt)
            return dt.ToString(format, CultureInfo.InvariantCulture);

        if (val is string s)
        {
            if (!string.IsNullOrEmpty(format) && IsDateFormat(format))
                return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed.ToString(format, CultureInfo.InvariantCulture) : s;
            if (format.IndexOfAny(new[] { '#', '0' }) >= 0)
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d.ToString(format, CultureInfo.InvariantCulture) : s;
            return s;
        }

        if (IsNumeric(val))
            return ToDouble(val).ToString(format, CultureInfo.InvariantCulture);

        return val.ToString() ?? "";
    }

    private static bool IsDateFormat(string format)
        => format.IndexOfAny("yMdHhmsfFgKtz".ToCharArray()) >= 0;

    /// <summary>replace('旧','新')：字符串替换</summary>
    private static object? ReplaceOp(object? val, object? oldVal, object? newVal)
    {
        var s = val?.ToString() ?? "";
        var old = oldVal?.ToString() ?? "";
        var @new = newVal?.ToString() ?? "";
        return string.IsNullOrEmpty(old) ? s : s.Replace(old, @new);
    }

    // ===== 算术 =====

    /// <summary>+：两边都是数字时做加法，否则做字符串拼接</summary>
    private static object? Add(object? left, object? right)
    {
        if (IsNumeric(left) && IsNumeric(right)) return ToDouble(left) + ToDouble(right);
        return ToDisplayString(left) + ToDisplayString(right);
    }

    /// <summary>- * / %：数字运算（数字字符串自动转为数字，* 即"转化为数字"；除零返回 null）</summary>
    private static object? Arithmetic(object? left, object? right, char op)
    {
        var l = ToDouble(left);
        var r = ToDouble(right);
        if ((op == '/' || op == '%') && r == 0) return null;
        return op switch
        {
            '-' => l - r,
            '*' => l * r,
            '/' => l / r,
            '%' => l % r,
            _ => l
        };
    }

    private static object? Negate(object? v) => -ToDouble(v);

    /// <summary>转展示字符串：null→""、bool→true/false、整数无小数点、日期 ISO 格式</summary>
    private static string ToDisplayString(object? v)
    {
        switch (v)
        {
            case null: return "";
            case bool b: return b ? "true" : "false";
            case double d:
                return d == Math.Floor(d) && !double.IsInfinity(d) && Math.Abs(d) < 1e15
                    ? ((long)d).ToString(CultureInfo.InvariantCulture)
                    : d.ToString(CultureInfo.InvariantCulture);
            case float f: return f.ToString(CultureInfo.InvariantCulture);
            case decimal m: return m.ToString(CultureInfo.InvariantCulture);
            case DateTime dt: return dt.ToString("yyyy-MM-dd HH:mm:ss");
            default: return v.ToString() ?? "";
        }
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

        // 字符串比较（==/!= 忽略大小写；日期统一为 ISO 格式后按字符串比较）
        var ls = left is DateTime ldt ? ldt.ToString("yyyy-MM-dd HH:mm:ss") : left.ToString() ?? "";
        var rs = right is DateTime rdt ? rdt.ToString("yyyy-MM-dd HH:mm:ss") : right.ToString() ?? "";
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
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
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
            string s => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0,
            _ => 0
        };
    }

    // ===== JsonElement → 原生类型 =====

    /// <summary>将 JsonElement 转为 .NET 原生类型，便于运算；对象/数组保留原 JsonElement 继续导航</summary>
    private static object? ToNative(object? obj)
    {
        if (obj is not JsonElement el) return obj;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el
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
