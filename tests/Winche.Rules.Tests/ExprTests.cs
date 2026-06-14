using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Xunit;

namespace Winche.Rules.Tests;

public class ExprTests
{
    private static void RoundTrips(RuleExpression e) =>
        Assert.Equal(e, RuleJson.DeserializeExpr(RuleJson.Serialize(e)));

    [Fact]
    public void OwnerOrPublic_RoundTrips()
    {
        var e = Expr.Any(
            Expr.Resource("data", "isPublic").Eq(true),
            Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")));
        RoundTrips(e);
    }

    [Fact]
    public void Lookup_Exists_And_Get_RoundTrip()
    {
        // exists(/teams/x) && get(/teams/x).data.active == true
        var e = Expr.All(
            Expr.Exists(Expr.Const("teams/x")),
            Expr.Get(Expr.Const("teams/x")).Field("data").Field("active").Eq(true));
        RoundTrips(e);
    }

    [Fact]
    public void Comparisons_Membership_Conditional_RoundTrip()
    {
        RoundTrips(Expr.Resource("data", "age").Ge(Expr.Const(18)));
        RoundTrips(Expr.Auth("uid").In(Expr.Resource("data", "members")));
        RoundTrips(new ConditionalExpression(Expr.Auth().Ne(Expr.Null), Expr.Const(true), Expr.Const(false)));
        RoundTrips(Expr.Resource("data", "role").Ne(Expr.Const("banned")).Not());
    }

    [Fact]
    public void Json_CarriesKindDiscriminators()
    {
        var json = RuleJson.Serialize(Expr.Resource("data", "ownerId").Eq(Expr.Auth("uid")));
        Assert.Contains("\"kind\":\"compare\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"member\"", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"var\"", json, StringComparison.Ordinal);
    }
}
