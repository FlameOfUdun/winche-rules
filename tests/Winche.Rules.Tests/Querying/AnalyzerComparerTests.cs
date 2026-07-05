using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Querying;
using Xunit;

namespace Winche.Rules.Tests.Querying;

public class AnalyzerComparerTests
{
    private sealed class AlwaysEqual : IRuleValueComparer
    {
        public bool AreEqual(RuleValue a, RuleValue b) => true;
        public bool TryCompare(RuleValue a, RuleValue b, out int r) { r = 0; return true; }
    }

    [Fact]
    public void Analyzer_UsesContextComparer_ForAtomProof()
    {
        // rule: allow list if resource.ownerId == "X"
        var ruleset = RuleSetBuilder.Build(r =>
            r.Match("docs/{id}", m => m.Allow(RuleOperations.Read,
                new CompareExpression(
                    new MemberExpression(new VarExpression("resource"), "ownerId"),
                    ComparisonOperator.Eq,
                    new LiteralExpression(RuleValue.String("X"))))));

        // query constrains ownerId == "Y": normally would NOT prove the rule,
        // but AlwaysEqual makes "Y" == "X" hold, proving it.
        var query = new QueryConstraints("docs",
            [new QueryConstraint(["ownerId"], ComparisonOperator.Eq, RuleValue.String("Y"))]);
        var ctx = new RuleContext { Comparer = new AlwaysEqual() };

        Assert.True(QueryAnalyzer.Allows(ruleset, query, ctx));
    }
}
