using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests.Evaluation;

public class EvaluatorComparerTests
{
    private sealed class AlwaysEqual : IRuleValueComparer
    {
        public bool AreEqual(RuleValue a, RuleValue b) => true;
        public bool TryCompare(RuleValue a, RuleValue b, out int r) { r = 0; return true; }
    }

    [Fact]
    public async Task Compare_UsesContextComparer()
    {
        // CompareExpr ctor is (Left, Op, Right) — fixed from the plan's draft which had Op first
        var cond = new CompareExpression(new LiteralExpression(RuleValue.Int(1)), ComparisonOperator.Eq, new LiteralExpression(RuleValue.Int(2)));
        var ctx = new RuleContext { Comparer = new AlwaysEqual() };
        Assert.True(await RuleEvaluator.EvaluateAsync(cond, ctx));
    }
}
