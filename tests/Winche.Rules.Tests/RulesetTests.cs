using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Xunit;

namespace Winche.Rules.Tests;

public class RulesetTests
{
    private static RuleContext Ctx(string? uid) => new()
    {
        Request = RuleValue.Map(new Dictionary<string, RuleValue>
        {
            ["method"] = RuleValue.String("get"),
            ["auth"] = uid is null
                ? RuleValue.Null
                : RuleValue.Map(new Dictionary<string, RuleValue> { ["uid"] = RuleValue.String(uid) }),
        }),
    };

    private static Task<bool> Allows(RuleSet rs, RuleOperation op, string path, string? uid) =>
        RulesetEvaluator.AllowsAsync(rs, op, path, Ctx(uid));

    // ── Path-param condition ────────────────────────────────────────────────

    [Fact]
    public async Task OwnerByPathParam()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u =>
                u.Allow(RuleOperations.Read, Expr.Auth("uid").Eq(Expr.Param("userId")))));

        Assert.True(await Allows(rs, RuleOperation.Get, "users/u1", "u1"));
        Assert.False(await Allows(rs, RuleOperation.Get, "users/u1", "u2"));
        Assert.False(await Allows(rs, RuleOperation.Create, "users/u1", "u1"));   // no write rule
        Assert.False(await Allows(rs, RuleOperation.Get, "users/u1", uid: null)); // unauthenticated
    }

    // ── Nested match ────────────────────────────────────────────────────────

    [Fact]
    public async Task NestedMatch_AppliesToDeeperPath()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u =>
            {
                u.Allow(RuleOperations.Read, Expr.Auth("uid").Eq(Expr.Param("userId")));
                u.Match("posts/{postId}", p =>
                    p.Allow(RuleOperations.Read, Expr.Const(true)));   // any signed-in/none — public posts
            }));

        Assert.True(await Allows(rs, RuleOperation.Get, "users/u1/posts/p1", uid: null));   // nested public
        Assert.True(await Allows(rs, RuleOperation.Get, "users/u1", "u1"));                 // outer owner
        Assert.False(await Allows(rs, RuleOperation.Get, "users/u1", "u2"));                // outer denies; nested doesn't apply
    }

    // ── Recursive wildcard ──────────────────────────────────────────────────

    [Fact]
    public async Task RecursiveWildcard_MatchesAnyDepth()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("files/{path=**}", m => m.Allow(RuleOperations.Read, Expr.Const(true))));

        Assert.True(await Allows(rs, RuleOperation.Get, "files/a", "x"));
        Assert.True(await Allows(rs, RuleOperation.Get, "files/a/b/c", "x"));
        Assert.False(await Allows(rs, RuleOperation.Get, "other/a", "x"));   // different collection → default-deny
    }

    [Fact]
    public async Task RecursiveWildcard_DoesNotMatchBareCollection()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("files/{path=**}", m => m.Allow(RuleOperations.Read, Expr.Const(true))));

        Assert.True(await Allows(rs, RuleOperation.Get, "files/a", "x"));       // 1 segment under → matches
        Assert.True(await Allows(rs, RuleOperation.Get, "files/a/b/c", "x"));   // deep → matches
        Assert.False(await Allows(rs, RuleOperation.Get, "files", "x"));        // 0 segments under → no match (Firestore: 1+)
    }

    // ── Default-deny ────────────────────────────────────────────────────────

    [Fact]
    public async Task NoMatchingRule_Denies()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u => u.Allow(RuleOperations.Read, Expr.Const(true))));

        Assert.False(await Allows(rs, RuleOperation.Get, "orders/o1", "x"));
    }

    // ── Ruleset.Merge ─────────────────────────────────────────────────────────

    [Fact]
    public void Merge_EmptySequence_ReturnsEmptyRuleset()
    {
        var result = RuleSet.Merge([]);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Merge_SingleRuleset_PreservesBlocks()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u =>
                u.Allow(RuleOperations.Read, Expr.Const(true))));

        var result = RuleSet.Merge([rs]);
        Assert.Equal(rs.Matches, result.Matches);
    }

    [Fact]
    public async Task Merge_TwoRulesets_ContainsAllBlocksAndBothAreAuthorized()
    {
        var rs1 = RulesetBuilder.Build(r =>
            r.Match("alpha/{id}", b => b.Allow(RuleOperations.Read, Expr.Const(true))));
        var rs2 = RulesetBuilder.Build(r =>
            r.Match("beta/{id}", b => b.Allow(RuleOperations.Read, Expr.Const(true))));

        var merged = RuleSet.Merge([rs1, rs2]);

        // All blocks from both rulesets are present
        Assert.Equal(rs1.Matches.Count + rs2.Matches.Count, merged.Matches.Count);
        Assert.Contains(rs1.Matches[0], merged.Matches);
        Assert.Contains(rs2.Matches[0], merged.Matches);

        // Both collections are authorized through the merged ruleset
        Assert.True(await Allows(merged, RuleOperation.Get, "alpha/doc1", "u1"));
        Assert.True(await Allows(merged, RuleOperation.Get, "beta/doc1", "u1"));
    }

    [Fact]
    public void Merge_NullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RuleSet.Merge(null!));
    }

    // ── JSON round-trip ──────────────────────────────────────────────────────

    [Fact]
    public void Ruleset_RoundTrips()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u =>
            {
                u.Allow(RuleOperations.Read, Expr.Auth("uid").Eq(Expr.Param("userId")));
                u.Allow(RuleOperations.Of(RuleOperation.Create), Expr.RequestResource("data", "ownerId").Eq(Expr.Auth("uid")));
                u.Match("posts/{postId}", p => p.Allow(RuleOperations.Read, Expr.Const(true)));
            }));

        Assert.Equal(rs, RuleJson.DeserializeRuleset(RuleJson.Serialize(rs)));
    }
}
