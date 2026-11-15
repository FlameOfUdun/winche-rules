using Winche.Values;
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
        r.Match("docs/{id}", m => m.Allow(RuleOperations.Read, new LiteralExpression(RuleValues.True))));

    [Fact]
    public async Task AllowsAsync_ReflectsRepositoryUpdate()
    {
        var repository = new MutableRuleSetRepository(DenyAll());
        var engine = new RuleEngine(repository);

        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", new RuleRequest()));

        repository.Update(AllowReadDocs());   // hot-swap, no new engine

        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "docs/d1", new RuleRequest()));
    }

    [Fact]
    public void Allows_Query_ReflectsRepositoryUpdate()
    {
        var repository = new MutableRuleSetRepository(DenyAll());
        var engine = new RuleEngine(repository);
        var query = new QueryConstraints("docs", []);
        var request = new RuleRequest
        {
            Request = new MapValue(new Dictionary<string, Value> { ["method"] = new StringValue("list") }),
        };

        Assert.False(engine.Allows(query, request));

        repository.Update(AllowReadDocs());   // list is part of Read; an unconstrained query is now provably allowed

        Assert.True(engine.Allows(query, request));
    }

    [Fact]
    public void Ctor_NullRepository_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RuleEngine(null!));
    }
}
