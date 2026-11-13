using System.Collections.ObjectModel;

namespace Winche.Rules.Evaluation;

/// <summary>
/// The data a condition is evaluated against. <see cref="Resource"/> and <see cref="Request"/> are the
/// root maps the expression navigates (e.g. <c>resource.data.ownerId</c>, <c>request.auth.uid</c>,
/// <c>request.resource.data.x</c>, <c>request.method</c>). The consumer builds them from its own types.
/// </summary>
public sealed class RuleContext
{
    /// <summary>
    /// The empty parameter set.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, Value> NoParams = new ReadOnlyDictionary<string, Value>(new Dictionary<string, Value>());

    /// <summary>
    /// The existing resource as a map (its field layout is entirely up to you), or <see cref="RuleValues.Null"/>.
    /// </summary>
    public Value Resource { get; init; } = RuleValues.Null;

    /// <summary>
    /// The request (a map with <c>auth</c>, <c>resource</c>, <c>time</c>, <c>method</c>, …).
    /// </summary>
    public Value Request { get; init; } = RuleValues.Null;

    /// <summary>
    /// Path-pattern captures (e.g. <c>userId</c> from <c>users/{userId}</c>).
    /// </summary>
    public IReadOnlyDictionary<string, Value> Params { get; init; } = NoParams;

    /// <summary>
    /// Backs <c>get()</c>/<c>exists()</c>; may be <c>null</c> when no rule uses them.
    /// </summary>
    public IRuleResourceProvider? Provider { get; init; }
}
