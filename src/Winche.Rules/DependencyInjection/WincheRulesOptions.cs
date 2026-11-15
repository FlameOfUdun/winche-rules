using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Winche.Rules.Evaluation;

namespace Winche.Rules.DependencyInjection;

/// <summary>
/// Options for configuring the Winche.Rules engine in dependency injection.
/// </summary>
public sealed record WincheRulesOptions
{
    /// <summary>
    /// Gets the <see cref="IRuleSetRepository"/> to back the engine with, if any.
    /// </summary>
    internal IRuleSetRepository? Repository { get; private set; }

    /// <summary>
    /// Backs the engine with a custom <see cref="IRuleSetRepository"/> (for example, one that self-refreshes
    /// from a file or database). If the repository also implements <see cref="IMutableRuleSetRepository"/>, it
    /// is registered as the write side too, so its rules can be hot-swapped.
    /// </summary>
    /// <param name="value">The repository to read rules from.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithRuleSetRepository(IRuleSetRepository value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Repository = value;
        return this;
    }

    /// <summary>
    /// Backs the engine with a <see cref="MutableRuleSetRepository"/> seeded with <paramref name="value"/>,
    /// making the rules hot-swappable at runtime. This is the default when no repository is configured.
    /// </summary>
    /// <param name="value">The initial ruleset, or <see langword="null"/> for an empty deny-all ruleset filled later via <see cref="IMutableRuleSetRepository.Update"/>.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithMutableRuleSetRepository(RuleSet? value = null)
    {
        Repository = new MutableRuleSetRepository(value);
        return this;
    }

    /// <summary>
    /// Backs the engine with a <see cref="StaticRuleSetRepository"/> that always returns <paramref name="value"/>.
    /// Use this when the ruleset is fixed at startup; the resulting engine is frozen (no hot-swap).
    /// </summary>
    /// <param name="value">The fixed ruleset. Must not be <see langword="null"/>.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithStaticRuleSetRepository(RuleSet value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Repository = new StaticRuleSetRepository(value);
        return this;
    }

    /// <summary>
    /// Non-keyed registration: registers the configured repository (defaulting to an empty
    /// <see cref="MutableRuleSetRepository"/>) and a single <see cref="RuleEngine"/> over them.
    /// If the repository is mutable, its <see cref="IMutableRuleSetRepository"/> write side is registered too.
    /// </summary>
    internal void Configure(IServiceCollection services)
    {
        var repository = Repository ?? new MutableRuleSetRepository();

        services.AddSingleton(repository);
        if (repository is IMutableRuleSetRepository mutable)
            services.AddSingleton(mutable);
        services.TryAddSingleton<RuleEngine>();
    }

    /// <summary>
    /// Keyed registration: registers one isolated <c>(repository, engine)</c> unit under
    /// <paramref name="serviceKey"/> — its own ruleset. Keys are never merged. The engine
    /// is built by a keyed factory so it resolves this key's dependencies; a mutable repository also exposes
    /// its <see cref="IMutableRuleSetRepository"/> write side under the key for hot-swapping.
    /// </summary>
    internal void ConfigureKeyed(IServiceCollection services, object serviceKey)
    {
        var repository = Repository ?? new MutableRuleSetRepository();

        services.AddKeyedSingleton<IRuleSetRepository>(serviceKey, (_, _) => repository);
        if (repository is IMutableRuleSetRepository mutable)
            services.AddKeyedSingleton<IMutableRuleSetRepository>(serviceKey, (_, _) => mutable);
        services.AddKeyedSingleton<RuleEngine>(serviceKey, (sp, key) =>
            new RuleEngine(sp.GetRequiredKeyedService<IRuleSetRepository>(key)));
    }
}
