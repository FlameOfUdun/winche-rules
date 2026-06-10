using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Querying;
using Xunit;

namespace Winche.Rules.Tests;

public class QueryAnalyzerTests
{
    private static RuleContext Ctx(string? uid) => new()
    {
        Request = RuleValue.Map(new Dictionary<string, RuleValue>
        {
            ["method"] = RuleValue.String("list"),
            ["auth"] = uid is null
                ? RuleValue.Null
                : RuleValue.Map(new Dictionary<string, RuleValue> { ["uid"] = RuleValue.String(uid) }),
        }),
    };

    private static QueryConstraint C(string field, CompareOp op, RuleValue value) => new([field], op, value);
    private static QueryConstraints Q(string collection, params QueryConstraint[] constraints) => new(collection, constraints);

    private static bool Allows(Ruleset rs, QueryConstraints q, string? uid) => QueryAnalyzer.Allows(rs, q, Ctx(uid));

    // ── Owner equality (the core case) ─────────────────────────────────────────

    [Fact]
    public void Owner_AllowsConstrainedQuery_RejectsUnconstrained()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u =>
                u.Allow(RuleOperations.Read, Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")))));

        Assert.True(Allows(rs, Q("users", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1")); // constrained to me
        Assert.False(Allows(rs, Q("users"), "u1"));                                                     // unconstrained → reject
        Assert.False(Allows(rs, Q("users", C("ownerId", CompareOp.Eq, RuleValue.String("other"))), "u1")); // constrained to someone else
        Assert.False(Allows(rs, Q("users", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), uid: null)); // unauthenticated
    }

    // ── Public OR owner ────────────────────────────────────────────────────────

    [Fact]
    public void PublicOrOwner()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("docs/{id}", d => d.Allow(RuleOperations.Read, Expr.Any(
                Expr.Resource("data", "isPublic").Eq(true),
                Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid"))))));

        Assert.True(Allows(rs, Q("docs", C("isPublic", CompareOp.Eq, RuleValue.Bool(true))), "u1"));   // proves public branch
        Assert.True(Allows(rs, Q("docs", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1"));  // proves owner branch
        Assert.False(Allows(rs, Q("docs"), "u1"));                                                     // proves neither
        Assert.False(Allows(rs, Q("docs", C("isPublic", CompareOp.Eq, RuleValue.Bool(false))), "u1")); // public=false, owner unknown
    }

    // ── Always-allowed ─────────────────────────────────────────────────────────

    [Fact]
    public void AlwaysTrueRule_AllowsAnyQuery()
    {
        var rs = RulesetBuilder.Build(r => r.Match("pub/{id}", d => d.Allow(RuleOperations.Read, Expr.Const(true))));
        Assert.True(Allows(rs, Q("pub"), uid: null));
    }

    // ── Range / inequality ─────────────────────────────────────────────────────

    [Fact]
    public void Range_ExactAndEqualityPinProve_WeakerRejected()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("people/{id}", d => d.Allow(RuleOperations.Read, Expr.Resource("data", "age").Ge(Expr.Const(18)))));

        Assert.True(Allows(rs, Q("people", C("age", CompareOp.Ge, RuleValue.Int(18))), "x"));   // exact
        Assert.True(Allows(rs, Q("people", C("age", CompareOp.Eq, RuleValue.Int(20))), "x"));   // pinned 20 ≥ 18
        Assert.False(Allows(rs, Q("people", C("age", CompareOp.Ge, RuleValue.Int(10))), "x"));  // ≥10 does NOT prove ≥18
        Assert.False(Allows(rs, Q("people"), "x"));                                             // unconstrained
    }

    // ── Things the analyzer must refuse to prove (sound, incomplete) ───────────

    [Fact]
    public void GetExistsRule_IsRejectedForLists()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("docs/{id}", d => d.Allow(RuleOperations.Read, Expr.Exists(Expr.Const("teams/t")))));
        Assert.False(Allows(rs, Q("docs", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1"));
    }

    [Fact]
    public void PerResultWildcardRule_CannotBeProvenForLists()
    {
        // allow read: if request.auth.uid == userId  — fine for GET, but a LIST can't prove the per-doc id.
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u => u.Allow(RuleOperations.Read, Expr.Auth("uid").Eq(Expr.Param("userId")))));
        Assert.False(Allows(rs, Q("users", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1"));
    }

    // ── Subcollection: parent wildcard is bound, leaf is symbolic ──────────────

    [Fact]
    public void Subcollection_BindsParentWildcard()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u =>
                u.Match("posts/{postId}", p =>
                    p.Allow(RuleOperations.Read, Expr.Resource("data", "authorId").Eq(Expr.Param("userId"))))));

        // userId is bound to u1 from the collection path; query pins authorId == u1 → provable
        Assert.True(Allows(rs, Q("users/u1/posts", C("authorId", CompareOp.Eq, RuleValue.String("u1"))), "anyone"));
        Assert.False(Allows(rs, Q("users/u1/posts"), "anyone"));   // unconstrained
    }

    // ── Wrong collection → default-deny ────────────────────────────────────────

    [Fact]
    public void UnrelatedCollection_Rejected()
    {
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{userId}", u => u.Allow(RuleOperations.Read, Expr.Const(true))));
        Assert.False(Allows(rs, Q("orders"), "u1"));
    }

    // ── Recursive-leaf governance ──────────────────────────────────────────────

    [Fact]
    public void CatchAll_RecursiveLeaf_GovernsAnyCollection()
    {
        // match /{document=**} with allow read: true — governs every collection at any depth.
        var rs = RulesetBuilder.Build(r =>
            r.Match("{document=**}", b => b.Allow(RuleOperations.Read, Expr.Const(true))));

        Assert.True(Allows(rs, Q("foo"), uid: null));
        Assert.True(Allows(rs, Q("anything"), uid: null));
        Assert.True(Allows(rs, Q("a/b/c"), uid: null));
    }

    [Fact]
    public void RecursiveLeaf_WithPrefix_GovernsSubcollections()
    {
        // match /users/{doc=**} — governs "users" itself and any subcollection under "users".
        var rs = RulesetBuilder.Build(r =>
            r.Match("users/{doc=**}", b =>
                b.Allow(RuleOperations.Read, Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")))));

        // list on "users" constrained to ownerId == uid → allowed
        Assert.True(Allows(rs, Q("users", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1"));
        // list on "users" unconstrained → rejected
        Assert.False(Allows(rs, Q("users"), "u1"));
        // list on subcollection users/u1/posts constrained → allowed (prefix "users" matches)
        Assert.True(Allows(rs, Q("users/u1/posts", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1"));
        // list on unrelated collection → rejected (prefix "users" does not match)
        Assert.False(Allows(rs, Q("orders", C("ownerId", CompareOp.Eq, RuleValue.String("u1"))), "u1"));
    }

    [Fact]
    public void RecursiveLeaf_IntermediateParamCapture_BoundInCondition()
    {
        // match /orgs/{orgId}/{doc=**} — orgId is captured from the second collection segment.
        var rs = RulesetBuilder.Build(r =>
            r.Match("orgs/{orgId}/{doc=**}", b =>
                b.Allow(RuleOperations.Read, Expr.Resource("data", "org").Eq(Expr.Param("orgId")))));

        // "orgs/acme/items" → orgId bound to "acme"; query pins org == "acme" → allowed
        Assert.True(Allows(rs, Q("orgs/acme/items", C("org", CompareOp.Eq, RuleValue.String("acme"))), "any"));
        // same collection, unconstrained → rejected
        Assert.False(Allows(rs, Q("orgs/acme/items"), "any"));
    }
}
