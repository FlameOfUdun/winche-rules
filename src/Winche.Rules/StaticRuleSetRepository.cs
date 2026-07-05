namespace Winche.Rules;

/// <summary>
/// An immutable <see cref="IRuleSetRepository"/> over a fixed <see cref="RuleSet"/>. Use it to build a frozen
/// <see cref="RuleEngine"/> whose rules never change at runtime.
/// </summary>
public sealed class StaticRuleSetRepository : IRuleSetRepository
{
    /// <summary>Creates a repository that always returns <paramref name="ruleset"/>.</summary>
    /// <param name="ruleset">The fixed ruleset. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ruleset"/> is <see langword="null"/>.</exception>
    public StaticRuleSetRepository(RuleSet ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        Current = ruleset;
    }

    /// <inheritdoc/>
    public RuleSet Current { get; }
}
