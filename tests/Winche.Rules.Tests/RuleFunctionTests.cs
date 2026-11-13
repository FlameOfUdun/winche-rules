using Winche.Values;
using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

/// <summary>3.1.0 built-in functions, written in the text syntax (which also exercises the parser).</summary>
public class RuleFunctionTests
{
    private static StringValue S(string s) => new StringValue(s);
    private static MapValue M(params (string K, Value V)[] e) => new MapValue(e.ToDictionary(x => x.K, x => x.V));

    // before: {name: 'a', role: 'user', age: 3}; after: {name: 'b', role: 'user', email: 'x'}
    private static readonly Value Before = M(("name", S("a")), ("role", S("user")), ("age", new IntegerValue(3)));
    private static readonly Value After = M(("name", S("b")), ("role", S("user")), ("email", S("x")));

    private static RuleContext Ctx() => new()
    {
        Resource = Before,
        Request = M(("resource", After), ("auth", M(("token", M(("roles", new ArrayValue([S("editor"), S("viewer")]))))))),
        Params = new Dictionary<string, Value> { ["userId"] = S("u1") },
    };

    private static Task<bool> Eval(string text) => RuleEvaluator.EvaluateAsync(RuleText.Parse(text), Ctx());

    [Theory]
    [InlineData("request.resource.keys() == ['email', 'name', 'role']")]
    [InlineData("request.resource.keys().hasOnly(['name', 'role', 'email', 'extra'])")]
    [InlineData("!request.resource.keys().hasOnly(['name', 'role'])")]
    [InlineData("request.resource.keys().hasAll(['name', 'role'])")]
    [InlineData("!request.resource.keys().hasAll(['name', 'age'])")]
    [InlineData("request.auth.token.roles.hasAny(['admin', 'editor'])")]
    [InlineData("!request.auth.token.roles.hasAny(['admin'])")]
    [InlineData("request.resource.diff(resource).affectedKeys() == ['age', 'email', 'name']")]
    [InlineData("request.resource.diff(resource).addedKeys() == ['email']")]
    [InlineData("request.resource.diff(resource).removedKeys() == ['age']")]
    [InlineData("request.resource.diff(resource).changedKeys() == ['name']")]
    [InlineData("request.resource.diff(resource).unchangedKeys() == ['role']")]
    [InlineData("!('role' in request.resource.diff(resource).affectedKeys())")]
    [InlineData("request.resource.diff(resource).affectedKeys().hasOnly(['name', 'email', 'age'])")]
    [InlineData("request.resource.name is string && resource.age is int && resource.age is number")]
    [InlineData("!(resource.age is float) && !(resource.name is int)")]
    [InlineData("request.resource is map && request.auth.token.roles is list && null is null")]
    [InlineData("request.resource.email.matches('[a-z]+')")]
    [InlineData("!'x1'.matches('[a-z]')")]
    [InlineData("'AbC'.lower() == 'abc' && 'AbC'.upper() == 'ABC'")]
    [InlineData("request.resource.get('missing', 'dflt') == 'dflt' && request.resource.get('name', 'dflt') == 'b'")]
    [InlineData("request.resource.size() == 3 && 'abc'.size() == 3 && size([1, 2]) == 2")]
    [InlineData("keys(request.resource) == request.resource.keys()")]
    public async Task Functions_evaluate(string condition) => Assert.True(await Eval(condition));

    [Theory]
    [InlineData("'a'.keys()")]                         // not a map
    [InlineData("request.resource.hasOnly(['a'])")]    // not a list
    [InlineData("'a'.matches('(')")]                    // bad regex
    [InlineData("resource.age.lower()")]                // not a string
    [InlineData("`is`(1, 'colour')")]                   // unknown type
    [InlineData("resource.affectedKeys()")]             // not a diff
    [InlineData("nope(1)")]                             // unknown function
    [InlineData("keys()")]                              // wrong arity
    public async Task Bad_calls_deny(string condition) => Assert.False(await Eval(condition));

    [Fact]
    public async Task A_catastrophic_regex_denies_instead_of_hanging()
    {
        var input = new string('a', 40) + "!";
        var ctx = Ctx();
        var started = DateTime.UtcNow;
        Assert.False(await RuleEvaluator.EvaluateAsync(Expr.Const(input).Matches("(a+)+$"), ctx));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Csharp_helpers_build_the_same_calls()
    {
        var data = Expr.Request("resource");
        Assert.Equal(RuleText.Parse("request.resource.keys().hasOnly(['name', 'role', 'email'])"), data.Keys().HasOnly("name", "role", "email"));
        Assert.Equal(RuleText.Parse("request.resource.diff(resource).affectedKeys().hasOnly(['name'])"),
            data.Diff(Expr.Var("resource")).AffectedKeys().HasOnly("name"));
        Assert.Equal(RuleText.Parse("request.resource.name is string"), data.Field("name").Is("string"));
        Assert.True(await RuleEvaluator.EvaluateAsync(data.GetOr("x", Expr.Const(1)).Eq(Expr.Const(1L)), Ctx()));
    }
}
