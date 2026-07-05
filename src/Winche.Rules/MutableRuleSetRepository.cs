namespace Winche.Rules;

/// <summary>
/// The default <see cref="IMutableRuleSetRepository"/>: holds a single <see cref="RuleSet"/> in a
/// <see langword="volatile"/> field and replaces it with a lock-free atomic reference write. Because a
/// <see cref="RuleSet"/> is immutable, an in-flight evaluation keeps the snapshot it started with while the
/// next evaluation observes the newly-set ruleset.
/// </summary>
/// <remarks>
/// Creates a repository seeded with <paramref name="initial"/>. When <paramref name="initial"/> is
/// <see langword="null"/> (the default), it starts with an empty, deny-all ruleset — useful when the rules
/// are loaded later via <see cref="Update"/>.
/// </remarks>
/// <param name="initial">The initial ruleset, or <see langword="null"/> for an empty deny-all ruleset.</param>
public sealed class MutableRuleSetRepository(RuleSet? initial = null) : IMutableRuleSetRepository
{
    private volatile RuleSet _current = initial ?? new([]);

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
