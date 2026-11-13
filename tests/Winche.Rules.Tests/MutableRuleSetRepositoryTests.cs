using Winche.Values;
using Winche.Rules;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class MutableRuleSetRepositoryTests
{
    private static RuleSet Set(string path) => RuleSetBuilder.Build(r =>
        r.Match(path, m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValues.True))));

    [Fact]
    public void Current_ReturnsInitialRuleSet()
    {
        var initial = Set("a/{id}");
        var repo = new MutableRuleSetRepository(initial);
        Assert.Same(initial, repo.Current);
    }

    [Fact]
    public void Update_ReplacesCurrentRuleSet()
    {
        var repo = new MutableRuleSetRepository(Set("a/{id}"));
        var next = Set("b/{id}");
        repo.Update(next);
        Assert.Same(next, repo.Current);
    }

    [Fact]
    public void Ctor_Null_DefaultsToEmptyDenyAll()
    {
        var repo = new MutableRuleSetRepository(null);
        Assert.Empty(repo.Current.Matches);
    }

    [Fact]
    public void Update_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new MutableRuleSetRepository(Set("a/{id}")).Update(null!));
}
