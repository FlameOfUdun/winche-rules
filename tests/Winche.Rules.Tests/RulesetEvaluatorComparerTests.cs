using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class RulesetEvaluatorComparerTests
{
    // Declares everything equal — proves AllowsAsync forwards ctx.Comparer into the scoped
    // context it builds for path captures (regression: the scoped context used to drop Comparer).
    private sealed class AlwaysEqual : IRuleValueComparer
    {
        public bool AreEqual(RuleValue a, RuleValue b) => true;
        public bool TryCompare(RuleValue a, RuleValue b, out int r) { r = 0; return true; }
    }

    [Fact]
    public async Task AllowsAsync_ForwardsComparer_IntoScopedContext()
    {
        // rule: allow read if resource.data.x == "Y"
        var ruleset = RulesetBuilder.Build(r =>
            r.Match("docs/{id}", m => m.Allow(RuleOperations.Read,
                new CompareExpression(
                    new MemberExpression(new MemberExpression(new VarExpression("resource"), "data"), "x"),
                    ComparisonOperator.Eq,
                    new LiteralExpression(RuleValue.String("Y"))))));

        // resource.data.x is actually "Z" — only AlwaysEqual makes "Z" == "Y", so the rule allows.
        var resource = RuleValue.Map(new Dictionary<string, RuleValue>
        {
            ["data"] = RuleValue.Map(new Dictionary<string, RuleValue> { ["x"] = RuleValue.String("Z") }),
        });
        var ctx = new RuleContext { Resource = resource, Comparer = new AlwaysEqual() };

        Assert.True(await RulesetEvaluator.AllowsAsync(ruleset, RuleOperation.Get, "docs/d1", ctx));
    }
}
