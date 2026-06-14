using Microsoft.Extensions.DependencyInjection;
using Winche.Rules.DependencyInjection;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddWincheRules_RegistersResolvableEngine()
    {
        var services = new ServiceCollection();
        services.AddWincheRules(opts =>
            opts.WithRuleset(ruleset => ruleset.Match("docs/{id}", m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true))))));

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetRequiredService<RuleEngine>());
    }

    [Fact]
    public async Task AddWincheRules_MergesMultipleRulesets()
    {
        var services = new ServiceCollection();
        services.AddWincheRules(opts =>
            opts.WithRuleset(ruleset => ruleset.Match("a/{id}", m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true))))));
        services.AddWincheRules(opts =>
            opts.WithRuleset(ruleset => ruleset.Match("b/{id}", m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true))))));

        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<RuleEngine>();

        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "a/1", new RuleRequest()));
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "b/1", new RuleRequest()));
    }
}
