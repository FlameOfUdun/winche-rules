global using PathSegment = Winche.PathPatternSegment;
global using LiteralSegment = Winche.LiteralPathSegment;
global using ParamSegment = Winche.ParamPathSegment;
global using RecursiveSegment = Winche.RecursivePathSegment;

namespace Winche.Rules.Matching;

/// <summary>
/// Rules' view of Winche.Core's <see cref="Winche.PathPattern"/> — the same path patterns as hooks and function
/// triggers — with captures as rule values: <c>{id}</c> a string, <c>{name=**}</c> a path. Malformed patterns are
/// rejected with an <see cref="ArgumentException"/>.
/// </summary>
internal static class RulePath
{
    public static IReadOnlyList<PathSegment> Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return Winche.PathPattern.Parse(pattern).Segments;
    }

    /// <summary>
    /// Matches a concrete path against a pattern, capturing variables. A recursive wildcard at the end
    /// (<c>users/{rest=**}</c>) matches one or more segments; one at the start or in the middle
    /// (<c>{path=**}/posts/{postId}</c>) matches zero or more. A pattern with more than one recursive wildcard matches
    /// nothing (they're rejected when rules are built).
    /// </summary>
    public static bool TryMatch(IReadOnlyList<PathSegment> pattern, string path, out Dictionary<string, RuleValue> captures)
    {
        captures = [];
        if (!Winche.PathPattern.Of(pattern).TryMatch(path, out var raw)) return false;
        captures = Values(pattern, raw);
        return true;
    }

    /// <summary>The number of recursive wildcards in a (possibly flattened) pattern.</summary>
    internal static int RecursiveCount(IReadOnlyList<PathSegment> pattern) => pattern.Count(s => s is RecursiveSegment);

    internal static bool TryMatchPrefix(IReadOnlyList<PathSegment> prefix, string path, out Dictionary<string, RuleValue> captures)
    {
        captures = [];
        if (!Winche.PathPattern.Of(prefix).TryMatchPrefix(path, out var raw)) return false;
        captures = Values(prefix, raw);
        return true;
    }

    private static Dictionary<string, RuleValue> Values(IReadOnlyList<PathSegment> pattern, Dictionary<string, string> raw)
    {
        var recursive = pattern.OfType<RecursiveSegment>().Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        return raw.ToDictionary(kv => kv.Key, kv => recursive.Contains(kv.Key) ? RuleValue.Path(kv.Value) : RuleValue.String(kv.Value));
    }
}
