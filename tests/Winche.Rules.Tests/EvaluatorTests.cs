using Winche.Rules;
using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class EvaluatorTests
{
    // ── Context builders ───────────────────────────────────────────────────────

    private static RuleValue Map(params (string K, RuleValue V)[] e) =>
        RuleValue.Map(e.ToDictionary(x => x.K, x => x.V));

    private static RuleValue Resource(RuleValue data) => Map(("data", data), ("__name__", RuleValue.Path("c/x")));
    private static RuleValue Auth(string uid) => Map(("uid", RuleValue.String(uid)));

    private static RuleContext Ctx(RuleValue? resource = null, RuleValue? auth = null, IDocumentSource? docs = null) =>
        new()
        {
            Resource = resource ?? RuleValue.Null,
            Request = Map(("method", RuleValue.String("get")), ("auth", auth ?? RuleValue.Null), ("resource", RuleValue.Null)),
            Documents = docs,
        };

    private static Task<bool> Eval(RuleExpr c, RuleContext ctx) => RuleEvaluator.EvaluateAsync(c, ctx);

    // ── Core ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_Allows_Owner_Denies_Other()
    {
        var cond = Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid"));
        Assert.True(await Eval(cond, Ctx(Resource(Map(("ownerId", RuleValue.String("me")))), Auth("me"))));
        Assert.False(await Eval(cond, Ctx(Resource(Map(("ownerId", RuleValue.String("me")))), Auth("other"))));
    }

    [Fact]
    public async Task PublicOrOwner()
    {
        var cond = Expr.Any(
            Expr.Resource("data", "isPublic").Eq(true),
            Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")));

        Assert.True(await Eval(cond, Ctx(Resource(Map(("isPublic", RuleValue.Bool(true)), ("ownerId", RuleValue.String("x")))), Auth("me"))));
        Assert.False(await Eval(cond, Ctx(Resource(Map(("isPublic", RuleValue.Bool(false)), ("ownerId", RuleValue.String("x")))), Auth("me"))));
    }

    [Fact]
    public async Task MissingField_Denies()
    {
        // resource.data has no ownerId → evaluation error → deny (not throw)
        var cond = Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid"));
        Assert.False(await Eval(cond, Ctx(Resource(Map(("name", RuleValue.String("x")))), Auth("me"))));
    }

    [Fact]
    public async Task NullAuth_IsGuardable()
    {
        // request.auth != null && resource.data.ownerId == request.auth.uid
        var cond = Expr.All(
            Expr.Auth().Ne(Expr.Null),
            Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")));

        Assert.False(await Eval(cond, Ctx(Resource(Map(("ownerId", RuleValue.String("me")))), auth: null)));   // unauth → short-circuits false
        Assert.True(await Eval(cond, Ctx(Resource(Map(("ownerId", RuleValue.String("me")))), Auth("me"))));
    }

    [Fact]
    public async Task Membership_And_Size()
    {
        var members = RuleValue.List([RuleValue.String("a"), RuleValue.String("me")]);
        Assert.True(await Eval(Expr.Auth("uid").In(Expr.Resource("data", "members")), Ctx(Resource(Map(("members", members))), Auth("me"))));
        Assert.True(await Eval(Expr.Size(Expr.Resource("data", "members")).Eq(Expr.Const(2)), Ctx(Resource(Map(("members", members))), Auth("me"))));
    }

    // ── Cross-document get()/exists() ──────────────────────────────────────────

    private sealed class FakeDocs(Dictionary<string, RuleValue> docs) : IDocumentSource
    {
        public Task<bool> ExistsAsync(string path, CancellationToken ct = default) => Task.FromResult(docs.ContainsKey(path));
        public Task<RuleValue> GetAsync(string path, CancellationToken ct = default) =>
            Task.FromResult(docs.TryGetValue(path, out var v) ? v : RuleValue.Null);
    }

    [Fact]
    public async Task GetAndExists_LookUpAnotherDocument()
    {
        var docs = new FakeDocs(new Dictionary<string, RuleValue>
        {
            ["teams/t1"] = Resource(Map(("active", RuleValue.Bool(true)))),
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
