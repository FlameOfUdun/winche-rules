using Winche.Rules.Evaluation;
using Winche.Rules.Matching;

namespace Winche.Rules;

/// <summary>
/// Evaluates a <see cref="RuleSet"/> for a single-document operation (get/create/update/delete):
/// a document is authorized if ANY matching <c>allow</c> rule for the operation has a
/// condition that evaluates true; otherwise default-deny. Path-pattern captures are bound as variables for
/// the condition. (Use the query analyzer — not this — for <c>list</c>.)
/// </summary>
public static class RulesetEvaluator
{
    /// <summary>
    /// Whether <paramref name="operation"/> on the document at <paramref name="documentPath"/> is allowed.
    /// </summary>
    public static async Task<bool> AllowsAsync(
        RuleSet ruleset,
        RuleOperation operation,
        string documentPath,
        RuleContext context,
        CancellationToken ct = default)
    {
        foreach (var (pattern, allow) in RuleFlatten.Flatten(ruleset.Matches, []))
        {
            if (!allow.Operations.Contains(operation)) continue;
            if (!PathPattern.TryMatch(pattern, documentPath, out var captures)) continue;

            foreach (var (k, v) in context.Params) captures.TryAdd(k, v);   // path captures take precedence
            var scoped = new RuleContext
            {
                Resource = context.Resource,
                Request = context.Request,
                Params = captures,
                Provider = context.Provider,
                Comparer = context.Comparer,
            };

            if (await RuleEvaluator.EvaluateAsync(allow.Condition, scoped, ct).ConfigureAwait(false))
                return true;
        }

        return false;
    }
}
