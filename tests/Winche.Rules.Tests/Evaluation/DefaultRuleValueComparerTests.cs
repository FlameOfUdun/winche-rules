using Winche.Rules;
using Winche.Rules.Evaluation;
using Xunit;

namespace Winche.Rules.Tests.Evaluation;

public class DefaultRuleValueComparerTests
{
    [Fact]
    public void Default_MirrorsStructEquality_AndOrdering()
    {
        var c = new DefaultRuleValueComparer();

        Assert.True(c.AreEqual(RuleValue.Int(5), RuleValue.Double(5.0)));   // numeric cross-type
        Assert.False(c.AreEqual(RuleValue.String("a"), RuleValue.String("b")));

        Assert.True(c.TryCompare(RuleValue.Int(1), RuleValue.Int(2), out var r));
        Assert.True(r < 0);

        Assert.False(c.TryCompare(RuleValue.Null, RuleValue.Int(1), out _)); // un-orderable
    }
}
