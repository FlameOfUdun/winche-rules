using System.Globalization;
using System.Text;
using Winche.Rules.Expressions;

namespace Winche.Rules.Text;

/// <summary>A rule condition that isn't valid text; <see cref="Line"/> and <see cref="Column"/> are 1-based.</summary>
public sealed class RuleSyntaxException(string message, int offset, int line, int column)
    : Exception($"{message} (line {line}, column {column})")
{
    /// <summary>The bare message, without the position.</summary>
    public string Reason { get; } = message;

    /// <summary>0-based character offset of the problem.</summary>
    public int Offset { get; } = offset;

    /// <summary>1-based line.</summary>
    public int Line { get; } = line;

    /// <summary>1-based column.</summary>
    public int Column { get; } = column;
}

/// <summary>
/// The text form of rule conditions, close to Firestore's rules language:
/// <code>
/// request.auth.token.sub == userId
///   &amp;&amp; request.resource.data.keys().hasOnly(['name', 'email'])
///   &amp;&amp; request.resource.data.name is string
///   &amp;&amp; !('role' in request.resource.data.diff(resource.data).affectedKeys())
/// </code>
/// <para>Operators, loosest first: <c>c ? a : b</c>, <c>||</c>, <c>&amp;&amp;</c>, then the comparisons
/// <c>== != &lt; &lt;= &gt; &gt;=</c>, <c>in</c> and <c>x is type</c>, then <c>!</c>, then <c>.field</c>,
/// <c>[index]</c> and method calls. <c>x.f(a)</c> is the function <c>f(x, a)</c>, so both spellings work.
/// Literals: numbers, <c>'strings'</c> or <c>"strings"</c>, <c>true</c>, <c>false</c>, <c>null</c>, lists
/// <c>[...]</c> and maps <c>{'k': v}</c> of literals, and <c>timestamp('ISO-8601')</c>, <c>path('a/b')</c>,
/// <c>bytes('base64')</c>, <c>float('1e3')</c> (finite only: rules are stored as JSON). A field name that isn't an identifier is written in backticks.</para>
/// <para><see cref="Format"/> and <see cref="Parse"/> round-trip: parsing formatted text gives back the same
/// expression, up to flattening nested <c>&amp;&amp;</c>/<c>||</c> and writing empty ones as <c>true</c>/<c>false</c>.</para>
/// </summary>
public static class RuleText
{
    /// <summary>Functions written as methods on their first argument (<c>x.keys()</c> rather than <c>keys(x)</c>).</summary>
    private static readonly HashSet<string> Methods = new(StringComparer.Ordinal)
    {
        "size", "keys", "hasOnly", "hasAll", "hasAny", "diff", "affectedKeys", "addedKeys", "removedKeys",
        "changedKeys", "unchangedKeys", "matches", "lower", "upper",
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal) { "true", "false", "null", "in", "is" };

    /// <summary>Parses a condition. Throws <see cref="RuleSyntaxException"/> with the position of the first problem.</summary>
    public static RuleExpression Parse(string text) => new Parser(text).ParseAll();

    /// <summary>Writes a condition as text.</summary>
    public static string Format(RuleExpression expression) => Write(expression, 0);

    // ── formatting ──────────────────────────────────────────────────────────

    // Precedence: 1 ternary, 2 or, 3 and, 4 comparison / in / is, 5 unary, 6 postfix, 7 primary.
    private static string Write(RuleExpression e, int min)
    {
        var (text, prec) = e switch
        {
            LiteralExpression l => (Literal(l.Value), l.Value.IsNumber && l.Value.AsNumber < 0 ? 5 : 7),
            VarExpression v => (Name(v.Name), 7),
            MemberExpression m => ($"{Write(m.Target, 6)}.{Name(m.Name)}", 6),
            IndexExpression ix => ($"{Write(ix.Target, 6)}[{Write(ix.Index, 0)}]", 6),
            CompareExpression c => ($"{Write(c.Left, 5)} {Op(c.Op)} {Write(c.Right, 5)}", 4),
            InExpression i => ($"{Write(i.Item, 5)} in {Write(i.Collection, 5)}", 4),
            NotExpression n => ($"!{Write(n.Operand, 5)}", 5),
            AndExpression { Operands.Count: 0 } => ("true", 7),
            OrExpression { Operands.Count: 0 } => ("false", 7),
            AndExpression { Operands.Count: 1 } a => (Write(a.Operands[0], min), 7),
            OrExpression { Operands.Count: 1 } o => (Write(o.Operands[0], min), 7),
            AndExpression a => (string.Join(" && ", a.Operands.Select(x => Write(x, 4))), 3),
            OrExpression o => (string.Join(" || ", o.Operands.Select(x => Write(x, 3))), 2),
            ConditionalExpression c => ($"{Write(c.Condition, 2)} ? {Write(c.Then, 0)} : {Write(c.Else, 0)}", 1),
            CallExpression call => Call(call),
            _ => throw new ArgumentException($"Cannot write a {e.GetType().Name} as text."),
        };
        return prec < min ? $"({text})" : text;
    }

    private static (string, int) Call(CallExpression call)
    {
        var args = call.Args;
        if (call.Name == "is" && args.Count == 2 && args[1] is LiteralExpression { Value.Kind: RuleValueKind.String } t
            && Evaluation.RuleFunctions.TypeNames.Contains(t.Value.AsString))
            return ($"{Write(args[0], 5)} is {t.Value.AsString}", 4);
        if (args.Count >= 1 && (Methods.Contains(call.Name) || (call.Name == "get" && args.Count == 3)))
            return ($"{Write(args[0], 6)}.{Name(call.Name)}({string.Join(", ", args.Skip(1).Select(a => Write(a, 0)))})", 6);
        return ($"{Name(call.Name)}({string.Join(", ", args.Select(a => Write(a, 0)))})", 7);
    }

    private static string Op(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Eq => "==", ComparisonOperator.Ne => "!=", ComparisonOperator.Lt => "<",
        ComparisonOperator.Le => "<=", ComparisonOperator.Gt => ">", ComparisonOperator.Ge => ">=",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static bool IsIdentifier(string s) =>
        s.Length > 0 && (char.IsAsciiLetter(s[0]) || s[0] == '_') && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
        && !Keywords.Contains(s);

    private static string Name(string s) => IsIdentifier(s) ? s : $"`{s.Replace("`", "``")}`";

    private static string Literal(RuleValue v) => v.Kind switch
    {
        RuleValueKind.Null => "null",
        RuleValueKind.Bool => v.AsBool ? "true" : "false",
        RuleValueKind.Int => v.AsInt.ToString(CultureInfo.InvariantCulture),
        RuleValueKind.Double => Double(v.AsDouble),
        RuleValueKind.String => Quote(v.AsString),
        RuleValueKind.Timestamp => $"timestamp({Quote(v.AsTimestamp.ToString("O", CultureInfo.InvariantCulture))})",
        RuleValueKind.Path => $"path({Quote(v.AsPath)})",
        RuleValueKind.Bytes => $"bytes({Quote(Convert.ToBase64String(v.AsBytes))})",
        RuleValueKind.List => $"[{string.Join(", ", v.AsList.Select(Literal))}]",
        RuleValueKind.Map => $"{{{string.Join(", ", v.AsMap.Select(kv => $"{Quote(kv.Key)}: {Literal(kv.Value)}"))}}}",
        _ => throw new ArgumentOutOfRangeException(nameof(v)),
    };

    private static string Double(double d)
    {
        if (double.IsNaN(d)) return "float('NaN')";
        if (double.IsPositiveInfinity(d)) return "float('Infinity')";
        if (double.IsNegativeInfinity(d)) return "float('-Infinity')";
        var s = d.ToString("R", CultureInfo.InvariantCulture);
        return s.Contains('.') || s.Contains('E') ? s : s + ".0";   // keep it a float, not an int, on the way back
    }

    private static string Quote(string s)
    {
        var sb = new StringBuilder("'");
        foreach (var c in s)
        {
            switch (c)
            {
                case '\'': sb.Append("\\'"); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}"); else sb.Append(c);
                    break;
            }
        }
        return sb.Append('\'').ToString();
    }

    // ── parsing ────────────────────────────────────────────────────────────

    private enum T { Ident, Number, String, Op, End }

    private readonly record struct Token(T Kind, string Text, int Offset, bool Quoted = false)
    {
        /// <summary>An unquoted identifier spelled <paramref name="word"/> (a `quoted` name is never a keyword).</summary>
        public bool IsWord(string word) => Kind == T.Ident && !Quoted && Text == word;
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly List<Token> _tokens;
        private int _i;

        public Parser(string text)
        {
            _text = text;
            _tokens = Lex(text);
        }

        public RuleExpression ParseAll()
        {
            if (Peek.Kind == T.End) throw Error("A condition is empty", Peek);
            var e = Ternary();
            if (Peek.Kind != T.End) throw Error($"Unexpected '{Peek.Text}'", Peek);
            return e;
        }

        private Token Peek => _tokens[_i];
        private Token Next() => _tokens[_i++];
        private bool IsOp(string op) => Peek.Kind == T.Op && Peek.Text == op;
        private bool IsWord(string w) => Peek.IsWord(w);

        private bool Accept(string op)
        {
            if (!IsOp(op)) return false;
            _i++;
            return true;
        }

        private void Expect(string op)
        {
            if (!Accept(op)) throw Error(Peek.Kind == T.End ? $"Expected '{op}' but the condition ended" : $"Expected '{op}' but found '{Peek.Text}'", Peek);
        }

        private RuleExpression Ternary()
        {
            var c = Or();
            if (!Accept("?")) return c;
            var then = Ternary();
            Expect(":");
            return new ConditionalExpression(c, then, Ternary());
        }

        private RuleExpression Or()
        {
            var items = new List<RuleExpression> { And() };
            while (Accept("||")) items.Add(And());
            return items.Count == 1 ? items[0] : new OrExpression(items);
        }

        private RuleExpression And()
        {
            var items = new List<RuleExpression> { Relation() };
            while (Accept("&&")) items.Add(Relation());
            return items.Count == 1 ? items[0] : new AndExpression(items);
        }

        private RuleExpression Relation()
        {
            var left = Unary();
            if (Peek.Kind == T.Op && Peek.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
            {
                var op = Next().Text switch
                {
                    "==" => ComparisonOperator.Eq, "!=" => ComparisonOperator.Ne, "<" => ComparisonOperator.Lt,
                    "<=" => ComparisonOperator.Le, ">" => ComparisonOperator.Gt, _ => ComparisonOperator.Ge,
                };
                return new CompareExpression(left, op, Unary());
            }
            if (IsWord("in"))
            {
                Next();
                return new InExpression(left, Unary());
            }
            if (IsWord("is"))
            {
                Next();
                var type = Peek;
                if (type.Kind != T.Ident || type.Quoted || !Evaluation.RuleFunctions.TypeNames.Contains(type.Text))
                    throw Error($"Expected a type after 'is' ({string.Join(", ", Evaluation.RuleFunctions.TypeNames)})", type);
                Next();
                return new CallExpression("is", [left, new LiteralExpression(RuleValue.String(type.Text))]);
            }
            return left;
        }

        private RuleExpression Unary()
        {
            if (Accept("!")) return new NotExpression(Unary());
            if (IsOp("-"))
            {
                var minus = Next();
                if (Peek.Kind != T.Number) throw Error("'-' must be followed by a number", minus);
                return new LiteralExpression(Number(Next(), negative: true));
            }
            return Postfix();
        }

        private RuleExpression Postfix()
        {
            var e = Primary();
            while (true)
            {
                if (Accept("."))
                {
                    var name = Peek;
                    if (name.Kind != T.Ident) throw Error("Expected a field or method name after '.'", name);
                    Next();
                    e = IsOp("(")
                        ? new CallExpression(name.Text, [e, .. Args()])
                        : new MemberExpression(e, name.Text);
                }
                else if (Accept("["))
                {
                    var index = Ternary();
                    Expect("]");
                    e = new IndexExpression(e, index);
                }
                else
                {
                    return e;
                }
            }
        }

        private List<RuleExpression> Args()
        {
            Expect("(");
            var args = new List<RuleExpression>();
            if (Accept(")")) return args;
            do args.Add(Ternary()); while (Accept(","));
            Expect(")");
            return args;
        }

        private RuleExpression Primary()
        {
            var t = Peek;
            switch (t.Kind)
            {
                case T.Number:
                    Next();
                    return new LiteralExpression(Number(t, negative: false));
                case T.String:
                    Next();
                    return new LiteralExpression(RuleValue.String(t.Text));
                case T.Ident when t.IsWord("true"):
                    Next();
                    return new LiteralExpression(RuleValue.Bool(true));
                case T.Ident when t.IsWord("false"):
                    Next();
                    return new LiteralExpression(RuleValue.Bool(false));
                case T.Ident when t.IsWord("null"):
                    Next();
                    return new LiteralExpression(RuleValue.Null);
                case T.Ident when !t.Quoted && Keywords.Contains(t.Text):
                    throw Error($"Unexpected '{t.Text}'", t);
                case T.Ident:
                    Next();
                    if (!IsOp("(")) return new VarExpression(t.Text);
                    var args = Args();
                    return (t.Quoted ? null : (RuleExpression?)LiteralCall(t, args)) ?? new CallExpression(t.Text, args);
                case T.Op when t.Text == "(":
                    Next();
                    var inner = Ternary();
                    Expect(")");
                    return inner;
                case T.Op when t.Text == "[":
                    Next();
                    var items = new List<RuleValue>();
                    if (!Accept("]"))
                    {
                        do items.Add(Constant()); while (Accept(","));
                        Expect("]");
                    }
                    return new LiteralExpression(RuleValue.List(items));
                case T.Op when t.Text == "{":
                    Next();
                    var map = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
                    if (!Accept("}"))
                    {
                        do
                        {
                            var key = Peek;
                            if (key.Kind is not (T.String or T.Ident)) throw Error("Expected a map key", key);
                            Next();
                            Expect(":");
                            if (!map.TryAdd(key.Text, Constant())) throw Error($"Duplicate map key '{key.Text}'", key);
                        } while (Accept(","));
                        Expect("}");
                    }
                    return new LiteralExpression(RuleValue.Map(map));
                case T.End:
                    throw Error("The condition ended too early", t);
                default:
                    throw Error($"Unexpected '{t.Text}'", t);
            }
        }

        // A list or map element: only constants (the expression model has no list or map construction).
        private RuleValue Constant()
        {
            var at = Peek;
            var e = Unary();
            return e is LiteralExpression l ? l.Value : throw Error("List and map elements must be constants", at);
        }

        // timestamp('…'), path('…'), bytes('…'), float('NaN') with a constant string are literals.
        private LiteralExpression? LiteralCall(Token name, List<RuleExpression> args)
        {
            if (name.Text is not ("timestamp" or "path" or "bytes" or "float")) return null;
            if (args is not [LiteralExpression { Value.Kind: RuleValueKind.String } arg])
                throw Error($"{name.Text}() takes one string", name);
            var s = arg.Value.AsString;
            switch (name.Text)
            {
                case "timestamp":
                    return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)
                        ? new LiteralExpression(RuleValue.Timestamp(ts))
                        : throw Error($"'{s}' is not an ISO-8601 timestamp", name);
                case "path":
                    return new LiteralExpression(RuleValue.Path(s));
                case "bytes":
                    try { return new LiteralExpression(RuleValue.Bytes(Convert.FromBase64String(s))); }
                    catch (FormatException) { throw Error($"'{s}' is not base64", name); }
                default:
                    // Rules are stored as JSON, which has no NaN or Infinity.
                    return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d)
                        ? new LiteralExpression(RuleValue.Double(d))
                        : throw Error($"'{s}' is not a finite number", name);
            }
        }

        private RuleValue Number(Token t, bool negative)
        {
            var s = negative ? "-" + t.Text : t.Text;
            if (!t.Text.Contains('.') && !t.Text.Contains('e') && !t.Text.Contains('E'))
                return long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l)
                    ? RuleValue.Int(l)
                    : throw Error($"{s} is out of range for an integer", t);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                ? RuleValue.Double(d)
                : throw Error($"'{s}' is not a number", t);
        }

        private RuleSyntaxException Error(string message, Token at) => Error(message, at.Offset);

        private RuleSyntaxException Error(string message, int offset) => RuleText.Error(_text, message, offset);

        private static readonly string[] Operators = ["&&", "||", "==", "!=", "<=", ">=", "<", ">", "!", "?", ":", ".", ",", "(", ")", "[", "]", "{", "}", "-"];

        private static List<Token> Lex(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;   // a // comment
                    continue;
                }
                var start = i;
                if (char.IsAsciiLetter(c) || c == '_')
                {
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    tokens.Add(new Token(T.Ident, text[start..i], start));
                }
                else if (c == '`')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= text.Length) throw RuleText.Error(text, "A `quoted name` isn't closed", start);
                        if (text[i] == '`')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '`') { sb.Append('`'); i += 2; continue; }
                            i++;
                            break;
                        }
                        sb.Append(text[i++]);
                    }
                    if (sb.Length == 0) throw RuleText.Error(text, "A `quoted name` can't be empty", start);
                    tokens.Add(new Token(T.Ident, sb.ToString(), start, Quoted: true));   // a name, never a keyword
                }
                else if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]) && !PrecededByValue(tokens)))
                {
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    if (i < text.Length && text[i] == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
                    {
                        i++;
                        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    }
                    if (i < text.Length && text[i] is 'e' or 'E')
                    {
                        i++;
                        if (i < text.Length && text[i] is '+' or '-') i++;
                        if (i >= text.Length || !char.IsAsciiDigit(text[i])) throw RuleText.Error(text, "A number's exponent has no digits", start);
                        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    }
                    tokens.Add(new Token(T.Number, text[start..i], start));
                }
                else if (c is '\'' or '"')
                {
                    var sb = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= text.Length || text[i] == '\n') throw RuleText.Error(text, "A string isn't closed", start);
                        var ch = text[i++];
                        if (ch == c) break;
                        if (ch != '\\') { sb.Append(ch); continue; }
                        if (i >= text.Length) throw RuleText.Error(text, "A string isn't closed", start);
                        var esc = text[i++];
                        switch (esc)
                        {
                            case '\\': sb.Append('\\'); break;
                            case '\'': sb.Append('\''); break;
                            case '"': sb.Append('"'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u' when i + 4 <= text.Length && int.TryParse(text.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code):
                                sb.Append((char)code);
                                i += 4;
                                break;
                            default:
                                throw RuleText.Error(text, $"Unknown escape '\\{esc}'", i - 2);
                        }
                    }
                    tokens.Add(new Token(T.String, sb.ToString(), start));
                }
                else
                {
                    var op = Operators.FirstOrDefault(o => string.CompareOrdinal(text, i, o, 0, o.Length) == 0);
                    if (op is null)
                        throw RuleText.Error(text, c == '=' ? "Use '==' to compare" : c == '&' ? "Use '&&'" : c == '|' ? "Use '||'" : $"Unexpected '{c}'", i);
                    tokens.Add(new Token(T.Op, op, i));
                    i += op.Length;
                }
            }
            tokens.Add(new Token(T.End, "", text.Length));
            return tokens;
        }

        // ".5" starts a number unless it follows something a member access could apply to (x.5 is an error later).
        private static bool PrecededByValue(List<Token> tokens) =>
            tokens.Count > 0 && tokens[^1] is { Kind: T.Ident or T.Number or T.String } or { Kind: T.Op, Text: ")" or "]" };
    }

    private static RuleSyntaxException Error(string text, string message, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < offset; i++)
        {
            if (text[i] != '\n') continue;
            line++;
            lineStart = i + 1;
        }
        return new RuleSyntaxException(message, offset, line, offset - lineStart + 1);
    }
}
