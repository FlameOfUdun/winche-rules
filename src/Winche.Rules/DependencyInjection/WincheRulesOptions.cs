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
    /// Gets the <see cref="IRuleValueComparer"/> to use for rule evaluation.
    /// </summary>
    internal IRuleValueComparer? Comparer { get; private set; }

    /// <summary>
    /// Gets the <see cref="RuleSet"/> to use for rule evaluation.
    /// </summary>
    internal RuleSet? Ruleset { get; private set; }

    /// <summary>
    /// Gets the custom <see cref="IRuleRepository"/> to back the engine with, if any.
    /// </summary>
    internal IRuleRepository? Repository { get; private set; }

    /// <summary>
    /// Sets the <see cref="IRuleValueComparer"/> to use for rule evaluation.
    /// </summary>
    /// <param name="comparer">The comparer to use for rule evaluation.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithComparer(IRuleValueComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        Comparer = comparer;
        return this;
    }

    /// <summary>
    /// Sets the <see cref="RuleSet"/> to use for rule evaluation.
    /// </summary>
    /// <param name="ruleset">The ruleset to use for rule evaluation.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithRuleset(RuleSet ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        Ruleset = ruleset;
        return this;
    }

    /// <summary>
    /// Sets the <see cref="RuleSet"/> to use for rule evaluation by building it using a <see cref="RulesetBuilder"/>.
    /// </summary>
    /// <param name="build">The action to build the ruleset.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithRuleset(Action<RulesetBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        Ruleset = RulesetBuilder.Build(build);
        return this;
    }

    /// <summary>
    /// Backs the engine with a custom <see cref="IRuleRepository"/> (for example, one that self-refreshes
    /// from a file or database). Only honored by the keyed <c>AddWincheRules</c> overload, where it takes
    /// precedence over <see cref="WithRuleset(RuleSet)"/>. If the repository also implements
    /// <see cref="IMutableRuleRepository"/>, it is registered under that key as the write side too.
    /// </summary>
    /// <param name="repository">The repository to read rules from.</param>
    /// <returns>The current <see cref="WincheRulesOptions"/> instance.</returns>
    public WincheRulesOptions WithRepository(IRuleRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Repository = repository;
        return this;
    }

    /// <summary>
    /// Non-keyed registration: registers the configured <see cref="RuleSet"/> and a single
    /// <see cref="RuleEngine"/> that merges every registered ruleset. Behavior is unchanged from before the
    /// repository seam existed — hot-swap is opt-in via the keyed overload or <see cref="WithRepository"/>.
    /// </summary>
    internal void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(Ruleset);
        services.AddSingleton(Ruleset);
        services.TryAddSingleton(sp => new RuleEngine(RuleSet.Merge(sp.GetServices<RuleSet>()), Comparer));
    }

    /// <summary>
    /// Keyed registration: registers one isolated, hot-swappable <c>(repository, engine)</c> unit under
    /// <paramref name="serviceKey"/> — its own ruleset, its own comparer, and its own
    /// <see cref="IMutableRuleRepository"/>. Keys are never merged with one another.
    /// </summary>
    internal void ConfigureKeyed(IServiceCollection services, object serviceKey)
    {
        var comparer = Comparer;

        if (Repository is not null)
        {
            var repository = Repository;
            services.AddKeyedSingleton<IRuleRepository>(serviceKey, (_, _) => repository);
            if (repository is IMutableRuleRepository mutable)
                services.AddKeyedSingleton<IMutableRuleRepository>(serviceKey, (_, _) => mutable);
        }
        else
        {
            ArgumentNullException.ThrowIfNull(Ruleset);
            var ruleset = Ruleset;
            services.AddKeyedSingleton(serviceKey, (_, _) => new MutableRuleRepository(ruleset));
            services.AddKeyedSingleton<IRuleRepository>(serviceKey, (sp, k) => sp.GetRequiredKeyedService<MutableRuleRepository>(k!));
            services.AddKeyedSingleton<IMutableRuleRepository>(serviceKey, (sp, k) => sp.GetRequiredKeyedService<MutableRuleRepository>(k!));
        }

        services.AddKeyedSingleton(serviceKey, (sp, k) => new RuleEngine(sp.GetRequiredKeyedService<IRuleRepository>(k!), comparer));
    }
}
