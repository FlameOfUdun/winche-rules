namespace Winche.Rules;

/// <summary>
/// How rules see <see cref="Value"/>s: their type names, equality and order. Values are Winche.Core's, the same the
/// database stores, so a rule compares exactly as a query orders.
/// </summary>
public static class RuleValues
{
    /// <summary>The null value.</summary>
    public static readonly NullValue Null = new();

    /// <summary>The value true.</summary>
    public static readonly BooleanValue True = new(true);

    /// <summary>The value false.</summary>
    public static readonly BooleanValue False = new(false);

    /// <summary>The boolean value for <paramref name="b"/>.</summary>
    public static BooleanValue Bool(bool b) => b ? True : False;

    /// <summary>A list of strings.</summary>
    public static ArrayValue Strings(IEnumerable<string> items) => new([.. items.Select(s => (Value)new StringValue(s))]);

    /// <summary>
    /// The rules-language name of <paramref name="v"/>'s type: <c>string</c>, <c>int</c>, <c>float</c>, <c>bool</c>,
    /// <c>null</c>, <c>map</c>, <c>list</c>, <c>timestamp</c>, <c>bytes</c>, <c>path</c> (a document reference),
    /// <c>fileRef</c>, <c>userRef</c> or <c>latlng</c>.
    /// </summary>
    public static string TypeName(Value v) => v switch
    {
        NullValue => "null",
        BooleanValue => "bool",
        IntegerValue => "int",
        DoubleValue => "float",
        StringValue => "string",
        BytesValue => "bytes",
        TimestampValue => "timestamp",
        ReferenceValue => "path",
        FileRefValue => "fileRef",
        UserRefValue => "userRef",
        GeoPointValue => "latlng",
        ArrayValue => "list",
        MapValue => "map",
        _ => v.GetType().Name,
    };

    /// <summary>
    /// Whether two values are equal: same type (integers and floats are both numbers) and same value. A file or user
    /// reference also equals its path or uid as a string, so <c>resource.data.owner == request.auth.uid</c> holds
    /// whether <c>owner</c> is stored as a user reference or a string.
    /// </summary>
    public static bool AreEqual(Value a, Value b) => (a, b) switch
    {
        (UserRefValue u, StringValue s) => u.Uid == s.Value,
        (StringValue s, UserRefValue u) => u.Uid == s.Value,
        (FileRefValue f, StringValue s) => f.Path == s.Value,
        (StringValue s, FileRefValue f) => f.Path == s.Value,
        _ => a.Rank == b.Rank && ValueComparer.Instance.Compare(a, b) == 0,
    };

    /// <summary>
    /// Orders two values of the same type (numbers together) the way the database orders them; false for values of
    /// different types or NaN, which have no order.
    /// </summary>
    public static bool TryCompare(Value a, Value b, out int result)
    {
        result = 0;
        if (a.Rank != b.Rank || a.Rank == TypeRank.NaN) return false;
        result = ValueComparer.Instance.Compare(a, b);
        return true;
    }

    /// <summary>
    /// The <c>request</c> a rule sees: <c>auth</c> (null when signed out, else <c>{uid, token}</c> with every claim in
    /// <c>token</c>), <c>resource</c> (the incoming data, or null), <c>method</c> and <c>time</c>.
    /// </summary>
    public static MapValue Request(IReadOnlyDictionary<string, object?>? claims, string method, Value resource, DateTimeOffset? now = null) =>
        new(new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["auth"] = Auth(claims),
            ["resource"] = resource,
            ["method"] = new StringValue(method),
            ["time"] = new TimestampValue(now ?? DateTimeOffset.UtcNow),
        });

    /// <summary><c>request.auth</c> for <paramref name="claims"/>: null when there are none.</summary>
    public static Value Auth(IReadOnlyDictionary<string, object?>? claims)
    {
        if (claims is null || claims.Count == 0) return Null;
        return new MapValue(new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["uid"] = claims.TryGetValue("uid", out var uid) && uid is not null ? new StringValue(uid.ToString()!) : Null,
            ["token"] = Value.FromObject(claims),
        });
    }
}
