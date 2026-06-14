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
        var options = new WincheRulesOptions();
        configure(options);
        options.Configure(services);
        return services;
    }
}
