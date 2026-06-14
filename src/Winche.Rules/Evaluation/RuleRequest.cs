using Winche.Rules;

namespace Winche.Rules.Evaluation;

/// <summary>
/// The per-request inputs to a rule evaluation: the existing <see cref="Resource"/>, the
/// <see cref="Request"/> map, path-pattern <see cref="Params"/>, and the <see cref="Provider"/> source
/// for <c>get()</c>/<c>exists()</c>. The comparer is supplied by the engine, not here — so consumers
/// cannot forget to thread it.
/// </summary>
public sealed class RuleRequest
{
    /// <summary>The existing resource as a map (its field layout is entirely up to you), or <see cref="RuleValue.Null"/>.</summary>
    public RuleValue Resource { get; init; } = RuleValue.Null;

    /// <summary>The request map (<c>auth</c>, <c>resource</c>, <c>time</c>, <c>method</c>, …), or <see cref="RuleValue.Null"/>.</summary>
    public RuleValue Request { get; init; } = RuleValue.Null;

    /// <summary>Path-pattern captures (e.g. <c>userId</c> from <c>users/{userId}</c>).</summary>
    public IReadOnlyDictionary<string, RuleValue> Params { get; init; } = RuleContext.NoParams;

    /// <summary>Backs <c>get()</c>/<c>exists()</c>; may be <c>null</c> when no rule uses them.</summary>
    public IRuleResourceProvider? Provider { get; init; }

    /// <summary>Builds the engine-internal <see cref="RuleContext"/>, injecting the engine's comparer.</summary>
    internal RuleContext ToContext(IRuleValueComparer comparer) => new()
    {
        Resource = Resource,
        Request = Request,
        Params = Params,
        Provider = Provider,
        Comparer = comparer,
    };
}
