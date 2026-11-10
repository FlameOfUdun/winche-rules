using System.Text.RegularExpressions;

namespace Winche.Rules.Matching;

/// <summary>
/// A parsed path-pattern segment.
/// </summary>
internal abstract record PathSegment;
internal sealed record LiteralSegment(string Value) : PathSegment;
internal sealed record ParamSegment(string Name) : PathSegment;
internal sealed record RecursiveSegment(string Name) : PathSegment;

/// <summary>
/// Parses and matches path patterns: literal segments, <c>{id}</c> single-segment captures,
/// and a trailing <c>{name=**}</c> recursive capture. Captures are exposed as path-param variables.
/// Malformed patterns (bare <c>*</c>, unbalanced or partial braces, non-identifier capture names,
/// a non-trailing recursive wildcard) are rejected with <see cref="ArgumentException"/>.
/// </summary>
internal static partial class PathPattern
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex CaptureName();

    public static IReadOnlyList<PathSegment> Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var raw = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length == 0)
            throw new ArgumentException("Path pattern must contain at least one segment.", nameof(pattern));

        var segments = new List<PathSegment>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var s = raw[i];

            if (s.Contains('{') || s.Contains('}'))
            {
                if (s[0] != '{' || s[^1] != '}' || s.IndexOf('{', 1) >= 0 || s.LastIndexOf('}', s.Length - 2) >= 0)
                    throw new ArgumentException(
                        $"Malformed capture segment '{s}': '{{' and '}}' must wrap the whole segment.", nameof(pattern));

                var inner = s[1..^1];
                if (inner.Contains('{') || inner.Contains('}'))
                    throw new ArgumentException(
                        $"Malformed capture segment '{s}': nested braces are not allowed.", nameof(pattern));

                if (inner.EndsWith("=**", StringComparison.Ordinal))
                {
                    if (segments.Any(x => x is RecursiveSegment))
                        throw new ArgumentException($"A path may have only one recursive wildcard; '{s}' is a second.", nameof(pattern));
                    var name = inner[..^3];
                    if (!CaptureName().IsMatch(name))
                        throw new ArgumentException(
                            $"Invalid recursive capture name in '{s}': must match [A-Za-z_][A-Za-z0-9_]*.", nameof(pattern));
                    segments.Add(new RecursiveSegment(name));
                }
                else
                {
                    if (inner.Contains('='))
                        throw new ArgumentException(
                            $"Invalid capture '{s}': only the recursive '{{name=**}}' form may use '='.", nameof(pattern));
                    if (!CaptureName().IsMatch(inner))
                        throw new ArgumentException(
                            $"Invalid capture name in '{s}': must match [A-Za-z_][A-Za-z0-9_]*.", nameof(pattern));
                    segments.Add(new ParamSegment(inner));
                }
            }
            else
            {
                if (s.Contains('*'))
                    throw new ArgumentException(
                        $"Bare wildcard segment '{s}' is not valid; use '{{name}}' for a single segment or " +
                        "'{name=**}' for a recursive wildcard.", nameof(pattern));
                segments.Add(new LiteralSegment(s));
            }
        }
        return segments;
    }

    /// <summary>
    /// Matches a concrete path against a pattern, capturing variables. A recursive wildcard at the end
    /// (<c>users/{rest=**}</c>) matches one or more segments; one at the start or in the middle
    /// (<c>{path=**}/posts/{postId}</c>) matches zero or more, so it also covers a top-level <c>posts</c>.
    /// Its capture is the matched segments as a path. A pattern with more than one recursive wildcard matches
    /// nothing (they're rejected when rules are built).
    /// </summary>
    public static bool TryMatch(IReadOnlyList<PathSegment> pattern, string path, out Dictionary<string, RuleValue> captures)
    {
        captures = [];
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var recursive = -1;
        for (var i = 0; i < pattern.Count; i++)
        {
            if (pattern[i] is not RecursiveSegment) continue;
            if (recursive >= 0) return false;
            recursive = i;
        }

        if (recursive < 0)
            return pattern.Count == segs.Length && MatchRange(pattern, 0, segs, 0, pattern.Count, captures);

        var suffix = pattern.Count - recursive - 1;
        var middle = segs.Length - recursive - suffix;
        if (middle < (suffix == 0 ? 1 : 0)) return false;   // trailing: one or more; otherwise zero or more
        if (!MatchRange(pattern, 0, segs, 0, recursive, captures)) return false;
        if (!MatchRange(pattern, recursive + 1, segs, segs.Length - suffix, suffix, captures)) return false;
        captures[((RecursiveSegment)pattern[recursive]).Name] = RuleValue.Path(string.Join('/', segs[recursive..(recursive + middle)]));
        return true;
    }

    private static bool MatchRange(IReadOnlyList<PathSegment> pattern, int from, string[] segs, int segFrom, int count, Dictionary<string, RuleValue> captures)
    {
        for (var j = 0; j < count; j++)
        {
            var seg = segs[segFrom + j];
            switch (pattern[from + j])
            {
                case LiteralSegment lit when !string.Equals(lit.Value, seg, StringComparison.Ordinal):
                    return false;
                case ParamSegment p:
                    captures[p.Name] = RuleValue.String(seg);
                    break;
            }
        }
        return true;
    }

    /// <summary>The number of recursive wildcards in a (possibly flattened) pattern.</summary>
    internal static int RecursiveCount(IReadOnlyList<PathSegment> pattern) => pattern.Count(s => s is RecursiveSegment);

    internal static bool TryMatchPrefix(IReadOnlyList<PathSegment> prefix, string path, out Dictionary<string, RuleValue> captures)
    {
        captures = [];
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length < prefix.Count) return false;
        for (var i = 0; i < prefix.Count; i++)
        {
            switch (prefix[i])
            {
                case RecursiveSegment:
                    return false;
                case LiteralSegment lit:
                    if (!string.Equals(lit.Value, segs[i], StringComparison.Ordinal)) return false;
                    break;
                case ParamSegment p:
                    captures[p.Name] = RuleValue.String(segs[i]);
                    break;
            }
        }
        return true;
    }
}
