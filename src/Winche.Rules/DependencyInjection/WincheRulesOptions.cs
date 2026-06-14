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

    internal void Configure(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(Ruleset);
        services.AddSingleton(Ruleset);
        services.TryAddSingleton(sp => new RuleEngine(RuleSet.Merge(sp.GetServices<RuleSet>()), Comparer));
    }
}