using System.Text;
using Winche.Rules.Expressions;
using Winche.Rules.Matching;

namespace Winche.Rules.Text;

/// <summary>
/// A whole ruleset as one text file, Firestore-style:
/// <code>
/// match users/{uid} {
///   allow read: if request.auth.uid == uid;
///   allow create: if request.auth.uid == uid
///     &amp;&amp; request.resource.data.keys().hasOnly(['name', 'email']);
///
///   match notes/{noteId} {
///     allow read, write: if request.auth.uid == uid;
///   }
/// }
/// </code>
/// <para>Operations: <c>get list create update delete</c>, and <c>read</c> (get, list) / <c>write</c>
/// (create, update, delete). <c>allow x;</c> without a condition allows unconditionally (written back as
/// <c>if true</c>). A nested <c>match</c> path extends its parent's. Comments (<c>//</c>) are allowed but,
/// since rules are stored as data, aren't kept.</para>
/// </summary>
public static partial class RuleText
{
    /// <summary>Parses a rules file. Throws <see cref="RuleSyntaxException"/> with the position of the first problem.</summary>
    public static RuleSet ParseRuleSet(string text) => new FileParser(text).Parse();

    /// <summary>Writes a ruleset as a rules file, using the readable forms (<c>not in</c>, <c>has only</c>, <c>changes only</c>).</summary>
    public static string FormatRuleSet(RuleSet ruleSet) => FormatRuleSet(ruleSet, readable: true);

    /// <summary>Writes a ruleset as a rules file; <paramref name="readable"/> false writes method chains instead.</summary>
    public static string FormatRuleSet(RuleSet ruleSet, bool readable)
    {
        var writer = new Writer(readable);
        var sb = new StringBuilder();
        foreach (var f in ruleSet.Functions ?? [])
        {
            WriteFunction(sb, writer, f, "");
            sb.Append('\n');
        }
        for (var i = 0; i < ruleSet.Matches.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            WriteBlock(sb, writer, ruleSet.Matches[i], 0);
        }
        return sb.ToString();
    }

    private const int LineWidth = 100;

    private static void WriteBlock(StringBuilder sb, Writer writer, MatchBlock block, int depth)
    {
        var pad = new string(' ', depth * 2);
        sb.Append(pad).Append("match ").Append(block.Path).Append(" {\n");
        foreach (var f in block.Functions ?? [])
        {
            WriteFunction(sb, writer, f, pad + "  ");
            sb.Append('\n');
        }
        foreach (var allow in block.Allow)
        {
            var head = $"{pad}  allow {Operations(allow.Operations)}: if ";
            var condition = writer.Write(allow.Condition, 0);
            // A long condition made of && parts goes one part per line.
            if (head.Length + condition.Length + 1 > LineWidth && allow.Condition is AndExpression { Operands.Count: > 1 } and)
                condition = string.Join($"\n{pad}    && ", and.Operands.Select(o => writer.Write(o, 4)));
            sb.Append(head).Append(condition).Append(";\n");
        }
        foreach (var nested in block.Matches)
        {
            sb.Append('\n');
            WriteBlock(sb, writer, nested, depth + 1);
        }
        sb.Append(pad).Append("}\n");
    }

    private static void WriteFunction(StringBuilder sb, Writer writer, RuleFunction f, string pad)
    {
        sb.Append(pad).Append("function ").Append(f.Name).Append('(').Append(string.Join(", ", f.Parameters)).Append(") {\n");
        foreach (var let in f.Lets ?? [])
            sb.Append(pad).Append("  let ").Append(let.Name).Append(" = ").Append(writer.Write(let.Value, 0)).Append(";\n");
        sb.Append(pad).Append("  return ").Append(writer.Write(f.Body, 0)).Append(";\n");
        sb.Append(pad).Append("}\n");
    }

    private static readonly RuleOperation[] Order = [RuleOperation.Get, RuleOperation.List, RuleOperation.Create, RuleOperation.Update, RuleOperation.Delete];

    private static string Operations(IReadOnlyList<RuleOperation> operations)
    {
        var set = operations.ToHashSet();
        var words = new List<string>();
        if (RuleOperations.Read.All(set.Contains)) { words.Add("read"); set.ExceptWith(RuleOperations.Read); }
        if (RuleOperations.Write.All(set.Contains)) { words.Add("write"); set.ExceptWith(RuleOperations.Write); }
        words.AddRange(Order.Where(set.Contains).Select(o => o.ToString().ToLowerInvariant()));
        return string.Join(", ", words);
    }

    private sealed class FileParser(string text)
    {
        private int _pos;
        private IReadOnlyList<PathSegment> _prefix = [];

        /// <summary>Where each condition and function body starts, to point errors found after parsing at the text.</summary>
        private readonly Dictionary<RuleExpression, int> _at = new(ReferenceEqualityComparer.Instance);

        public RuleSet Parse()
        {
            var blocks = new List<MatchBlock>();
            var functions = new List<RuleFunction>();
            while (true)
            {
                SkipTrivia();
                if (_pos >= text.Length) break;
                var at = _pos;
                var word = Word();
                if (word == "match") blocks.Add(Match());
                else if (word == "function") functions.Add(Function(at));
                else if (word == "allow") throw Fail("'allow' must be inside a match block", at);
                else throw Fail(word.Length == 0 ? $"Unexpected '{text[at]}'" : $"Expected 'match' or 'function' but found '{word}'", at);
            }
            var ruleSet = new RuleSet(blocks) { Functions = functions.Count > 0 ? functions : null };
            foreach (var problem in FunctionChecks.Check(ruleSet))
                throw Fail(problem.Message, Locate(problem));
            return ruleSet;
        }

        /// <summary>The offset of a problem: the call's name inside the expression it's in, else that expression's start.</summary>
        private int Locate(FunctionChecks.Problem problem)
        {
            if (problem.Function is { } definition && _functionAt.TryGetValue(definition, out var defAt)) return defAt;
            if (problem.In is null || !_at.TryGetValue(problem.In, out var start)) return 0;
            if (problem.Call is null) return start;
            var end = ConditionEndFrom(start);
            for (var i = text.IndexOf(problem.Call + "(", start, StringComparison.Ordinal); i >= 0 && i < end;
                 i = text.IndexOf(problem.Call + "(", i + 1, StringComparison.Ordinal))
                if (i == 0 || !(char.IsAsciiLetterOrDigit(text[i - 1]) || text[i - 1] is '_' or '.'))
                    return i;
            return start;
        }

        private readonly Dictionary<RuleFunction, int> _functionAt = new(ReferenceEqualityComparer.Instance);

        private RuleFunction Function(int at)
        {
            SkipTrivia();
            var nameAt = _pos;
            var name = Word();
            if (name.Length == 0) throw Fail("Expected a function name after 'function'", nameAt);
            SkipTrivia();
            Expect('(', "after the function name");
            var parameters = new List<string>();
            SkipTrivia();
            if (_pos < text.Length && text[_pos] == ')') _pos++;
            else
            {
                while (true)
                {
                    SkipTrivia();
                    var pAt = _pos;
                    var p = Word();
                    if (p.Length == 0) throw Fail("Expected a parameter name", pAt);
                    if (parameters.Contains(p)) throw Fail($"'{name}' has two parameters named '{p}'", pAt);
                    parameters.Add(p);
                    SkipTrivia();
                    if (_pos < text.Length && text[_pos] == ',') { _pos++; continue; }
                    Expect(')', "after the parameters");
                    break;
                }
            }
            SkipTrivia();
            Expect('{', "to start the function body");

            var lets = new List<LetBinding>();
            RuleExpression body;
            while (true)
            {
                SkipTrivia();
                var wordAt = _pos;
                var word = Word();
                if (word == "let")
                {
                    SkipTrivia();
                    var letAt = _pos;
                    var letName = Word();
                    if (letName.Length == 0) throw Fail("Expected a name after 'let'", letAt);
                    if (parameters.Contains(letName) || lets.Any(l => l.Name == letName))
                        throw Fail($"'{letName}' is already a parameter or let in '{name}'", letAt);
                    SkipTrivia();
                    if (_pos + 1 < text.Length && text[_pos] == '=' && text[_pos + 1] == '=') throw Fail("Use '=' after a let name", _pos);
                    Expect('=', $"after 'let {letName}'");
                    lets.Add(new LetBinding(letName, Expression("after '='")));
                }
                else if (word == "return")
                {
                    body = Expression("after 'return'");
                    break;
                }
                else
                {
                    throw Fail(word.Length == 0 && _pos < text.Length && text[_pos] == '}'
                        ? $"'{name}' needs a 'return'"
                        : $"Expected 'let' or 'return' in '{name}'", wordAt);
                }
            }
            SkipTrivia();
            Expect('}', $"after '{name}''s return");
            var function = new RuleFunction(name, parameters, lets, body);
            _functionAt[function] = nameAt;
            _ = at;
            return function;
        }

        /// <summary>An expression up to its ';' (consumed).</summary>
        private RuleExpression Expression(string where)
        {
            SkipTrivia();
            var start = _pos;
            var end = ConditionEnd();
            RuleExpression e;
            try
            {
                e = RuleText.Parse(text[start..end]);
            }
            catch (RuleSyntaxException ex)
            {
                throw Fail(ex.Reason == "A condition is empty" ? $"Expected an expression {where}" : ex.Reason, start + ex.Offset);
            }
            _at[e] = start;
            _pos = end + 1;
            return e;
        }

        private MatchBlock Match()
        {
            SkipTrivia();
            var at = _pos;
            while (_pos < text.Length && !char.IsWhiteSpace(text[_pos]) && !(text[_pos] == '{' && EndsPath())) _pos++;
            var path = text[at.._pos];
            if (path.Length == 0) throw Fail("Expected a path after 'match'", at);
            try
            {
                PathPattern.Parse(path);
            }
            catch (ArgumentException ex)
            {
                throw Fail(ex.Message.Split(" (Parameter")[0], at);
            }

            SkipTrivia();
            Expect('{', "after the match path");
            var outer = _prefix;
            _prefix = [.. outer, .. PathPattern.Parse(path)];

            var allow = new List<AllowRule>();
            var nested = new List<MatchBlock>();
            var functions = new List<RuleFunction>();
            while (true)
            {
                SkipTrivia();
                if (_pos >= text.Length) throw Fail($"Missing '}}' to close 'match {path}'", text.Length);
                if (text[_pos] == '}')
                {
                    _pos++;
                    _prefix = outer;
                    break;
                }
                var wordAt = _pos;
                var word = Word();
                if (word == "allow") allow.Add(Allow());
                else if (word == "match") nested.Add(Match());
                else if (word == "function") functions.Add(Function(wordAt));
                else throw Fail(word.Length == 0 ? $"Unexpected '{text[wordAt]}'" : $"Expected 'allow', 'match', 'function' or '}}' but found '{word}'", wordAt);
            }

            if (PathPattern.RecursiveCount([.. _prefix, .. PathPattern.Parse(path)]) > 1)
                throw Fail($"'match {path}' has a second recursive wildcard (a path, including its parents', may have one)", at);
            if (nested.Count > 0 && PathPattern.Parse(path)[^1] is RecursiveSegment)
                throw Fail($"'match {path}' ends in a recursive wildcard, so nothing nested in it can match", at);
            return new MatchBlock(path, allow, nested) { Functions = functions.Count > 0 ? functions : null };
        }

        // A '{' ends the path when it opens the block rather than a {capture}: nothing after it on the
        // path, i.e. the previous char isn't '/' (a capture starts a segment) — "users/{id}{" is a path
        // "users/{id}" followed by the block.
        private bool EndsPath() => _pos > 0 && text[_pos - 1] != '/' && !char.IsWhiteSpace(text[_pos - 1]) && text[_pos - 1] == '}';

        private AllowRule Allow()
        {
            var operations = new List<RuleOperation>();
            while (true)
            {
                SkipTrivia();
                var at = _pos;
                var word = Word();
                switch (word)
                {
                    case "read": operations.AddRange(RuleOperations.Read); break;
                    case "write": operations.AddRange(RuleOperations.Write); break;
                    case "get": operations.Add(RuleOperation.Get); break;
                    case "list": operations.Add(RuleOperation.List); break;
                    case "create": operations.Add(RuleOperation.Create); break;
                    case "update": operations.Add(RuleOperation.Update); break;
                    case "delete": operations.Add(RuleOperation.Delete); break;
                    case "":
                        throw Fail(_pos < text.Length ? $"Expected an operation but found '{text[_pos]}'" : "Expected an operation", at);
                    default:
                        throw Fail($"Unknown operation '{word}' (read, write, get, list, create, update, delete)", at);
                }
                SkipTrivia();
                if (_pos < text.Length && text[_pos] == ',') { _pos++; continue; }
                break;
            }
            var ops = Order.Where(operations.Contains).ToList();

            SkipTrivia();
            if (_pos < text.Length && text[_pos] == ';')
            {
                _pos++;
                return new AllowRule(ops, new LiteralExpression(RuleValue.Bool(true)));
            }
            Expect(':', "after the operations (or ';' to allow unconditionally)");
            SkipTrivia();
            var ifAt = _pos;
            if (Word() != "if") throw Fail("Expected 'if' before the condition", ifAt);

            var start = _pos;
            var end = ConditionEnd();
            RuleExpression condition;
            try
            {
                condition = RuleText.Parse(text[start..end]);
            }
            catch (RuleSyntaxException ex)
            {
                throw Fail(ex.Reason == "A condition is empty" ? "Expected a condition after 'if'" : ex.Reason, start + ex.Offset);
            }
            _at[condition] = start;
            _pos = end + 1;   // past ';'
            return new AllowRule(ops, condition);
        }

        private int ConditionEndFrom(int start)
        {
            var saved = _pos;
            _pos = start;
            try { return ConditionEnd(); }
            catch (RuleSyntaxException) { return text.Length; }
            finally { _pos = saved; }
        }

        // The ';' that ends a condition: the first one outside strings, `names`, comments and brackets.
        private int ConditionEnd()
        {
            var depth = 0;
            var i = _pos;
            while (i < text.Length)
            {
                var c = text[i];
                if (c is '\'' or '"' or '`')
                {
                    i++;
                    while (i < text.Length && text[i] != c && text[i] != '\n')
                        i += text[i] == '\\' && c != '`' ? 2 : 1;
                    i++;
                    continue;
                }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                {
                    while (i < text.Length && text[i] != '\n') i++;
                    continue;
                }
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}')
                {
                    if (depth == 0) throw Fail("Missing ';' after the condition", i);
                    depth--;
                }
                else if (c == ';' && depth == 0) return i;
                i++;
            }
            throw Fail("Missing ';' after the condition", text.Length);
        }

        private string Word()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiLetterOrDigit(text[_pos]) || text[_pos] == '_')) _pos++;
            return text[start.._pos];
        }

        private void Expect(char c, string where)
        {
            if (_pos < text.Length && text[_pos] == c)
            {
                _pos++;
                return;
            }
            throw Fail(_pos < text.Length ? $"Expected '{c}' {where} but found '{text[_pos]}'" : $"Expected '{c}' {where}", _pos);
        }

        private void SkipTrivia()
        {
            while (_pos < text.Length)
            {
                if (char.IsWhiteSpace(text[_pos])) { _pos++; continue; }
                if (text[_pos] == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n') _pos++;
                    continue;
                }
                break;
            }
        }

        private RuleSyntaxException Fail(string message, int offset) => Error(text, message, offset);
    }
}
