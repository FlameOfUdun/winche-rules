namespace Winche.Rules.Matching;

/// <summary>Flattens a match tree into (accumulated path pattern, allow rule) pairs.</summary>
internal static class RuleFlatten
{
    public static IEnumerable<(IReadOnlyList<PathSegment> Pattern, AllowRule Allow)> Flatten(
        IReadOnlyList<MatchBlock> blocks, IReadOnlyList<PathSegment> prefix)
    {
        foreach (var block in blocks)
        {
            IReadOnlyList<PathSegment> full =
                prefix.Count == 0 ? PathPattern.Parse(block.Path) : [.. prefix, .. PathPattern.Parse(block.Path)];

            foreach (var allow in block.Allow)
                yield return (full, allow);

            foreach (var nested in Flatten(block.Matches, full))
                yield return nested;
        }
    }
}
