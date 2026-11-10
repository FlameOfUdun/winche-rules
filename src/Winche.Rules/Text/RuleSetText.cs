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

    /// <summary>Writes a ruleset as a rules file.</summary>
    public static string FormatRuleSet(RuleSet ruleSet)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < ruleSet.Matches.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            WriteBlock(sb, ruleSet.Matches[i], 0);
        }
        return sb.ToString();
    }

    private const int LineWidth = 100;

    private static void WriteBlock(StringBuilder sb, MatchBlock block, int depth)
    {
        var pad = new string(' ', depth * 2);
        sb.Append(pad).Append("match ").Append(block.Path).Append(" {\n");
        foreach (var allow in block.Allow)
        {
            var head = $"{pad}  allow {Operations(allow.Operations)}: if ";
            var condition = Format(allow.Condition);
            // A long condition made of && parts goes one part per line.
            if (head.Length + condition.Length + 1 > LineWidth && allow.Condition is AndExpression { Operands.Count: > 1 } and)
                condition = string.Join($"\n{pad}    && ", and.Operands.Select(o => Write(o, 4)));
            sb.Append(head).Append(condition).Append(";\n");
        }
        foreach (var nested in block.Matches)
        {
            sb.Append('\n');
            WriteBlock(sb, nested, depth + 1);
        }
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

        public RuleSet Parse()
        {
            var blocks = new List<MatchBlock>();
            while (true)
            {
                SkipTrivia();
                if (_pos >= text.Length) return new RuleSet(blocks);
                var at = _pos;
                var word = Word();
                if (word == "match") blocks.Add(Match());
                else if (word == "allow") throw Fail("'allow' must be inside a match block", at);
                else throw Fail(word.Length == 0 ? $"Unexpected '{text[at]}'" : $"Expected 'match' but found '{word}'", at);
            }
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

            var allow = new List<AllowRule>();
            var nested = new List<MatchBlock>();
            while (true)
            {
                SkipTrivia();
                if (_pos >= text.Length) throw Fail($"Missing '}}' to close 'match {path}'", text.Length);
                if (text[_pos] == '}')
                {
                    _pos++;
                    break;
                }
                var wordAt = _pos;
                var word = Word();
                if (word == "allow") allow.Add(Allow());
                else if (word == "match") nested.Add(Match());
                else throw Fail(word.Length == 0 ? $"Unexpected '{text[wordAt]}'" : $"Expected 'allow', 'match' or '}}' but found '{word}'", wordAt);
            }

            if (nested.Count > 0 && PathPattern.Parse(path)[^1] is RecursiveSegment)
                throw Fail($"'match {path}' ends in a recursive wildcard, so nothing nested in it can match", at);
            return new MatchBlock(path, allow, nested);
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
            _pos = end + 1;   // past ';'
            return new AllowRule(ops, condition);
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
