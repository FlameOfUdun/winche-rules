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
/// </summary>
internal static class PathPattern
{
    public static IReadOnlyList<PathSegment> Parse(string pattern)
    {
        var raw = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var segments = new List<PathSegment>(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var s = raw[i];
            if (s.Length >= 2 && s[0] == '{' && s[^1] == '}')
            {
                var inner = s[1..^1];
                if (inner.EndsWith("=**", StringComparison.Ordinal))
                {
                    if (i != raw.Length - 1)
                        throw new ArgumentException($"Recursive wildcard '{s}' must be the last segment.", nameof(pattern));
                    segments.Add(new RecursiveSegment(inner[..^3]));
                }
                else
                {
                    if (inner.Length == 0)
                        throw new ArgumentException("Empty capture '{}' is not valid.", nameof(pattern));
                    segments.Add(new ParamSegment(inner));
                }
            }
            else
            {
                segments.Add(new LiteralSegment(s));
            }
        }
        return segments;
    }

    /// <summary>
    /// Matches a concrete path against a pattern, capturing variables.
    /// </summary>
    public static bool TryMatch(IReadOnlyList<PathSegment> pattern, string path, out Dictionary<string, RuleValue> captures)
    {
        captures = [];
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < pattern.Count; i++)
        {
            switch (pattern[i])
            {
                case RecursiveSegment r:
                    captures[r.Name] = RuleValue.Path(string.Join('/', segs[i..]));
                    return true;
                default:
                    if (i >= segs.Length) return false;
                    if (pattern[i] is LiteralSegment lit)
                    {
                        if (!string.Equals(lit.Value, segs[i], StringComparison.Ordinal)) return false;
                    }
                    else if (pattern[i] is ParamSegment p)
                    {
                        captures[p.Name] = RuleValue.String(segs[i]);
                    }
                    break;
            }
        }

        return pattern.Count == segs.Length;   // no recursive wildcard → lengths must match exactly
    }

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
