using Winche.Rules.Evaluation;
using Winche.Rules.Querying;

namespace Winche.Rules;

/// <summary>
/// An instantiable rules engine that reads its <see cref="RuleSet"/> from an <see cref="IRuleSetRepository"/>
/// on every evaluation of a per-request <see cref="RuleRequest"/>. Because the ruleset is read per call, a repository
/// that swaps its ruleset makes rules hot-swappable without rebuilding the engine. Values compare as the database
/// orders them (<see cref="RuleValues"/>).
/// </summary>
public sealed class RuleEngine
{
    private readonly IRuleSetRepository _repository;

    /// <summary>Creates an engine that reads its ruleset from <paramref name="repository"/> on every evaluation.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="repository"/> is <see langword="null"/>.</exception>
    public RuleEngine(IRuleSetRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>Whether <paramref name="operation"/> on the document at <paramref name="documentPath"/> is allowed.</summary>
    public Task<bool> AllowsAsync(RuleOperation operation, string documentPath, RuleRequest request, CancellationToken ct = default)
        => RuleSetEvaluator.AllowsAsync(_repository.Current, operation, documentPath, request.ToContext(), ct);

    /// <summary>Whether the list <paramref name="query"/> is provably safe under the read rules.</summary>
    public bool Allows(QueryConstraints query, RuleRequest request)
        => QueryAnalyzer.Allows(_repository.Current, query, request.ToContext());
}
