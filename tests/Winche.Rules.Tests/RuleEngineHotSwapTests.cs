using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Querying;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleEngineHotSwapTests
{
    private static RuleSet DenyAll() => new(new List<MatchBlock>());

    private static RuleSet AllowReadDocs() => RulesetBuilder.Build(r =>
        r.Match("docs/{id}", m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValue.Bool(true)))));

    [Fact]
    public async Task AllowsAsync_ReflectsRepositoryUpdate()
    {
        var repo = new MutableRuleRepository(DenyAll());
        var engine = new RuleEngine(repo);

        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", new RuleRequest()));

        repo.Update(AllowReadDocs());   // hot-swap, no new engine

        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", new RuleRequest()));
    }

    [Fact]
    public void Allows_Query_ReflectsRepositoryUpdate()
    {
        var repo = new MutableRuleRepository(DenyAll());
        var engine = new RuleEngine(repo);
        var query = new QueryConstraints("docs", []);
        var request = new RuleRequest
        {
            Request = RuleValue.Map(new Dictionary<string, RuleValue> { ["method"] = RuleValue.String("list") }),
        };

        Assert.False(engine.Allows(query, request));

        repo.Update(AllowReadDocs());   // list is part of Read; an unconstrained query is now provably allowed

        Assert.True(engine.Allows(query, request));
    }

    [Fact]
    public void Ctor_NullRepository_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new RuleEngine((IRuleRepository)null!));
}
