namespace Winche.Rules;

/// <summary>
/// Supplies the <see cref="RuleSet"/> a <see cref="RuleEngine"/> evaluates against. The engine reads
/// <see cref="Current"/> on every evaluation, so an implementation that replaces its ruleset at runtime
/// makes rules hot-swappable without rebuilding the engine.
/// </summary>
public interface IRuleRepository
{
    /// <summary>
    /// The current ruleset. Implementations must make this an O(1), in-memory read; the returned
    /// <see cref="RuleSet"/> is immutable, so a caller may safely hold it for the duration of an evaluation.
    /// </summary>
    RuleSet Current { get; }
}

/// <summary>
/// An <see cref="IRuleRepository"/> whose ruleset can be replaced at runtime. Reload/admin code depends on
/// this write-side interface; the engine only ever depends on the read-only <see cref="IRuleRepository"/>,
/// so a component that merely evaluates rules cannot mutate them.
/// </summary>
public interface IMutableRuleRepository : IRuleRepository
{
    /// <summary>
    /// Atomically replaces the current ruleset. Safe to call concurrently with in-flight evaluations: an
    /// evaluation already running keeps the snapshot it started with, and the next one observes
    /// <paramref name="ruleset"/>.
    /// </summary>
    /// <param name="ruleset">The new ruleset. Must not be <see langword="null"/>.</param>
    void Update(RuleSet ruleset);
}
