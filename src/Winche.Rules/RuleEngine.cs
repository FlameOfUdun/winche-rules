using Winche.Rules.Evaluation;
using Winche.Rules.Querying;

namespace Winche.Rules;

/// <summary>
/// An instantiable rules engine that reads its <see cref="RuleSet"/> from an <see cref="IRuleRepository"/>
/// on every evaluation and applies a fixed <see cref="IRuleValueComparer"/> over the per-request
/// <see cref="RuleRequest"/>. Because the ruleset is read per call, a repository that swaps its ruleset
/// makes rules hot-swappable without rebuilding the engine. The comparer is applied internally, so callers
/// cannot forget it.
/// </summary>
public sealed class RuleEngine
{
    private readonly IRuleRepository _repository;
    private readonly IRuleValueComparer _comparer;

    /// <summary>
    /// Creates an engine that reads its ruleset from <paramref name="repository"/> on every evaluation;
    /// <paramref name="comparer"/> defaults to <see cref="DefaultRuleValueComparer"/>.
    /// </summary>
    public RuleEngine(IRuleRepository repository, IRuleValueComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _comparer = comparer ?? DefaultRuleValueComparer.Instance;
    }

    /// <summary>
    /// Creates an engine over a fixed <paramref name="ruleset"/> (not hot-swappable);
    /// <paramref name="comparer"/> defaults to <see cref="DefaultRuleValueComparer"/>.
    /// </summary>
    public RuleEngine(RuleSet ruleset, IRuleValueComparer? comparer = null)
        : this(new StaticRuleRepository(ruleset), comparer)
    {
    }

    /// <summary>Whether <paramref name="operation"/> on the document at <paramref name="documentPath"/> is allowed.</summary>
    public Task<bool> AllowsAsync(RuleOperation operation, string documentPath, RuleRequest request, CancellationToken ct = default)
        => RulesetEvaluator.AllowsAsync(_repository.Current, operation, documentPath, request.ToContext(_comparer), ct);

    /// <summary>Whether the list <paramref name="query"/> is provably safe under the read rules.</summary>
    public bool Allows(QueryConstraints query, RuleRequest request)
        => QueryAnalyzer.Allows(_repository.Current, query, request.ToContext(_comparer));
}
