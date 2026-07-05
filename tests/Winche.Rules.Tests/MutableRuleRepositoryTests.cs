using Winche.Rules;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class MutableRuleRepositoryTests
{
    private static RuleSet Set(string path) => RulesetBuilder.Build(r =>
        r.Match(path, m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true)))));

    [Fact]
    public void Current_ReturnsInitialRuleset()
    {
        var initial = Set("a/{id}");
        var repo = new MutableRuleRepository(initial);
        Assert.Same(initial, repo.Current);
    }

    [Fact]
    public void Update_ReplacesCurrentRuleset()
    {
        var repo = new MutableRuleRepository(Set("a/{id}"));
        var next = Set("b/{id}");
        repo.Update(next);
        Assert.Same(next, repo.Current);
    }

    [Fact]
    public void Ctor_NullRuleset_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new MutableRuleRepository(null!));

    [Fact]
    public void Update_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new MutableRuleRepository(Set("a/{id}")).Update(null!));
}
