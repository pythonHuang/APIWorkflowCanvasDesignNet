namespace Juggle.Domain.Engine;

/// <summary>
/// 报表公式引擎 — 递归下降解析 + 求值
/// 支持: 单元格引用(A1)、范围引用(A1:A10)、字段引用(${field})、
/// 算术(+ - * / %)、比较(== != > < >= <=)、逻辑(&& || !)、
/// 三元(?:)、内置函数(SUM/AVG/MIN/MAX/COUNT/IF/SUBSTR等)
/// </summary>
public class FormulaEngine
{
    private readonly string _expr;
    private int _pos;
    private readonly Dictionary<string, object?> _cellValues;  // "A1" → value
    private readonly Dictionary<string, object?> _fields;       // "${field}" → value
    private readonly Func<string, string, List<object?>> _rangeResolver; // (start, end) → values

    public FormulaEngine(string expr,
        Dictionary<string, object?> cellValues,
        Dictionary<string, object?> fields,
        Func<string, string, List<object?>> rangeResolver)
    {
        _expr = expr;
        _pos = 0;
        _cellValues = cellValues;
        _fields = fields;
        _rangeResolver = rangeResolver;
    }

    public object? Evaluate()
    {
        if (string.IsNullOrEmpty(_expr)) return "";
        var val = ParseTernary();
        SkipWhitespace();
        if (_pos < _expr.Length)
            return $"#ERR: unexpected '{_expr[_pos]}'";
        return val;
    }

    // ===== Recursive Descent Parser =====

    private object? ParseTernary()
    {
        var cond = ParseOr();
        SkipWhitespace();
        if (Peek() == '?')
        {
            _pos++;
            var trueVal = ParseTernary();
            SkipWhitespace();
            if (Peek() == ':')
            {
                _pos++;
                var falseVal = ParseTernary();
                return IsTruthy(cond) ? trueVal : falseVal;
            }
            return "#ERR: missing ':'";
        }
        return cond;
    }

    private object? ParseOr()
    {
        var left = ParseAnd();
        SkipWhitespace();
        while (Peek() == '|' && Peek(1) == '|')
        {
            _pos += 2;
            var right = ParseAnd();
            left = IsTruthy(left) ? left : right; // short-circuit
        }
        return left;
    }

    private object? ParseAnd()
    {
        var left = ParseCompare();
        SkipWhitespace();
        while (Peek() == '&' && Peek(1) == '&')
        {
            _pos += 2;
            var right = ParseCompare();
            left = IsTruthy(left) ? right : false; // short-circuit
        }
        return left;
    }

    private object? ParseCompare()
    {
        var left = ParseAddSub();
        SkipWhitespace();
        var op = "";
        if (Peek() == '=' && Peek(1) == '=') { _pos += 2; op = "=="; }
        else if (Peek() == '!' && Peek(1) == '=') { _pos += 2; op = "!="; }
        else if (Peek() == '>' && Peek(1) == '=') { _pos += 2; op = ">="; }
        else if (Peek() == '<' && Peek(1) == '=') { _pos += 2; op = "<="; }
        else if (Peek() == '>') { _pos++; op = ">"; }
        else if (Peek() == '<') { _pos++; op = "<"; }
        else return left;

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
            left = Arithmetic(left, right, op);
        }
        return left;
    }

    private object? ParseMulDiv()
    {
        var left = ParseUnary();
        SkipWhitespace();
        while (Peek() == '*' || Peek() == '/' || Peek() == '%')
        {
            var op = _expr[_pos++];
            var right = ParseUnary();
            left = Arithmetic(left, right, op);
        }
        return left;
    }

    private object? ParseUnary()
    {
        SkipWhitespace();
        if (Peek() == '-') { _pos++; var v = ParseUnary(); return Negate(v); }
        if (Peek() == '!') { _pos++; return !IsTruthy(ParseUnary()); }
        return ParsePrimary();
    }

    private object? ParsePrimary()
    {
        SkipWhitespace();
        if (_pos >= _expr.Length) return "#ERR: unexpected end";

        // 数字
        if (char.IsDigit(_expr[_pos]) || (_expr[_pos] == '.' && _pos + 1 < _expr.Length && char.IsDigit(_expr[_pos + 1])))
        {
            var start = _pos;
            while (_pos < _expr.Length && (char.IsDigit(_expr[_pos]) || _expr[_pos] == '.')) _pos++;
            var numStr = _expr[start.._pos];
            if (double.TryParse(numStr, out var d))
                return numStr.Contains('.') ? d : (object)(long)d;
            return d;
        }

        // 字符串
        if (_expr[_pos] == '\'' || _expr[_pos] == '"')
        {
            var quote = _expr[_pos++];
            var start = _pos;
            while (_pos < _expr.Length && _expr[_pos] != quote) _pos++;
            var str = _expr[start.._pos];
            if (_pos < _expr.Length) _pos++;
            return str;
        }

        // 字段引用 ${field}
        if (_expr[_pos] == '$' && _pos + 1 < _expr.Length && _expr[_pos + 1] == '{')
        {
            _pos += 2;
            var start = _pos;
            while (_pos < _expr.Length && _expr[_pos] != '}') _pos++;
            var field = _expr[start.._pos];
            if (_pos < _expr.Length) _pos++;
            return _fields.TryGetValue($"${{{field}}}", out var fv) ? fv : _fields.TryGetValue(field, out fv) ? fv : null;
        }

        // 括号
        if (_expr[_pos] == '(')
        {
            _pos++;
            var val = ParseTernary();
            SkipWhitespace();
            if (_pos < _expr.Length && _expr[_pos] == ')') _pos++;
            return val;
        }

        // 函数调用 / 单元格引用
        var ident = ParseIdentifier();
        if (string.IsNullOrEmpty(ident)) return "#ERR: expected value";

        SkipWhitespace();
        if (_pos < _expr.Length && _expr[_pos] == '(')
            return CallFunction(ident);

        // 单元格引用 A1 或 ds1!A1
        if (char.IsLetter(ident[0]))
            return ResolveCellRef(ident);

        return ident;
    }

    private string ParseIdentifier()
    {
        var start = _pos;
        while (_pos < _expr.Length && (char.IsLetterOrDigit(_expr[_pos]) || _expr[_pos] == '_' || _expr[_pos] == '!' || _expr[_pos] == ':'))
        {
            if (_expr[_pos] == '!' || _expr[_pos] == ':')
            {
                // 范围引用: A1:A10 — 停止标识符解析，交给 ResolveCellRef
                if (_expr[_pos] == ':') break;
                _pos++; // skip '!'
                continue;
            }
            _pos++;
        }
        return _expr[start.._pos];
    }

    private object? CallFunction(string name)
    {
        _pos++; // skip '('
        var args = new List<object?>();
        var depth = 1;
        var argStart = _pos;
        while (_pos < _expr.Length && depth > 0)
        {
            if (_expr[_pos] == '(') depth++;
            else if (_expr[_pos] == ')') depth--;
            else if (_expr[_pos] == ',' && depth == 1)
            {
                args.Add(EvaluateArg(_expr[argStart.._pos]));
                argStart = _pos + 1;
            }
            _pos++;
        }
        if (argStart < _pos - 1)
            args.Add(EvaluateArg(_expr[argStart..(_pos - 1)]));

        return ExecuteFunction(name.ToUpper(), args);
    }

    private object? EvaluateArg(string argExpr)
    {
        if (string.IsNullOrWhiteSpace(argExpr)) return null;
        // 范围引用: A1:A10
        if (argExpr.Contains(':') && char.IsLetter(argExpr.Trim()[0]))
        {
            var parts = argExpr.Trim().Split(':');
            if (parts.Length == 2)
            {
                var values = _rangeResolver(parts[0].Trim(), parts[1].Trim());
                return values;
            }
        }
        // 单元格引用
        if (char.IsLetter(argExpr.Trim()[0]) && !argExpr.StartsWith("$"))
        {
            return ResolveCellRef(argExpr.Trim());
        }
        // 嵌套公式求值
        var sub = new FormulaEngine(argExpr.Trim(), _cellValues, _fields, _rangeResolver);
        return sub.Evaluate();
    }

    private object? ExecuteFunction(string name, List<object?> args)
    {
        try
        {
            switch (name)
            {
                case "SUM": return SumRange(args);
                case "AVG": return AvgRange(args);
                case "MIN": return MinRange(args);
                case "MAX": return MaxRange(args);
                case "COUNT": return CountRange(args);
                case "IF": return args.Count >= 3 ? (IsTruthy(args[0]) ? args[1] : args[2]) : "#ERR: IF requires 3 args";
                case "SUBSTR": return args.Count >= 3 ? SubStr(args) : "#ERR";
                case "LEN": return args.Count >= 1 ? (args[0]?.ToString()?.Length ?? 0) : 0;
                case "UPPER": return args.Count >= 1 ? args[0]?.ToString()?.ToUpper() : "";
                case "LOWER": return args.Count >= 1 ? args[0]?.ToString()?.ToLower() : "";
                case "TRIM": return args.Count >= 1 ? args[0]?.ToString()?.Trim() : "";
                case "CONCAT": return string.Concat(args.Select(a => a?.ToString() ?? ""));
                case "ROUND": return args.Count >= 2 ? Math.Round(ToDouble(args[0]), ToInt(args[1])) : args[0];
                case "ABS": return args.Count >= 1 ? Math.Abs(ToDouble(args[0])) : 0;
                case "NOW": return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                case "TODAY": return DateTime.Now.ToString("yyyy-MM-dd");
                case "ROW": return _cellValues.TryGetValue("_rowIndex", out var ri) ? (ToInt(ri) + 1) : 1;
                default: return $"#NAME? {name}";
            }
        }
        catch { return "#ERR"; }
    }

    // ===== Range Operations =====

    private object SumRange(List<object?> args)
    {
        double sum = 0;
        foreach (var a in args)
        {
            if (a is List<object?> list) sum += list.Sum(v => ToDouble(v));
            else sum += ToDouble(a);
        }
        return sum;
    }

    private object AvgRange(List<object?> args)
    {
        var all = FlattenRange(args);
        return all.Count > 0 ? all.Average(ToDouble) : 0;
    }

    private object MinRange(List<object?> args)
    {
        var all = FlattenRange(args);
        return all.Count > 0 ? all.Min(ToDouble) : 0;
    }

    private object MaxRange(List<object?> args)
    {
        var all = FlattenRange(args);
        return all.Count > 0 ? all.Max(ToDouble) : 0;
    }

    private object CountRange(List<object?> args)
    {
        return FlattenRange(args).Count;
    }

    private object SubStr(List<object?> args)
    {
        var s = args[0]?.ToString() ?? "";
        var start = ToInt(args[1]);
        var len = args.Count > 2 ? ToInt(args[2]) : s.Length - start;
        if (start < 0) start = s.Length + start;
        if (start < 0 || start >= s.Length) return "";
        if (start + len > s.Length) len = s.Length - start;
        return s.Substring(start, len);
    }

    private List<object?> FlattenRange(List<object?> args)
    {
        var result = new List<object?>();
        foreach (var a in args)
        {
            if (a is List<object?> list) result.AddRange(list);
            else result.Add(a);
        }
        return result;
    }

    // ===== Cell Reference Resolution =====

    private object? ResolveCellRef(string refStr)
    {
        // dsName!A1 format
        string? dataset = null;
        var cellPart = refStr;
        if (refStr.Contains('!'))
        {
            var parts = refStr.Split('!');
            dataset = parts[0];
            cellPart = parts[1];
        }

        if (!string.IsNullOrEmpty(dataset))
            cellPart = $"{dataset}!{cellPart}";

        if (_cellValues.TryGetValue(cellPart, out var val))
            return val;
        if (_cellValues.TryGetValue(refStr, out val))
            return val;
        return null;
    }

    // ===== Arithmetic & Comparison =====

    private static object? Arithmetic(object? left, object? right, char op)
    {
        var l = ToDouble(left);
        var r = ToDouble(right);
        return op switch { '+' => l + r, '-' => l - r, '*' => l * r, '/' => r != 0 ? l / r : "#DIV/0!", '%' => l % r, _ => 0 };
    }

    private static object? Negate(object? v) => -ToDouble(v);

    private static object Compare(object? left, object? right, string op)
    {
        if (left is double || right is double || left is long || right is long || left is int || right is int)
        {
            var l = ToDouble(left); var r = ToDouble(right);
            return op switch { "==" => l == r, "!=" => l != r, ">" => l > r, "<" => l < r, ">=" => l >= r, "<=" => l <= r, _ => false };
        }
        var ls = left?.ToString() ?? ""; var rs = right?.ToString() ?? "";
        return op switch { "==" => ls == rs, "!=" => ls != rs, ">" => ls.CompareTo(rs) > 0, "<" => ls.CompareTo(rs) < 0, ">=" => ls.CompareTo(rs) >= 0, "<=" => ls.CompareTo(rs) <= 0, _ => false };
    }

    // ===== Helpers =====

    private static double ToDouble(object? v)
    {
        if (v == null) return 0;
        if (v is double d) return d;
        if (v is long l) return l;
        if (v is int i) return i;
        if (v is float f) return f;
        if (v is decimal m) return (double)m;
        if (double.TryParse(v.ToString(), out var r)) return r;
        return 0;
    }

    private static int ToInt(object? v)
    {
        if (v == null) return 0;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (v is double d) return (int)d;
        if (int.TryParse(v.ToString(), out var r)) return r;
        return 0;
    }

    private static bool IsTruthy(object? v)
    {
        if (v == null) return false;
        if (v is bool b) return b;
        if (v is double d) return d != 0;
        if (v is long l) return l != 0;
        if (v is int i) return i != 0;
        if (v is string s) return !string.IsNullOrEmpty(s) && s != "false" && s != "0";
        return true;
    }

    private char Peek(int ahead = 0) => _pos + ahead < _expr.Length ? _expr[_pos + ahead] : '\0';

    private void SkipWhitespace()
    {
        while (_pos < _expr.Length && char.IsWhiteSpace(_expr[_pos])) _pos++;
    }

    // ===== Static Entry Point =====
    public static object? Eval(string formula,
        Dictionary<string, object?>? cellValues = null,
        Dictionary<string, object?>? fields = null,
        Func<string, string, List<object?>>? rangeResolver = null)
    {
        var expr = formula.StartsWith("=") ? formula[1..] : formula;
        var engine = new FormulaEngine(expr,
            cellValues ?? new(),
            fields ?? new(),
            rangeResolver ?? ((s, e) => new()));
        return engine.Evaluate();
    }
}
