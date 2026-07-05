namespace Winche.Rules;

/// <summary>
/// An immutable <see cref="IRuleRepository"/> over a fixed <see cref="RuleSet"/>. Backs the
/// ruleset-constructed <see cref="RuleEngine"/> so that a <see cref="RuleEngine"/> built from a
/// <see cref="RuleSet"/> behaves exactly as it did before the repository seam existed.
/// </summary>
internal sealed class StaticRuleRepository : IRuleRepository
{
    public StaticRuleRepository(RuleSet ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        Current = ruleset;
    }

    public RuleSet Current { get; }
}
