using System.Globalization;
using Winche.Rules;
using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Winche.Rules.Text;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleTextTests
{
    [Fact]
    public void Parses_operators_with_the_right_precedence()
    {
        var e = RuleText.Parse("a == 1 || b && !c.d in ['x'] ? e[0] : f.g(2)");
        var expected = new ConditionalExpression(
            new OrExpression([
                new CompareExpression(new VarExpression("a"), ComparisonOperator.Eq, new LiteralExpression(RuleValue.Int(1))),
                new AndExpression([
                    new VarExpression("b"),
                    new InExpression(new NotExpression(new MemberExpression(new VarExpression("c"), "d")),
                        new LiteralExpression(RuleValue.List([RuleValue.String("x")]))),
                ]),
            ]),
            new IndexExpression(new VarExpression("e"), new LiteralExpression(RuleValue.Int(0))),
            new CallExpression("g", [new VarExpression("f"), new LiteralExpression(RuleValue.Int(2))]));
        Assert.Equal(expected, e);
    }

    [Fact]
    public void Writes_the_shapes_people_expect()
    {
        Assert.Equal("request.auth.token.sub == userId",
            RuleText.Format(Expr.Auth("token", "sub").Eq(Expr.Param("userId"))));
        Assert.Equal("request.resource.keys().hasOnly(['name', 'email'])",
            RuleText.Format(Expr.Request("resource").Keys().HasOnly("name", "email")));
        Assert.Equal("!(collection in ['analytics', 'notificationLog'])",
            RuleText.Format(Expr.Param("collection").In(Expr.Strings("analytics", "notificationLog")).Not()));
        Assert.Equal("request.resource.age is int", RuleText.Format(Expr.Request("resource", "age").Is("int")));
        Assert.Equal("(a || b) && c", RuleText.Format(Expr.All(Expr.Any(Expr.Var("a"), Expr.Var("b")), Expr.Var("c"))));
        Assert.Equal("a || b && c", RuleText.Format(Expr.Any(Expr.Var("a"), Expr.All(Expr.Var("b"), Expr.Var("c")))));
        Assert.Equal("exists(path('users/u1'))", RuleText.Format(Expr.Exists(Expr.Value(RuleValue.Path("users/u1")))));
        Assert.Equal("resource.`odd name`.`in`", RuleText.Format(Expr.Resource("odd name", "in")));
        Assert.Equal("1.0 == 1 && -2 < x", RuleText.Format(Expr.All(Expr.Const(1.0).Eq(Expr.Const(1L)), Expr.Const(-2L).Lt(Expr.Var("x")))));
        Assert.Equal("true", RuleText.Format(Expr.All()));
        Assert.Equal("false", RuleText.Format(Expr.Any()));
    }

    [Theory]
    [InlineData("request.auth.token.sub == userId && request.resource.keys().hasOnly(['name', 'email'])")]
    [InlineData("!('role' in request.resource.diff(resource).affectedKeys())")]
    [InlineData("request.time < timestamp('2027-01-01T00:00:00.0000000+00:00')")]
    [InlineData("resource.blob == bytes('AAEC') && resource.ref == path('a/b') && resource.n == float('1e3')")]
    [InlineData("`is`(x) && `true` == `null`.`in`")]
    [InlineData("resource.m == {'a': 1, 'b': [true, null, 'x\\'y']} && resource.`weird key` != 0.5")]
    [InlineData("x ? (y ? 1 : 2) : z.get('k', 3)")]
    [InlineData("a[b[0]].c(d.e(), [])")]
    public void Format_and_parse_round_trip(string text)
    {
        var parsed = RuleText.Parse(text);
        var formatted = RuleText.Format(parsed);
        Assert.Equal(parsed, RuleText.Parse(formatted));
        Assert.Equal(formatted, RuleText.Format(RuleText.Parse(formatted)));
        // and through the stored JSON form the console saves
        Assert.Equal(parsed, RuleJson.DeserializeExpr(RuleJson.Serialize(parsed)));
    }

    [Fact]
    public void Random_expressions_round_trip()
    {
        var rng = new Random(11);
        for (var i = 0; i < 2000; i++)
        {
            var e = Gen(rng, depth: 4);
            var text = RuleText.Format(e);
            RuleExpression back;
            try { back = RuleText.Parse(text); }
            catch (RuleSyntaxException ex) { throw new Xunit.Sdk.XunitException($"#{i}: {text}\n{ex.Message}"); }
            Assert.True(Normalize(e).Equals(Normalize(back)), $"#{i}:\n  text: {text}\n  back: {RuleText.Format(back)}");
        }
    }

    [Theory]
    [InlineData("a = b", "Use '==' to compare", 1, 3)]
    [InlineData("a &&", "The condition ended too early", 1, 5)]
    [InlineData("a == 'x", "A string isn't closed", 1, 6)]
    [InlineData("a\n  && (b", "Expected ')' but the condition ended", 2, 8)]
    [InlineData("x == float('NaN')", "'NaN' is not a finite number", 1, 6)]
    [InlineData("a is colour", "Expected a type after 'is'", 1, 6)]
    [InlineData("[a]", "List and map elements must be constants", 1, 2)]
    [InlineData("timestamp('soon')", "'soon' is not an ISO-8601 timestamp", 1, 1)]
    [InlineData("a b", "Unexpected 'b'", 1, 3)]
    [InlineData("", "A condition is empty", 1, 1)]
    public void Errors_say_what_and_where(string text, string reason, int line, int column)
    {
        var ex = Assert.Throws<RuleSyntaxException>(() => RuleText.Parse(text));
        Assert.StartsWith(reason, ex.Reason);
        Assert.Equal((line, column), (ex.Line, ex.Column));
    }

    [Fact]
    public void Comments_and_newlines_are_allowed()
    {
        var e = RuleText.Parse("""
            // owner only
            request.auth.token.sub == userId   // the path capture
              && request.resource.keys().hasOnly(['a'])
            """);
        Assert.IsType<AndExpression>(e);
    }

    // ── random expressions ─────────────────────────────────────────────────

    private static readonly string[] Names = ["a", "b", "request", "resource", "userId", "odd name", "in", "x_1"];

    private static RuleValue GenValue(Random rng, int depth) => rng.Next(depth > 0 ? 10 : 8) switch
    {
        0 => RuleValue.Null,
        1 => RuleValue.Bool(rng.Next(2) == 0),
        2 => RuleValue.Int(rng.Next(-1000, 1000)),
        3 => RuleValue.Double(Math.Round(rng.NextDouble() * 200 - 100, rng.Next(0, 4))),   // finite: stored as JSON
        4 => RuleValue.String(new string([.. Enumerable.Range(0, rng.Next(0, 5)).Select(_ => "ab'\"\\\n é`"[rng.Next(9)])])),
        5 => RuleValue.Timestamp(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(rng.Next(100000))),
        6 => RuleValue.Path("a/" + rng.Next(10)),
        7 => RuleValue.Bytes([(byte)rng.Next(256), (byte)rng.Next(256)]),
        8 => RuleValue.List([.. Enumerable.Range(0, rng.Next(0, 3)).Select(_ => GenValue(rng, depth - 1))]),
        _ => RuleValue.Map(Enumerable.Range(0, rng.Next(0, 3)).ToDictionary(k => "k" + k, _ => GenValue(rng, depth - 1))),
    };

    private static readonly string[] Calls = ["size", "keys", "hasOnly", "diff", "affectedKeys", "matches", "lower", "exists", "get", "custom", "is"];

    private static RuleExpression Gen(Random rng, int depth)
    {
        if (depth == 0) return rng.Next(2) == 0 ? new LiteralExpression(GenValue(rng, 1)) : new VarExpression(Names[rng.Next(Names.Length)]);
        RuleExpression Sub() => Gen(rng, depth - 1);
        switch (rng.Next(12))
        {
            case 0: return new LiteralExpression(GenValue(rng, 2));
            case 1: return new VarExpression(Names[rng.Next(Names.Length)]);
            case 2: return new MemberExpression(Sub(), Names[rng.Next(Names.Length)]);
            case 3: return new IndexExpression(Sub(), Sub());
            case 4: return new CompareExpression(Sub(), (ComparisonOperator)rng.Next(6), Sub());
            case 5: return new AndExpression([.. Enumerable.Range(0, rng.Next(0, 4)).Select(_ => Sub())]);
            case 6: return new OrExpression([.. Enumerable.Range(0, rng.Next(0, 4)).Select(_ => Sub())]);
            case 7: return new NotExpression(Sub());
            case 8: return new InExpression(Sub(), Sub());
            case 9: return new ConditionalExpression(Sub(), Sub(), Sub());
            default:
                var name = Calls[rng.Next(Calls.Length)];
                if (name == "is") return new CallExpression("is", [Sub(), new LiteralExpression(RuleValue.String(rng.Next(3) == 0 ? "weird" : "string"))]);
                return new CallExpression(name, [.. Enumerable.Range(0, rng.Next(0, 4)).Select(_ => Sub())]);
        }
    }

    // What the text form can't distinguish: nesting of && / || (flattened), single-operand ones, empty ones.
    private static RuleExpression Normalize(RuleExpression e) => e switch
    {
        AndExpression a => Flat(a.Operands, isAnd: true),
        OrExpression o => Flat(o.Operands, isAnd: false),
        MemberExpression m => m with { Target = Normalize(m.Target) },
        IndexExpression ix => new IndexExpression(Normalize(ix.Target), Normalize(ix.Index)),
        CompareExpression c => new CompareExpression(Normalize(c.Left), c.Op, Normalize(c.Right)),
        NotExpression n => new NotExpression(Normalize(n.Operand)),
        InExpression i => new InExpression(Normalize(i.Item), Normalize(i.Collection)),
        ConditionalExpression c => new ConditionalExpression(Normalize(c.Condition), Normalize(c.Then), Normalize(c.Else)),
        CallExpression c => new CallExpression(c.Name, [.. c.Args.Select(Normalize)]),
        _ => e,
    };

    private static RuleExpression Flat(IReadOnlyList<RuleExpression> operands, bool isAnd)
    {
        var flat = new List<RuleExpression>();
        foreach (var op in operands.Select(Normalize))
        {
            if (isAnd && op is AndExpression a) flat.AddRange(a.Operands);
            else if (!isAnd && op is OrExpression o) flat.AddRange(o.Operands);
            else flat.Add(op);
        }
        return flat.Count switch
        {
            0 => new LiteralExpression(RuleValue.Bool(isAnd)),
            1 => flat[0],
            _ => isAnd ? new AndExpression(flat) : new OrExpression(flat),
        };
    }
}
