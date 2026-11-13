using Winche.Rules;
using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleSetTextTests
{
    private const string File = """
        // Owners only.
        match users/{uid} {
          allow read: if request.auth.uid == uid;
          allow create: if request.auth.uid == uid && request.resource.data.keys().hasOnly(['name', 'email']);
          allow update, delete: if request.auth.uid == uid;

          match notes/{noteId} {
            allow read, write: if request.auth.uid == uid;
          }
        }

        match public/{doc=**} {
          allow get;
        }
        """;

    [Fact]
    public void Parses_a_rules_file()
    {
        var rs = RuleText.ParseRuleSet(File);

        var expected = RuleSetBuilder.Build(r =>
        {
            var owner = Expr.Auth("uid").Eq(Expr.Param("uid"));
            r.Match("users/{uid}", u =>
            {
                u.Allow(RuleOperations.Read, owner);
                u.Allow([RuleOperation.Create], owner.And(Expr.Request("resource", "data").Keys().HasOnly("name", "email")));
                u.Allow([RuleOperation.Update, RuleOperation.Delete], owner);
                u.Match("notes/{noteId}", n => n.Allow(RuleOperations.All, owner));
            });
            r.Match("public/{doc=**}", p => p.Allow([RuleOperation.Get], Expr.Const(true)));
        });
        Assert.Equal(Canon(expected), Canon(rs));
    }

    [Fact]
    public void Formats_and_parses_back_to_the_same_rules()
    {
        var rs = RuleText.ParseRuleSet(File);
        var text = RuleText.FormatRuleSet(rs);

        Assert.Equal(rs, RuleText.ParseRuleSet(text));
        Assert.Equal(text, RuleText.FormatRuleSet(RuleText.ParseRuleSet(text)));
        Assert.Equal(rs, RuleJson.DeserializeRuleSet(RuleJson.Serialize(rs)));
        Assert.Equal("""
            match users/{uid} {
              allow read: if request.auth.uid == uid;
              allow create: if request.auth.uid == uid && request.resource.data has only ['name', 'email'];
              allow update, delete: if request.auth.uid == uid;

              match notes/{noteId} {
                allow read, write: if request.auth.uid == uid;
              }
            }

            match public/{doc=**} {
              allow get: if true;
            }

            """.Replace("\r\n", "\n"), text);
    }

    [Fact]
    public void Long_conditions_go_one_part_per_line()
    {
        var rs = RuleText.ParseRuleSet("""
            match profiles/{uid} {
              allow create: if request.auth.uid == uid && request.resource.data.keys().hasOnly(['name', 'email', 'photo']) && request.resource.data.name is string && request.resource.data.name.size() <= 50 && request.resource.data.photo is string;
            }
            """);
        var text = RuleText.FormatRuleSet(rs);
        Assert.Contains("allow create: if request.auth.uid == uid\n    && request.resource.data has only ['name', 'email', 'photo']", text);
        Assert.Equal(rs, RuleText.ParseRuleSet(text));
    }

    [Fact]
    public void An_empty_file_is_no_rules()
    {
        Assert.Empty(RuleText.ParseRuleSet("  // nothing yet\n").Matches);
        Assert.Equal("", RuleText.FormatRuleSet(new RuleSet([])));
    }

    [Fact]
    public void Paths_may_touch_the_brace_and_start_with_a_slash()
    {
        var rs = RuleText.ParseRuleSet("match /users/{uid}{ allow read; }");
        Assert.Equal("/users/{uid}", rs.Matches[0].Path);
    }

    [Theory]
    [InlineData("allow read;", "'allow' must be inside a match block", 1, 1)]
    [InlineData("match users/{uid} {\n  allow read\n}", "Expected ':' after the operations", 3, 1)]
    [InlineData("match users/{uid} {\n  allow raed: if true;\n}", "Unknown operation 'raed'", 2, 9)]
    [InlineData("match users/{uid} {\n  allow read: request.auth != null;\n}", "Expected 'if' before the condition", 2, 15)]
    [InlineData("match users/{uid} {\n  allow read: if request.auth = null;\n}", "Use '==' to compare", 2, 31)]
    [InlineData("match users/{uid} {\n  allow read: if a\n}", "Missing ';' after the condition", 3, 1)]
    [InlineData("match users/{uid} {\n  allow read: if true;\n", "Missing '}' to close 'match users/{uid}'", 3, 1)]
    [InlineData("match users/{u-id} {}", "'{u-id}' isn't a valid capture", 1, 7)]
    [InlineData("match a/{p=**} {\n  match b/{c} {}\n}", "'match a/{p=**}' ends in a recursive wildcard", 1, 7)]
    [InlineData("match users/{uid} {\n  allow read: if '; still a string';", "Missing '}' to close", 2, 37)]
    public void Errors_say_what_and_where(string text, string reason, int line, int column)
    {
        var ex = Assert.Throws<RuleSyntaxException>(() => RuleText.ParseRuleSet(text));
        Assert.StartsWith(reason, ex.Reason);
        Assert.Equal((line, column), (ex.Line, ex.Column));
    }

    // Operation order and && nesting don't matter: compare the serialized form of a normalized copy.
    private static string Canon(RuleSet rs) => RuleJson.Serialize(new RuleSet([.. rs.Matches.Select(Canon)]));

    private static MatchBlock Canon(MatchBlock b) => new(b.Path,
        [.. b.Allow.Select(a => new AllowRule([.. a.Operations.Order()], RuleText.Parse(RuleText.Format(a.Condition))))],
        [.. b.Matches.Select(Canon)]);
}
