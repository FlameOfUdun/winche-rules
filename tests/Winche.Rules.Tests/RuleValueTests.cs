using System.Text.Json;
using Winche.Rules;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleValueTests
{
    // ── Equality ───────────────────────────────────────────────────────────────

    [Fact]
    public void IntAndDouble_AreEqualByNumericValue()
    {
        Assert.Equal(RuleValue.Int(1), RuleValue.Double(1.0));
        Assert.NotEqual(RuleValue.Int(1), RuleValue.Int(2));
        Assert.NotEqual(RuleValue.Int(1), RuleValue.String("1"));
    }

    [Fact]
    public void List_And_Map_AreDeepEqual()
    {
        var a = RuleValue.List([RuleValue.Int(1), RuleValue.String("x")]);
        var b = RuleValue.List([RuleValue.Int(1), RuleValue.String("x")]);
        Assert.Equal(a, b);

        var m1 = RuleValue.Map(new Dictionary<string, RuleValue> { ["k"] = RuleValue.Bool(true) });
        var m2 = RuleValue.Map(new Dictionary<string, RuleValue> { ["k"] = RuleValue.Bool(true) });
        Assert.Equal(m1, m2);
        Assert.Equal(m1.GetHashCode(), m2.GetHashCode());
    }

    // ── Comparison ─────────────────────────────────────────────────────────────

    [Fact]
    public void Numbers_CompareAcrossIntAndDouble()
    {
        Assert.True(RuleValue.Int(1).TryCompareTo(RuleValue.Double(2.5), out var c) && c < 0);
        Assert.True(RuleValue.Double(3.0).TryCompareTo(RuleValue.Int(3), out var e) && e == 0);
    }

    [Fact]
    public void Strings_CompareOrdinal_And_CrossKind_IsNotOrderable()
    {
        Assert.True(RuleValue.String("a").TryCompareTo(RuleValue.String("b"), out var c) && c < 0);
        Assert.False(RuleValue.String("1").TryCompareTo(RuleValue.Int(1), out _));   // string vs number
        Assert.False(RuleValue.Null.TryCompareTo(RuleValue.Null, out _));            // null not orderable
    }

    // ── JSON round-trip ────────────────────────────────────────────────────────

    private static void RoundTrips(RuleValue v)
    {
        var json = JsonSerializer.Serialize(v);
        Assert.Equal(v, JsonSerializer.Deserialize<RuleValue>(json));
    }

    [Fact]
    public void Json_RoundTrips_AllKinds()
    {
        RoundTrips(RuleValue.Null);
        RoundTrips(RuleValue.Bool(true));
        RoundTrips(RuleValue.Int(42));
        RoundTrips(RuleValue.Double(1.5));
        RoundTrips(RuleValue.String("hello"));
        RoundTrips(RuleValue.Path("/users/u1"));
        RoundTrips(RuleValue.Timestamp(new DateTimeOffset(2024, 6, 9, 12, 0, 0, TimeSpan.Zero)));
        RoundTrips(RuleValue.Bytes([1, 2, 3, 255]));
        RoundTrips(RuleValue.List([RuleValue.Int(1), RuleValue.String("two"), RuleValue.Bool(false)]));
        RoundTrips(RuleValue.Map(new Dictionary<string, RuleValue>
        {
            ["ownerId"] = RuleValue.String("u1"),
            ["count"] = RuleValue.Int(3),
            ["nested"] = RuleValue.Map(new Dictionary<string, RuleValue> { ["x"] = RuleValue.Double(2.0) }),
        }));
    }

    [Fact]
    public void Json_DistinguishesIntFromDouble()
    {
        Assert.Equal("{\"$integer\":\"5\"}", JsonSerializer.Serialize(RuleValue.Int(5)));
        Assert.Equal(RuleValueKind.Int, JsonSerializer.Deserialize<RuleValue>("{\"$integer\":\"5\"}").Kind);
        Assert.Equal(RuleValueKind.Double, JsonSerializer.Deserialize<RuleValue>("{\"$double\":5}").Kind);
    }

    [Fact]
    public void Json_PlainObjectIsAMap()
    {
        var v = JsonSerializer.Deserialize<RuleValue>("{\"a\":true,\"b\":\"x\"}");
        Assert.Equal(RuleValueKind.Map, v.Kind);
        Assert.Equal(RuleValue.Bool(true), v.AsMap["a"]);
    }
}
