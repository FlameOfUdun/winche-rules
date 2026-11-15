using Winche.Values;
using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Winche.Rules.Querying;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

/// <summary>Rules-file functions and path building (4.1.0).</summary>
public class FunctionsAndPathsTests
{
    private static RuleEngine Engine(string rules) =>
        new(new StaticRuleSetRepository(RuleText.ParseRuleSet(rules)));

    private static MapValue Map(params (string Key, Value Value)[] entries) =>
        new MapValue(entries.ToDictionary(e => e.Key, e => e.Value));

    private static Value Auth(string uid, params (string Key, Value Value)[] claims) =>
        Map(("uid", new StringValue(uid)), ("token", Map([("uid", new StringValue(uid)), .. claims])));

    private static RuleRequest Request(Value auth, Value? resource = null, IRuleResourceProvider? provider = null) => new()
    {
        Resource = resource ?? RuleValues.Null,
        Request = Map(("auth", auth)),
        Provider = provider,
    };

    private sealed class Docs(Dictionary<string, Value> docs) : IRuleResourceProvider
    {
        public List<string> Asked { get; } = [];
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) { Asked.Add(path); return Task.FromResult(docs.ContainsKey(path)); }
        public Task<Value> GetAsync(string path, CancellationToken ct = default) { Asked.Add(path); return Task.FromResult(docs.GetValueOrDefault(path, RuleValues.Null)); }
    }

    // ── paths ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/teams/t1", "path('teams/t1')")]
    [InlineData("/users/$(request.auth.uid)", "path('users', request.auth.uid)")]
    [InlineData("/users/$(uid)/notes/$(id)", "path('users', uid, 'notes', id)")]
    [InlineData("/databases/$(database)/documents/users/$(uid)", "path('users', uid)")]   // Firebase's prefix
    [InlineData("/a-b/c.d/$(x.y)", "path('a-b', 'c.d', x.y)")]
    public void Path_literals_are_paths(string literal, string same)
    {
        Assert.Equal(RuleText.Parse(same), RuleText.Parse(literal));
    }

    [Theory]
    [InlineData("get(/users/$(request.auth.uid)).role == 'admin'")]
    [InlineData("exists(/teams/t1) && resource.team == /teams/$(teamId)")]
    [InlineData("get(/a/$(get(/b/$(x)).c)).d == 1")]
    [InlineData("resource.ref in /a/b && resource.m has any /c/d")]
    public void Path_literals_format_back(string text)
    {
        var e = RuleText.Parse(text);
        Assert.Equal(text, RuleText.Format(e));
        Assert.Equal(e, RuleText.Parse(RuleText.Format(e, readable: false)));
    }

    [Theory]
    [InlineData("get(/users/)", "A path segment is empty")]
    [InlineData("get(/users/$(uid)", "Expected ')'")]
    [InlineData("get(/users/$())", "$( ) needs an expression")]
    [InlineData("get(/users/$(a &&))", "ended too early")]
    public void Bad_path_literals_say_where(string text, string message)
    {
        var ex = Assert.Throws<RuleSyntaxException>(() => RuleText.Parse(text));
        Assert.Contains(message, ex.Reason);
    }

    [Fact]
    public async Task Built_paths_look_up_documents()
    {
        var engine = Engine("""
            match projects/{id} {
              allow get: if get(/members/$(request.auth.uid)).role == 'editor'
                && exists(/projects/$(id)/owners/$(request.auth.uid));
            }
            """);
        var docs = new Docs(new()
        {
            ["members/u1"] = Map(("role", new StringValue("editor"))),
            ["projects/p1/owners/u1"] = Map(),
        });
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "projects/p1", Request(Auth("u1"), provider: docs)));
        Assert.Contains("members/u1", docs.Asked);
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "projects/p2", Request(Auth("u1"), provider: docs)));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "projects/p1", Request(Auth("u2"), provider: docs)));
    }

    [Fact]
    public async Task A_value_is_one_segment_unless_it_is_a_path()
    {
        // A uid of "u1/admin" can't reach members/u1/admin; a {rest=**} capture (a path) adds all its segments.
        var engine = Engine("""
            match files/{rest=**} {
              allow get: if exists(/index/$(rest)) && !exists(/members/$(request.auth.uid));
            }
            """);
        var docs = new Docs(new() { ["index/a/b"] = Map(), ["members/u1/admin"] = Map() });
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "files/a/b", Request(Auth("x"), provider: docs)));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "files/a/b", Request(Auth("u1/admin"), provider: docs)));   // error → deny
    }

    // ── functions ────────────────────────────────────────────────────────────

    private const string WithFunctions = """
        function signedIn() {
          return request.auth != null;
        }

        function isOwner(uid) {
          return signedIn() && request.auth.uid == uid;
        }

        match users/{uid} {
          function isStaff() {
            let role = request.auth.token.role;
            return role in ['admin', 'staff'];
          }

          allow get: if isOwner(uid) || isStaff();
          allow list: if isStaff();

          match notes/{noteId} {
            allow read: if isOwner(uid);
            allow update: if canEdit(resource);

            function canEdit(note) {
              return note.ownerId == request.auth.uid || isStaff();
            }
          }
        }

        match notes/{id} {
          allow list: if isOwnedBy(request.auth.uid);

          function isOwnedBy(user) {
            return resource.ownerId == user;
          }
        }
        """;

    [Fact]
    public async Task Functions_evaluate_with_their_parameters_lets_and_scope()
    {
        var engine = Engine(WithFunctions);
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "users/u1", Request(Auth("u1"))));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "users/u2", Request(Auth("u1"))));
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "users/u2", Request(Auth("u1", ("role", new StringValue("staff"))))));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "users/u1", Request(RuleValues.Null)));
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "users/u1/notes/n1", Request(Auth("u1"))));
        // A function defined after its use, in a nested block, sees the outer block's functions.
        Assert.True(await engine.AllowsAsync(RuleOperation.Update, "users/u2/notes/n1",
            Request(Auth("u1"), Map(("ownerId", new StringValue("u1"))))));
        Assert.False(await engine.AllowsAsync(RuleOperation.Update, "users/u2/notes/n1",
            Request(Auth("u1"), Map(("ownerId", new StringValue("u3"))))));
    }

    [Fact]
    public void List_rules_through_functions_are_still_proved()
    {
        var rules = RuleText.ParseRuleSet(WithFunctions);
        var asU1 = new RuleRequest { Request = Map(("auth", Auth("u1")), ("method", new StringValue("list"))) };
        var byOwner = new QueryConstraints("notes", [new QueryConstraint(["ownerId"], ComparisonOperator.Eq, new StringValue("u1"))]);
        var engine = new RuleEngine(new StaticRuleSetRepository(rules));
        Assert.True(engine.Allows(byOwner, asU1));
        Assert.False(engine.Allows(new QueryConstraints("notes", []), asU1));
        // Staff (from a token claim, through a let) may list users.
        var asStaff = new RuleRequest { Request = Map(("auth", Auth("s", ("role", new StringValue("admin")))), ("method", new StringValue("list"))) };
        Assert.True(engine.Allows(new QueryConstraints("users", []), asStaff));
        Assert.False(engine.Allows(new QueryConstraints("users", []), asU1));
    }

    [Fact]
    public void Functions_format_and_round_trip_through_text_and_json()
    {
        var rules = RuleText.ParseRuleSet(WithFunctions);
        var text = RuleText.FormatRuleSet(rules);
        Assert.Contains("function isOwner(uid) {\n  return signedIn() && request.auth.uid == uid;\n}\n", text);
        Assert.Contains("  function isStaff() {\n    let role = request.auth.token.role;\n    return role in ['admin', 'staff'];\n  }\n", text);
        Assert.Equal(rules, RuleText.ParseRuleSet(text));
        Assert.Equal(text, RuleText.FormatRuleSet(RuleText.ParseRuleSet(text)));
        Assert.Equal(rules, RuleJson.DeserializeRuleSet(RuleJson.Serialize(rules)));
        Assert.NotEqual(rules, RuleText.ParseRuleSet(text.Replace("'staff']", "'staff', 'x']")));
    }

    [Fact]
    public void Rules_without_functions_serialize_as_before()
    {
        var json = RuleJson.Serialize(RuleText.ParseRuleSet("match a/{b} { allow read; }"));
        Assert.DoesNotContain("Functions", json);
        Assert.Empty(RuleSetValidation.Validate(RuleJson.DeserializeRuleSet(json)));
    }

    [Theory]
    [InlineData("function f() { return g(); }\nfunction g() { return f(); }\nmatch a/{b} { allow read: if f(); }", "'f' calls itself through 'g'", 1, 10)]
    [InlineData("function f(x) { return x == 1 || f(x); }\nmatch a/{b} { allow read: if f(1); }", "'f' calls itself", 1, 10)]
    [InlineData("function f(x) { return x; }\nmatch a/{b} {\n  allow read: if true && f(1, 2);\n}", "'f' takes 1 argument, not 2", 3, 26)]
    [InlineData("function size(x) { return true; }\nmatch a/{b} { allow read; }", "'size' is a built-in name", 1, 10)]
    [InlineData("function f() { return true; }\nfunction f() { return false; }\nmatch a/{b} { allow read; }", "'f' is defined twice", 2, 10)]
    [InlineData("function f(a, a) { return a; }", "two parameters named 'a'", 1, 15)]
    [InlineData("function f(a) { let a = 1; return a; }", "already a parameter or let", 1, 21)]
    [InlineData("function f() { let x == 1; return x; }", "Use '='", 1, 22)]
    [InlineData("function f() { let x = 1; }", "needs a 'return'", 1, 27)]
    [InlineData("function f() { return true }", "Missing ';'", 1, 28)]
    public void Function_mistakes_point_at_the_text(string file, string message, int line, int column)
    {
        var ex = Assert.Throws<RuleSyntaxException>(() => RuleText.ParseRuleSet(file));
        Assert.Contains(message, ex.Reason);
        Assert.Equal((line, column), (ex.Line, ex.Column));
    }

    [Fact]
    public void Validation_reports_function_mistakes_in_stored_rules()
    {
        var f = new RuleFunction("f", ["x"], [], new VarExpression("x"));
        var rules = new RuleSet([new MatchBlock("a/{b}", [new AllowRule([RuleOperation.Get], new CallExpression("f", []))], [])])
        {
            Functions = [f],
        };
        var issue = Assert.Single(RuleSetValidation.Validate(rules));
        Assert.Equal("a/{b}", issue.Path);
        Assert.Contains("takes 1 argument, not 0", issue.Message);
    }

    [Fact]
    public void Unknown_functions_know_the_files_own()
    {
        var rules = RuleText.ParseRuleSet("""
            function ok() { return true; }
            match a/{b} {
              function local() { return ok(); }
              allow read: if ok() && local() && typo() && size('x') > 0;
            }
            match c/{d} {
              allow read: if local();
            }
            """);
        Assert.Equal(["local", "typo"], RuleSetValidation.UnknownFunctions(rules));   // local isn't visible in c/{d}
    }

    [Fact]
    public async Task Merged_rulesets_keep_their_own_functions()
    {
        var a = RuleText.ParseRuleSet("function ok() { return request.auth != null; }\nmatch a/{x} { allow get: if ok(); }");
        var b = RuleText.ParseRuleSet("function ok() { return false; }\nmatch b/{x} { allow get: if ok(); }");
        var merged = RuleSet.Merge([a, b]);
        Assert.Empty(RuleSetValidation.Validate(merged));
        var engine = new RuleEngine(new StaticRuleSetRepository(merged));
        var signedIn = Request(Auth("u1"));
        Assert.True(await engine.AllowsAsync(RuleOperation.Get, "a/1", signedIn));
        Assert.False(await engine.AllowsAsync(RuleOperation.Get, "b/1", signedIn));
    }
}
