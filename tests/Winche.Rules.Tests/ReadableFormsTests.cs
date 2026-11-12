using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

/// <summary><c>not in</c>, <c>has only|all|any</c> and <c>changes only|all|any</c>: shorthand for the method chains.</summary>
public class ReadableFormsTests
{
    [Theory]
    [InlineData("'role' not in request.resource.data", "!('role' in request.resource.data)")]
    [InlineData("request.resource.data has only ['name', 'email']", "request.resource.data.keys().hasOnly(['name', 'email'])")]
    [InlineData("request.resource.data has all ['name']", "request.resource.data.keys().hasAll(['name'])")]
    [InlineData("request.auth.token has any ['admin', 'staff']", "request.auth.token.keys().hasAny(['admin', 'staff'])")]
    [InlineData("changes only ['name']", "request.resource.data.diff(resource.data).affectedKeys().hasOnly(['name'])")]
    [InlineData("changes all ['a', 'b']", "request.resource.data.diff(resource.data).affectedKeys().hasAll(['a', 'b'])")]
    [InlineData("changes any ['role']", "request.resource.data.diff(resource.data).affectedKeys().hasAny(['role'])")]
    public void Each_form_is_the_same_expression_as_its_chain(string readable, string chain)
    {
        var a = RuleText.Parse(readable);
        Assert.Equal(RuleText.Parse(chain), a);
        Assert.Equal(readable, RuleText.Format(RuleText.Parse(chain)));       // the formatter writes the short form
        Assert.Equal(chain, RuleText.Format(a, readable: false));             // and can write the chain
    }

    [Fact]
    public void Forms_bind_like_comparisons()
    {
        // && is looser, ! is tighter: `!x not in y` is (!x) not in y.
        Assert.Equal(
            RuleText.Parse("(a has only ['x']) && (changes only ['y']) && (b not in c)"),
            RuleText.Parse("a has only ['x'] && changes only ['y'] && b not in c"));
        Assert.Equal("!(changes any ['role'])", RuleText.Format(RuleText.Parse("!(changes any ['role'])")));
        Assert.Equal("(a not in b) == false", RuleText.Format(RuleText.Parse("(a not in b) == false")));
    }

    [Fact]
    public void The_words_are_still_usable_as_names()
    {
        // Only `has only|all|any`, `changes only|all|any` and `not in` are special; the words alone are names.
        var e = RuleText.Parse("changes == has && not.only == resource.changes && resource.has.any == 1");
        Assert.Equal(RuleText.Parse("`changes` == `has` && `not`.`only` == resource.`changes` && resource.`has`.`any` == 1"), e);
        Assert.Equal(e, RuleText.Parse(RuleText.Format(e)));
    }

    [Fact]
    public void A_list_after_the_word_is_required()
    {
        Assert.Throws<RuleSyntaxException>(() => RuleText.Parse("resource has only"));
        Assert.Throws<RuleSyntaxException>(() => RuleText.Parse("x not in"));
    }

    [Theory]
    [InlineData("{'name': 'B', 'owner': 'u1'}", true)]
    [InlineData("{'name': 'A', 'owner': 'u2'}", false)]
    [InlineData("{'name': 'A', 'owner': 'u1', 'extra': 1}", false)]
    public async Task Changes_evaluates_against_the_document_before_and_after(string after, bool allowed)
    {
        var rules = RuleText.ParseRuleSet("""
            match docs/{id} {
              allow update: if changes only ['name'] && 'admin' not in request.resource.data;
            }
            """);
        var engine = new RuleEngine(new StaticRuleSetRepository(rules), new DefaultRuleValueComparer());
        RuleValue Doc(string json) => System.Text.Json.JsonSerializer.Deserialize<RuleValue>(json.Replace('\'', '"'), Json.RuleJson.Options);
        RuleValue WithData(RuleValue fields) => RuleValue.Map(new Dictionary<string, RuleValue>(fields.AsMap) { ["data"] = fields });

        var request = new RuleRequest
        {
            Resource = WithData(Doc("{'name': 'A', 'owner': 'u1'}")),
            Request = RuleValue.Map(new Dictionary<string, RuleValue> { ["resource"] = WithData(Doc(after)) }),
        };
        Assert.Equal(allowed, await engine.AllowsAsync(RuleOperation.Update, "docs/d1", request));
    }
}
