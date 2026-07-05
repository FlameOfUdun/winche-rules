using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleEngineTests
{
    private sealed class AlwaysEqual : IRuleValueComparer
    {
        public bool AreEqual(RuleValue a, RuleValue b) => true;
        public bool TryCompare(RuleValue a, RuleValue b, out int r) { r = 0; return true; }
    }

    private static RuleSet OwnerRule() => RulesetBuilder.Build(r =>
        r.Match("docs/{id}", m => m.Allow(RuleOperations.Read,
            new CompareExpression(
                new MemberExpression(new MemberExpression(new VarExpression("resource"), "data"), "x"),
                ComparisonOperator.Eq,
                new LiteralExpression(RuleValue.String("Y"))))));

    private static RuleRequest WithX(string value) => new()
    {
        Resource = RuleValue.Map(new Dictionary<string, RuleValue>
        {
            ["data"] = RuleValue.Map(new Dictionary<string, RuleValue> { ["x"] = RuleValue.String(value) }),
        }),
    };

    [Fact]
    public async Task AllowsAsync_UsesEngineComparer()
    {
        var engine = new RuleEngine(new StaticRuleSetRepository(OwnerRule()), new AlwaysEqual());   // AlwaysEqual makes "Z" == "Y"
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", WithX("Z")));
    }

    [Fact]
    public async Task AllowsAsync_DefaultComparer_DeniesMismatch()
    {
        var engine = new RuleEngine(new StaticRuleSetRepository(OwnerRule()), new DefaultRuleValueComparer());   // default comparer: "Z" != "Y"
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", WithX("Z")));
    }
}
