using Winche.Values;
using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Matching;
using Winche.Rules.Querying;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

/// <summary>3.1.0: a recursive wildcard anywhere in a path, and collection group queries.</summary>
public class RecursiveWildcardTests
{
    [Theory]
    [InlineData("{path=**}/posts/{id}", "posts/p1", true, "")]               // zero segments before
    [InlineData("{path=**}/posts/{id}", "users/u1/posts/p1", true, "users/u1")]
    [InlineData("{path=**}/posts/{id}", "a/b/c/d/posts/p1", true, "a/b/c/d")]
    [InlineData("{path=**}/posts/{id}", "users/u1/notes/n1", false, null)]
    [InlineData("{path=**}/posts/{id}", "users/u1/posts", false, null)]
    [InlineData("users/{uid}/{path=**}/likes/{id}", "users/u1/likes/l1", true, "")]
    [InlineData("users/{uid}/{path=**}/likes/{id}", "users/u1/posts/p1/likes/l1", true, "posts/p1")]
    [InlineData("users/{uid}/{path=**}/likes/{id}", "teams/t1/likes/l1", false, null)]
    [InlineData("users/{rest=**}", "users/u1", true, "u1")]                   // trailing: one or more, as before
    [InlineData("users/{rest=**}", "users", false, null)]
    public void Matches_with_a_recursive_wildcard_anywhere(string pattern, string path, bool matches, string? captured)
    {
        var ok = RulePath.TryMatch(RulePath.Parse(pattern), path, out var captures);
        Assert.Equal(matches, ok);
        if (!matches) return;
        var name = pattern.Contains("{path=**}") ? "path" : "rest";
        Assert.Equal(captured, ((ReferenceValue)captures[name]).Path);
        if (pattern.Contains("{id}")) Assert.Equal(path.Split('/')[^1], ((StringValue)captures["id"]).Value);
    }

    [Fact]
    public void Two_recursive_wildcards_in_one_full_path_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => RuleSetBuilder.Build(r =>
            r.Match("{a=**}/posts/{id}", p => p.Match("comments/{c=**}", c => c.Allow([RuleOperation.Get], Expr.Const(true))))));
        var ex = Assert.Throws<RuleSyntaxException>(() => RuleText.ParseRuleSet("match {a=**}/posts/{id} {\n  match x/{b=**} {\n  }\n}"));
        Assert.StartsWith("'match x/{b=**}' has a second recursive wildcard", ex.Reason);
        // Siblings each have their own: fine.
        RuleText.ParseRuleSet("match {a=**}/posts/{id} {\n}\nmatch {b=**}/notes/{id} {\n}");
    }

    [Fact]
    public async Task Single_document_operations_use_it()
    {
        var engine = new RuleEngine(new StaticRuleSetRepository(RuleText.ParseRuleSet(
            "match {path=**}/posts/{id} {\n  allow get: if id in ['p1', 'top'];\n}")));
        var req = new RuleRequest { Request = new MapValue(new Dictionary<string, Value>()) };
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "users/u1/posts/p1", req));
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "posts/top", req));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "posts/other", req));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "users/u1/notes/n1", req));
    }

    // ── collection group queries ───────────────────────────────────────────

    private static RuleContext Ctx(string uid) => new()
    {
        Request = new MapValue(new Dictionary<string, Value>
        {
            ["auth"] = new MapValue(new Dictionary<string, Value> { ["uid"] = new StringValue(uid) }),
        }),
    };

    private static QueryConstraints Group(string id, params QueryConstraint[] c) => new(id, c, CollectionGroup: true);
    private static QueryConstraint Eq(string field, string value) => new([field], ComparisonOperator.Eq, new StringValue(value));

    [Fact]
    public void A_group_query_needs_a_rule_covering_every_parent()
    {
        var rules = RuleText.ParseRuleSet("""
            match {path=**}/posts/{postId} {
              allow list: if resource.authorId == request.auth.uid;
            }
            match users/{uid}/notes/{noteId} {
              allow list: if request.auth.uid == uid;
            }
            """);

        Assert.True(QueryAnalyzer.Allows(rules, Group("posts", Eq("authorId", "u1")), Ctx("u1")));
        Assert.False(QueryAnalyzer.Allows(rules, Group("posts", Eq("authorId", "u2")), Ctx("u1")));
        Assert.False(QueryAnalyzer.Allows(rules, Group("posts"), Ctx("u1")));                    // unconstrained
        Assert.False(QueryAnalyzer.Allows(rules, Group("notes", Eq("x", "y")), Ctx("u1")));      // only users/{uid}/notes is covered
        Assert.False(QueryAnalyzer.Allows(rules, Group("comments"), Ctx("u1")));                  // nothing covers it
    }

    [Fact]
    public void A_capture_for_the_collection_binds_to_the_id_and_a_bare_recursive_covers_all()
    {
        var byName = RuleText.ParseRuleSet("match {path=**}/{coll}/{doc} {\n  allow list: if coll in ['public', 'news'];\n}");
        Assert.True(QueryAnalyzer.Allows(byName, Group("news"), Ctx("u1")));
        Assert.False(QueryAnalyzer.Allows(byName, Group("secret"), Ctx("u1")));

        var all = RuleText.ParseRuleSet("match {path=**} {\n  allow read: if request.auth.uid == 'admin';\n}");
        Assert.True(QueryAnalyzer.Allows(all, Group("anything"), Ctx("admin")));
        Assert.False(QueryAnalyzer.Allows(all, Group("anything"), Ctx("u1")));
    }

    [Fact]
    public void A_condition_on_the_unbound_parent_capture_is_not_guessed()
    {
        var rules = RuleText.ParseRuleSet("match {path=**}/posts/{id} {\n  allow list: if path == 'users/u1';\n}");
        Assert.False(QueryAnalyzer.Allows(rules, Group("posts"), Ctx("u1")));
    }

    [Fact]
    public void A_middle_recursive_rule_also_governs_ordinary_collection_queries()
    {
        var rules = RuleText.ParseRuleSet("match {path=**}/posts/{id} {\n  allow list: if request.auth.uid == 'u1';\n}");
        Assert.True(QueryAnalyzer.Allows(rules, new QueryConstraints("users/u9/posts", []), Ctx("u1")));
        Assert.True(QueryAnalyzer.Allows(rules, new QueryConstraints("posts", []), Ctx("u1")));
        Assert.False(QueryAnalyzer.Allows(rules, new QueryConstraints("users/u9/notes", []), Ctx("u1")));
    }
}
