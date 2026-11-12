using Winche.Rules.Matching;

namespace Winche.Rules;

/// <summary>
/// One structural problem in a <see cref="RuleSet"/>. <see cref="Path"/> is the offending match block's full
/// (parent-joined) path when known, or <see langword="null"/> for a problem with the ruleset as a whole.
/// </summary>
public sealed record RuleValidationIssue(string? Path, string Message);

/// <summary>
/// Structural checks for a <see cref="RuleSet"/> that did not come from <see cref="Text.RuleText.ParseRuleSet"/>
/// (for example, rules stored as JSON): every match path parses, a match ending in a recursive wildcard has no
/// nested matches, and no match path (including its parents') has more than one recursive wildcard.
/// </summary>
public static class RuleSetValidation
{
    /// <summary>Returns every problem found; an empty list means the ruleset is valid.</summary>
    public static IReadOnlyList<RuleValidationIssue> Validate(RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        var issues = new List<RuleValidationIssue>();
        Walk(ruleSet.Matches ?? [], [], "", issues);
        return issues;
    }

    private static void Walk(
        IReadOnlyList<MatchBlock> blocks, IReadOnlyList<PathSegment> prefix, string prefixText, List<RuleValidationIssue> issues)
    {
        foreach (var block in blocks)
        {
            if (block is null)
            {
                issues.Add(new(prefixText.Length == 0 ? null : prefixText, "match block is empty."));
                continue;
            }

            var label = prefixText.Length == 0 ? block.Path ?? "" : $"{prefixText}/{block.Path}";
            var nested = block.Matches ?? [];

            IReadOnlyList<PathSegment> own;
            try
            {
                own = PathPattern.Parse(block.Path ?? "");
            }
            catch (ArgumentException ex)
            {
                issues.Add(new(label, ex.Message));
                Walk(nested, prefix, label, issues);   // still report problems further down
                continue;
            }

            IReadOnlyList<PathSegment> full = [.. prefix, .. own];
            if (nested.Count > 0 && own.Count > 0 && own[^1] is RecursiveSegment)
                issues.Add(new(label,
                    $"match '{block.Path}' ends in a recursive wildcard and cannot contain nested matches; " +
                    "nested rules would be unreachable."));
            if (PathPattern.RecursiveCount(full) > 1)
                issues.Add(new(label,
                    $"match '{label}' has more than one recursive wildcard (including its parents'); it could never match."));

            Walk(nested, full, label, issues);
        }
    }
}
