namespace Winche.Rules.Evaluation;

/// <summary>
/// The equality + ordering port the engine uses for value comparisons. A consumer may supply an
/// implementation whose semantics match its own query/storage engine, so the query analyzer's
/// "provably safe" proof uses the same comparison rules the consumer will execute at runtime.
/// </summary>
public interface IRuleValueComparer
{
    /// <summary>
    /// Whether <paramref name="a"/> and <paramref name="b"/> are equal (drives <c>==</c>/<c>!=</c>/<c>in</c>).
    /// </summary>
    bool AreEqual(RuleValue a, RuleValue b);

    /// <summary>
    /// Orders the two values. 
    /// Returns <c>false</c> when they are un-orderable (drives <c>&lt; &lt;= &gt; &gt;=</c>).
    /// </summary>
    bool TryCompare(RuleValue a, RuleValue b, out int result);
}

/// <summary>The engine's built-in comparer: delegates to <see cref="RuleValue"/>'s own semantics.</summary>
public sealed class DefaultRuleValueComparer : IRuleValueComparer
{
    /// <summary>Shared instance.</summary>
    public static readonly DefaultRuleValueComparer Instance = new();

    /// <inheritdoc/>
    public bool AreEqual(RuleValue a, RuleValue b) => a.Equals(b);

    /// <inheritdoc/>
    public bool TryCompare(RuleValue a, RuleValue b, out int result) => a.TryCompareTo(b, out result);
}
