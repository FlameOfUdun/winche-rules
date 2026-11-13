namespace Winche.Rules.Matching;

internal sealed record FlannetedRule(IReadOnlyList<PathSegment> Pattern, AllowRule Allow);

/// <summary>
/// Flattens a match tree into (accumulated path pattern, allow rule) pairs.
/// </summary>
internal static class RuleFlatten
{
    /// <summary>
    /// Throws <see cref="ArgumentException"/> when a block's full path (its parents' paths and its own) has
    /// more than one recursive wildcard — such a rule could never match.
    /// </summary>
    public static void Validate(IReadOnlyList<MatchBlock> blocks, IReadOnlyList<PathSegment>? prefix = null, string? prefixText = null)
    {
        foreach (var block in blocks)
        {
            var own = RulePath.Parse(block.Path);
            IReadOnlyList<PathSegment> full = prefix is null ? own : [.. prefix, .. own];
            var fullText = prefixText is null ? block.Path : $"{prefixText}/{block.Path}";
            if (RulePath.RecursiveCount(full) > 1)
                throw new ArgumentException($"match '{fullText}' has more than one recursive wildcard (including its parents'); it could never match.");
            Validate(block.Matches, full, fullText);
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RuleSet, IReadOnlyList<FlannetedRule>> Cache = new();

    /// <summary>
    /// The ruleset's rules with their full patterns and with calls to its functions expanded (cached per ruleset
    /// instance: rulesets are immutable).
    /// </summary>
    public static IReadOnlyList<FlannetedRule> Flatten(RuleSet ruleSet) =>
        Cache.GetValue(ruleSet, rs => [.. Flatten(rs.Matches ?? [], [], FunctionScope.Empty.With(rs.Functions))]);

    private static IEnumerable<FlannetedRule> Flatten(IReadOnlyList<MatchBlock> blocks, IReadOnlyList<PathSegment> prefix, FunctionScope outer)
    {
        foreach (var block in blocks)
        {
            IReadOnlyList<PathSegment> full =
                prefix.Count == 0 ? RulePath.Parse(block.Path) : [.. prefix, .. RulePath.Parse(block.Path)];
            var scope = outer.With(block.Functions);

            foreach (var allow in block.Allow)
                yield return new FlannetedRule(full, ReferenceEquals(scope, FunctionScope.Empty)
                    ? allow
                    : allow with { Condition = FunctionInliner.Inline(allow.Condition, scope) });

            foreach (var nested in Flatten(block.Matches ?? [], full, scope))
                yield return nested;
        }
    }

    public static IEnumerable<FlannetedRule> Flatten(IReadOnlyList<MatchBlock> blocks, IReadOnlyList<PathSegment> prefix)
    {
        foreach (var block in blocks)
        {
            IReadOnlyList<PathSegment> full =
                prefix.Count == 0 ? RulePath.Parse(block.Path) : [.. prefix, .. RulePath.Parse(block.Path)];

            foreach (var allow in block.Allow)
                yield return new FlannetedRule(full, allow);

            foreach (var nested in Flatten(block.Matches, full))
                yield return nested;
        }
    }
}
