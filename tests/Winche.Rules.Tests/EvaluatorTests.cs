using Winche.Values;
using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class EvaluatorTests
{
    // ── Context builders ───────────────────────────────────────────────────────

    private static MapValue Map(params (string K, Value V)[] e) =>
        new MapValue(e.ToDictionary(x => x.K, x => x.V));

    private static Value Resource(Value data) => Map(("data", data), ("__name__", new ReferenceValue("c/x")));
    private static Value Auth(string uid) => Map(("uid", new StringValue(uid)));

    private static RuleContext Ctx(Value? resource = null, Value? auth = null, IRuleResourceProvider? docs = null) =>
        new()
        {
            Resource = resource ?? RuleValues.Null,
            Request = Map(("method", new StringValue("get")), ("auth", auth ?? RuleValues.Null), ("resource", RuleValues.Null)),
            Provider = docs,
        };

    private static Task<bool> Eval(RuleExpression c, RuleContext ctx) => RuleEvaluator.EvaluateAsync(c, ctx);

    // ── Core ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_Allows_Owner_Denies_Other()
    {
        var cond = Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid"));
        Assert.True(await Eval(cond, Ctx(Resource(Map(("ownerId", new StringValue("me")))), Auth("me"))));
        Assert.False(await Eval(cond, Ctx(Resource(Map(("ownerId", new StringValue("me")))), Auth("other"))));
    }

    [Fact]
    public async Task PublicOrOwner()
    {
        var cond = Expr.Any(
            Expr.Resource("data", "isPublic").Eq(true),
            Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")));

        Assert.True(await Eval(cond, Ctx(Resource(Map(("isPublic", RuleValues.True), ("ownerId", new StringValue("x")))), Auth("me"))));
        Assert.False(await Eval(cond, Ctx(Resource(Map(("isPublic", RuleValues.False), ("ownerId", new StringValue("x")))), Auth("me"))));
    }

    [Fact]
    public async Task MissingField_Denies()
    {
        // resource.data has no ownerId → evaluation error → deny (not throw)
        var cond = Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid"));
        Assert.False(await Eval(cond, Ctx(Resource(Map(("name", new StringValue("x")))), Auth("me"))));
    }

    [Fact]
    public async Task NullAuth_IsGuardable()
    {
        // request.auth != null && resource.data.ownerId == request.auth.uid
        var cond = Expr.All(
            Expr.Auth().Ne(Expr.Null),
            Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")));

        Assert.False(await Eval(cond, Ctx(Resource(Map(("ownerId", new StringValue("me")))), auth: null)));   // unauth → short-circuits false
        Assert.True(await Eval(cond, Ctx(Resource(Map(("ownerId", new StringValue("me")))), Auth("me"))));
    }

    [Fact]
    public async Task Membership_And_Size()
    {
        var members = new ArrayValue([new StringValue("a"), new StringValue("me")]);
        Assert.True(await Eval(Expr.Auth("uid").In(Expr.Resource("data", "members")), Ctx(Resource(Map(("members", members))), Auth("me"))));
        Assert.True(await Eval(Expr.Size(Expr.Resource("data", "members")).Eq(Expr.Const(2)), Ctx(Resource(Map(("members", members))), Auth("me"))));
    }

    // ── Cross-document get()/exists() ──────────────────────────────────────────

    private sealed class FakeDocs(Dictionary<string, Value> docs) : IRuleResourceProvider
    {
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(docs.ContainsKey(path));
        public Task<Value> GetAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(docs.TryGetValue(path, out var v) ? v : RuleValues.Null);
    }

    [Fact]
    public async Task GetAndExists_LookUpAnotherDocument()
    {
        var docs = new FakeDocs(new Dictionary<string, Value>
        {
            ["teams/t1"] = Resource(Map(("active", RuleValues.True))),
        });
        // exists(teams/t1) && get(teams/t1).data.active == true
        var cond = Expr.All(
            Expr.Exists(Expr.Const("teams/t1")),
            Expr.Get(Expr.Const("teams/t1")).Field("data").Field("active").Eq(true));

        Assert.True(await Eval(cond, Ctx(auth: Auth("me"), docs: docs)));

        // missing team → exists() false → denies
        Assert.False(await Eval(
            Expr.Exists(Expr.Const("teams/none")).And(Expr.Const(true)),
            Ctx(auth: Auth("me"), docs: docs)));
    }
}
