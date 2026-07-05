using Microsoft.Extensions.DependencyInjection;

namespace Winche.Rules.DependencyInjection;

/// <summary>
/// DI registration for the Winche.Rules engine.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds and configures the Winche.Rules engine.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="configure">The action to configure the <see cref="WincheRulesOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddWincheRules(this IServiceCollection services, Action<WincheRulesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var options = new WincheRulesOptions();
        configure(options);
        options.Configure(services);
        return services;
    }

    /// <summary>
    /// Adds an isolated, hot-swappable Winche.Rules engine under <paramref name="serviceKey"/>. Each key
    /// gets its own ruleset, its own comparer, and its own <see cref="IMutableRuleRepository"/>; keys are
    /// never merged with one another. Resolve the engine with <c>[FromKeyedServices(key)] RuleEngine</c>,
    /// and reload that type's rules at runtime by resolving <c>[FromKeyedServices(key)] IMutableRuleRepository</c>
    /// and calling <see cref="IMutableRuleRepository.Update"/> — no restart, and other keys are unaffected.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add the services to.</param>
    /// <param name="serviceKey">The key that identifies this engine (typically the resource type).</param>
    /// <param name="configure">The action to configure the <see cref="WincheRulesOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddWincheRules(this IServiceCollection services, object serviceKey, Action<WincheRulesOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(serviceKey);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new WincheRulesOptions();
        configure(options);
        options.ConfigureKeyed(services, serviceKey);
        return services;
    }
}
