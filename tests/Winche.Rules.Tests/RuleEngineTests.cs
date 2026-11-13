using Winche.Values;
using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleEngineTests
{
    private static RuleSet OwnerRule() => RuleSetBuilder.Build(r =>
        r.Match("docs/{id}", m => m.Allow(RuleOperations.Read,
            new CompareExpression(
                new MemberExpression(new MemberExpression(new VarExpression("resource"), "data"), "x"),
                ComparisonOperator.Eq,
                new LiteralExpression(new StringValue("Y"))))));

    private static RuleRequest WithX(string value) => new()
    {
        Resource = new MapValue(new Dictionary<string, Value>
        {
            ["data"] = new MapValue(new Dictionary<string, Value> { ["x"] = new StringValue(value) }),
        }),
    };

    [Fact]
    public async Task AllowsAsync_ComparesWithTheSharedValueOrder()
    {
        var engine = new RuleEngine(new StaticRuleSetRepository(OwnerRule()));
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", WithX("Y")));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", WithX("Z")));
    }

    [Fact]
    public async Task Stored_types_reach_rules_unchanged()
    {
        var rules = RuleText.ParseRuleSet("""
            match places/{id} {
              allow get: if resource.data.at is latlng && resource.data.at.latitude > 50
                && resource.data.owner is path && resource.data.owner == /users/u1
                && resource.data.n == 3.0;
            }
            """);
        var engine = new RuleEngine(new StaticRuleSetRepository(rules));
        RuleRequest With(Value at) => new()
        {
            Resource = new MapValue(new Dictionary<string, Value>
            {
                ["data"] = new MapValue(new Dictionary<string, Value>
                {
                    ["at"] = at, ["owner"] = new ReferenceValue("users/u1"), ["n"] = new IntegerValue(3),
                }),
            }),
        };
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "places/p1", With(new GeoPointValue(51.5, -0.1))));
        // A map that only looks like a geopoint isn't one.
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "places/p1", With(new MapValue(new Dictionary<string, Value>
        {
            ["latitude"] = new DoubleValue(51.5), ["longitude"] = new DoubleValue(-0.1),
        }))));
    }
}
