namespace Winche.Rules;

/// <summary>
/// The default <see cref="IMutableRuleRepository"/>: holds a single <see cref="RuleSet"/> in a
/// <see langword="volatile"/> field and replaces it with a lock-free atomic reference write. Because a
/// <see cref="RuleSet"/> is immutable, an in-flight evaluation keeps the snapshot it started with while the
/// next evaluation observes the newly-set ruleset.
/// </summary>
public sealed class MutableRuleRepository : IMutableRuleRepository
{
    private volatile RuleSet _current;

    /// <summary>Creates a repository seeded with <paramref name="initial"/>.</summary>
    /// <param name="initial">The initial ruleset. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="initial"/> is <see langword="null"/>.</exception>
    public MutableRuleRepository(RuleSet initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _current = initial;
    }

    /// <inheritdoc/>
    public RuleSet Current => _current;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ruleset"/> is <see langword="null"/>.</exception>
    public void Update(RuleSet ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        _current = ruleset;
    }
}
