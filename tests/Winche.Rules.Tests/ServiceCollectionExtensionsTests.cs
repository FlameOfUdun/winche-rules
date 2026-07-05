using Microsoft.Extensions.DependencyInjection;
using Winche.Rules.DependencyInjection;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class ServiceCollectionExtensionsTests
{
    private sealed class AlwaysEqual : IRuleValueComparer
    {
        public bool AreEqual(RuleValue a, RuleValue b) => true;
        public bool TryCompare(RuleValue a, RuleValue b, out int r) { r = 0; return true; }
    }

    private static RuleSet AllowRead(string path) => RuleSetBuilder.Build(r =>
        r.Match(path, m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true)))));

    [Fact]
    public void AddWincheRules_RegistersResolvableEngine()
    {
        var services = new ServiceCollection();
        services.AddWincheRules(opts => opts.WithMutableRuleSetRepository(AllowRead("docs/{id}")));

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetRequiredService<RuleEngine>());
    }

    [Fact]
    public async Task AddWincheRules_NonKeyed_ResolvesAndEvaluates()
    {
        var services = new ServiceCollection();
        services.AddWincheRules(opts => opts.WithMutableRuleSetRepository(AllowRead("a/{id}")));

        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<RuleEngine>();

        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "a/1", new RuleRequest()));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "b/1", new RuleRequest()));
    }

    [Fact]
    public async Task AddWincheRules_Keyed_IsolatesEnginesPerType()
    {
        var services = new ServiceCollection();
        services.AddWincheRules("docs", o => o.WithMutableRuleSetRepository(AllowRead("docs/{id}")));
        services.AddWincheRules("orders", o => o.WithMutableRuleSetRepository(AllowRead("orders/{id}")));

        using var sp = services.BuildServiceProvider();
        var docs = sp.GetRequiredKeyedService<RuleEngine>("docs");
        var orders = sp.GetRequiredKeyedService<RuleEngine>("orders");

        Assert.True(await docs.AllowsAsync(RuleOperation.Get, "docs/1", new RuleRequest()));
        Assert.False(await docs.AllowsAsync(RuleOperation.Get, "orders/1", new RuleRequest()));   // no cross-key merge
        Assert.True(await orders.AllowsAsync(RuleOperation.Get, "orders/1", new RuleRequest()));
    }

    [Fact]
    public async Task AddWincheRules_Keyed_HotSwapPerTypeIndependent()
    {
        var services = new ServiceCollection();
        services.AddWincheRules("docs", o => o.WithMutableRuleSetRepository());   // empty deny-all initially
        services.AddWincheRules("orders", o => o.WithMutableRuleSetRepository(AllowRead("orders/{id}")));

        using var sp = services.BuildServiceProvider();
        var docsEngine = sp.GetRequiredKeyedService<RuleEngine>("docs");
        var docsRepo = sp.GetRequiredKeyedService<IMutableRuleSetRepository>("docs");

        Assert.False(await docsEngine.AllowsAsync(RuleOperation.Get, "docs/1", new RuleRequest()));

        docsRepo.Update(AllowRead("docs/{id}"));

        Assert.True(await docsEngine.AllowsAsync(RuleOperation.Get, "docs/1", new RuleRequest()));

        var orders = sp.GetRequiredKeyedService<RuleEngine>("orders");
        Assert.True(await orders.AllowsAsync(RuleOperation.Get, "orders/1", new RuleRequest()));   // untouched by docs update
    }

    [Fact]
    public async Task AddWincheRules_Keyed_UsesPerKeyComparer()
    {
        var rule = RuleSetBuilder.Build(r => r.Match("docs/{id}", m => m.Allow(RuleOperations.Read,
            new CompareExpression(new MemberExpression(new VarExpression("resource"), "x"),
                ComparisonOperator.Eq, new LiteralExpression(RuleValue.String("Y"))))));

        var services = new ServiceCollection();
        services.AddWincheRules("loose", o => o.WithMutableRuleSetRepository(rule).WithRuleValueComparer(new AlwaysEqual()));
        services.AddWincheRules("strict", o => o.WithMutableRuleSetRepository(rule));   // default comparer

        using var sp = services.BuildServiceProvider();
        var req = new RuleRequest
        {
            Resource = RuleValue.Map(new Dictionary<string, RuleValue> { ["x"] = RuleValue.String("Z") }),
        };

        Assert.True(await sp.GetRequiredKeyedService<RuleEngine>("loose").AllowsAsync(RuleOperation.Get, "docs/1", req));
        Assert.False(await sp.GetRequiredKeyedService<RuleEngine>("strict").AllowsAsync(RuleOperation.Get, "docs/1", req));
    }

    [Fact]
    public void AddWincheRules_Keyed_Static_HasNoWriteSide()
    {
        var services = new ServiceCollection();
        services.AddWincheRules("frozen", o => o.WithStaticRuleSetRepository(AllowRead("x/{id}")));

        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredKeyedService<RuleEngine>("frozen"));          // engine resolves
        Assert.Null(sp.GetKeyedService<IMutableRuleSetRepository>("frozen"));      // but it is frozen — no write side
    }
}
