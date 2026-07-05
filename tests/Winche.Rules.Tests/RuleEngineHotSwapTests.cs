using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Querying;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleEngineHotSwapTests
{
    private static RuleSet DenyAll() => new(new List<MatchBlock>());

    private static RuleSet AllowReadDocs() => RuleSetBuilder.Build(r =>
        r.Match("docs/{id}", m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true)))));

    [Fact]
    public async Task AllowsAsync_ReflectsRepositoryUpdate()
    {
        var repository = new MutableRuleSetRepository(DenyAll());
        var comparer = new DefaultRuleValueComparer();
        var engine = new RuleEngine(repository, comparer);

        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", new RuleRequest()));

        repository.Update(AllowReadDocs());   // hot-swap, no new engine

        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", new RuleRequest()));
    }

    [Fact]
    public void Allows_Query_ReflectsRepositoryUpdate()
    {
        var repository = new MutableRuleSetRepository(DenyAll());
        var comparer = new DefaultRuleValueComparer();
        var engine = new RuleEngine(repository, comparer);
        var query = new QueryConstraints("docs", []);
        var request = new RuleRequest
        {
            Request = RuleValue.Map(new Dictionary<string, RuleValue> { ["method"] = RuleValue.String("list") }),
        };

        Assert.False(engine.Allows(query, request));

        repository.Update(AllowReadDocs());   // list is part of Read; an unconstrained query is now provably allowed

        Assert.True(engine.Allows(query, request));
    }

    [Fact]
    public void Ctor_NullRepository_Throws()
    {
        var comparer = new DefaultRuleValueComparer();
        Assert.Throws<ArgumentNullException>(() => new RuleEngine(null!, comparer));
    }
}
