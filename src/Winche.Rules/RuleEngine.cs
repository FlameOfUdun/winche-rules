using Winche.Rules.Evaluation;
using Winche.Rules.Querying;

namespace Winche.Rules;

/// <summary>
/// An instantiable rules engine that captures the two app-lifetime constants — the compiled
/// <see cref="RuleSet"/> and the <see cref="IRuleValueComparer"/> — and evaluates over the per-request
/// <see cref="RuleRequest"/>. The comparer is applied internally, so callers cannot forget it.
/// </summary>
public sealed class RuleEngine
{
    private readonly RuleSet _ruleset;
    private readonly IRuleValueComparer _comparer;

    /// <summary>Creates an engine over <paramref name="ruleset"/>; <paramref name="comparer"/> defaults to <see cref="DefaultRuleValueComparer"/>.</summary>
    public RuleEngine(RuleSet ruleset, IRuleValueComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        _ruleset = ruleset;
        _comparer = comparer ?? DefaultRuleValueComparer.Instance;
    }

    /// <summary>Whether <paramref name="operation"/> on the document at <paramref name="documentPath"/> is allowed.</summary>
    public Task<bool> AllowsAsync(RuleOperation operation, string documentPath, RuleRequest request, CancellationToken ct = default)
        => RulesetEvaluator.AllowsAsync(_ruleset, operation, documentPath, request.ToContext(_comparer), ct);

    /// <summary>Whether the list <paramref name="query"/> is provably safe under the read rules.</summary>
    public bool Allows(QueryConstraints query, RuleRequest request)
        => QueryAnalyzer.Allows(_ruleset, query, request.ToContext(_comparer));
}
