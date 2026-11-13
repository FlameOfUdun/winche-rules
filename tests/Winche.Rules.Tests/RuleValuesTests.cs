using System.Text.Json;
using Winche.Rules.Expressions;
using Winche.Rules.Json;
using Winche.Values;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleValuesTests
{
    [Fact]
    public void Numbers_are_equal_and_ordered_across_int_and_float()
    {
        Assert.True(RuleValues.AreEqual(new IntegerValue(1), new DoubleValue(1.0)));
        Assert.False(RuleValues.AreEqual(new IntegerValue(1), new StringValue("1")));
        Assert.True(RuleValues.TryCompare(new IntegerValue(1), new DoubleValue(2.5), out var c) && c < 0);
    }

    [Fact]
    public void Values_of_different_types_or_NaN_have_no_order()
    {
        Assert.False(RuleValues.TryCompare(new StringValue("1"), new IntegerValue(1), out _));
        Assert.False(RuleValues.TryCompare(new DoubleValue(double.NaN), new DoubleValue(1), out _));
        Assert.True(RuleValues.TryCompare(new StringValue("a"), new StringValue("b"), out var c) && c < 0);
    }

    [Fact]
    public void Lists_and_maps_are_deep_equal()
    {
        Assert.True(RuleValues.AreEqual(
            new ArrayValue([new IntegerValue(1), new StringValue("x")]),
            new ArrayValue([new DoubleValue(1), new StringValue("x")])));
        Assert.True(RuleValues.AreEqual(
            new MapValue(new Dictionary<string, Value> { ["k"] = RuleValues.True }),
            new MapValue(new Dictionary<string, Value> { ["k"] = RuleValues.True })));
    }

    [Fact]
    public void Type_names_are_the_rules_language_ones()
    {
        Assert.Equal("int", RuleValues.TypeName(new IntegerValue(1)));
        Assert.Equal("float", RuleValues.TypeName(new DoubleValue(1)));
        Assert.Equal("path", RuleValues.TypeName(new ReferenceValue("a/b")));
        Assert.Equal("latlng", RuleValues.TypeName(new GeoPointValue(1, 2)));
        Assert.Equal("list", RuleValues.TypeName(new ArrayValue([])));
    }

    private static readonly JsonSerializerOptions Literal = new() { Converters = { new RuleLiteralJsonConverter() } };

    private static void RoundTrips(Value v) =>
        Assert.Equal(v, JsonSerializer.Deserialize<Value>(JsonSerializer.Serialize(v, Literal), Literal));

    [Fact]
    public void Literals_round_trip_in_ruleset_json()
    {
        RoundTrips(RuleValues.Null);
        RoundTrips(RuleValues.True);
        RoundTrips(new IntegerValue(42));
        RoundTrips(new DoubleValue(1.5));
        RoundTrips(new StringValue("hello"));
        RoundTrips(new ReferenceValue("users/u1"));
        RoundTrips(new TimestampValue(new DateTimeOffset(2024, 6, 9, 12, 0, 0, TimeSpan.Zero)));
        RoundTrips(new BytesValue([1, 2, 3, 255]));
        RoundTrips(new GeoPointValue(51.5, -0.1));
        RoundTrips(new ArrayValue([new IntegerValue(1), new StringValue("two"), RuleValues.False]));
        RoundTrips(new MapValue(new Dictionary<string, Value>
        {
            ["ownerId"] = new StringValue("u1"),
            ["nested"] = new MapValue(new Dictionary<string, Value> { ["x"] = new DoubleValue(2.0) }),
        }));
    }

    [Fact]
    public void Ruleset_json_keeps_its_readable_literal_format()
    {
        Assert.Equal("""{"$integer":"5"}""", JsonSerializer.Serialize<Value>(new IntegerValue(5), Literal));
        Assert.IsType<DoubleValue>(JsonSerializer.Deserialize<Value>("""{"$double":5}""", Literal));
        Assert.IsType<MapValue>(JsonSerializer.Deserialize<Value>("""{"a":true}""", Literal));
        var map = new MapValue(new Dictionary<string, Value> { ["n"] = new IntegerValue(1) });
        Assert.Equal("""{"n":{"$integer":"1"}}""", JsonSerializer.Serialize(map, Literal));   // a record, not only Value
        Assert.Equal(map, JsonSerializer.Deserialize<MapValue>(JsonSerializer.Serialize(map, Literal), Literal));
        var json = RuleJson.Serialize(new LiteralExpression(new IntegerValue(5)));
        Assert.Contains("\"$integer\":\"5\"", json);
        Assert.Equal(new LiteralExpression(new IntegerValue(5)), RuleJson.DeserializeExpr(json));
    }

    [Fact]
    public void The_request_carries_the_callers_claims()
    {
        var at = new DateTimeOffset(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
        var request = RuleValues.Request(new Dictionary<string, object?> { ["uid"] = "u1", ["admin"] = true }, "get", RuleValues.Null, at);
        var auth = Assert.IsType<MapValue>(request.Fields["auth"]);
        Assert.Equal(new StringValue("u1"), auth.Fields["uid"]);
        Assert.Equal(RuleValues.True, ((MapValue)auth.Fields["token"]).Fields["admin"]);
        Assert.Equal(new TimestampValue(at), request.Fields["time"]);
        Assert.IsType<NullValue>(RuleValues.Request(null, "get", RuleValues.Null).Fields["auth"]);
    }
}
