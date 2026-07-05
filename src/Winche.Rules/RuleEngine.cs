using Winche.Rules.Evaluation;
using Winche.Rules.Querying;

namespace Winche.Rules;

/// <summary>
/// An instantiable rules engine that reads its <see cref="RuleSet"/> from an <see cref="IRuleSetRepository"/>
/// on every evaluation and applies a fixed <see cref="IRuleValueComparer"/> over the per-request
/// <see cref="RuleRequest"/>. Because the ruleset is read per call, a repository that swaps its ruleset
/// makes rules hot-swappable without rebuilding the engine. The comparer is applied internally, so callers
/// cannot forget it.
/// </summary>
public sealed class RuleEngine
{
    private readonly IRuleSetRepository _repository;
    private readonly IRuleValueComparer _comparer;

    /// <summary>
    /// Creates an engine that reads its ruleset from <paramref name="repository"/> on every evaluation and
    /// compares values with <paramref name="comparer"/>.
    /// </summary>
    /// <param name="repository">The source of the current ruleset. Must not be <see langword="null"/>.</param>
    /// <param name="comparer">The comparer for equality/ordering. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repository"/> or <paramref name="comparer"/> is <see langword="null"/>.</exception>
    public RuleEngine(IRuleSetRepository repository, IRuleValueComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(comparer);
        _repository = repository;
        _comparer = comparer;
    }

    /// <summary>Whether <paramref name="operation"/> on the document at <paramref name="documentPath"/> is allowed.</summary>
    public Task<bool> AllowsAsync(RuleOperation operation, string documentPath, RuleRequest request, CancellationToken ct = default)
        => RuleSetEvaluator.AllowsAsync(_repository.Current, operation, documentPath, request.ToContext(_comparer), ct);

    /// <summary>Whether the list <paramref name="query"/> is provably safe under the read rules.</summary>
    public bool Allows(QueryConstraints query, RuleRequest request)
        => QueryAnalyzer.Allows(_repository.Current, query, request.ToContext(_comparer));
}
