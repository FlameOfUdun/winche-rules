using System.Globalization;
using System.Text.Json.Serialization;
using Winche.Rules.Json;

namespace Winche.Rules;

/// <summary>The kind of a <see cref="RuleValue"/>, mirroring Firestore's value types.</summary>
public enum RuleValueKind
{
    /// <summary>An explicit null (present, but null).</summary>
    Null,
    /// <summary>A boolean.</summary>
    Bool,
    /// <summary>A 64-bit integer.</summary>
    Int,
    /// <summary>A double-precision float.</summary>
    Double,
    /// <summary>A string.</summary>
    String,
    /// <summary>Raw bytes.</summary>
    Bytes,
    /// <summary>A timestamp.</summary>
    Timestamp,
    /// <summary>A document reference / path.</summary>
    Path,
    /// <summary>An ordered list of values.</summary>
    List,
    /// <summary>A string-keyed map of values (a document's fields are a map).</summary>
    Map,
}

/// <summary>
/// The neutral, JSON-ish value the rules engine operates on — independent of any storage type. A document
/// is a <see cref="Map"/> of fields. Each consumer adapts its own resource type to <see cref="RuleValue"/>.
/// Numbers compare across <see cref="RuleValueKind.Int"/>/<see cref="RuleValueKind.Double"/> by numeric
/// value (Firestore semantics); comparison across other kinds is undefined.
/// </summary>
[JsonConverter(typeof(RuleValueJsonConverter))]
public readonly struct RuleValue : IEquatable<RuleValue>
{
    private readonly object? _payload;   // bool | long | double | string | byte[] | DateTimeOffset | IReadOnlyList<RuleValue> | IReadOnlyDictionary<string, RuleValue> | null

    private RuleValue(RuleValueKind kind, object? payload)
    {
        Kind = kind;
        _payload = payload;
    }

    /// <summary>The value's kind.</summary>
    public RuleValueKind Kind { get; }

    // ── Factories ─────────────────────────────────────────────────────────────

    /// <summary>The null value.</summary>
    public static readonly RuleValue Null = new(RuleValueKind.Null, null);
    /// <summary>Creates a boolean value.</summary>
    public static RuleValue Bool(bool v) => new(RuleValueKind.Bool, v);
    /// <summary>Creates an integer value.</summary>
    public static RuleValue Int(long v) => new(RuleValueKind.Int, v);
    /// <summary>Creates a double value.</summary>
    public static RuleValue Double(double v) => new(RuleValueKind.Double, v);
    /// <summary>Creates a string value.</summary>
    public static RuleValue String(string v) => new(RuleValueKind.String, v);
    /// <summary>Creates a bytes value.</summary>
    public static RuleValue Bytes(byte[] v) => new(RuleValueKind.Bytes, v);
    /// <summary>Creates a timestamp value.</summary>
    public static RuleValue Timestamp(DateTimeOffset v) => new(RuleValueKind.Timestamp, v);
    /// <summary>Creates a document reference / path value.</summary>
    public static RuleValue Path(string v) => new(RuleValueKind.Path, v);
    /// <summary>Creates a list value.</summary>
    public static RuleValue List(IReadOnlyList<RuleValue> v) => new(RuleValueKind.List, v);
    /// <summary>Creates a map value.</summary>
    public static RuleValue Map(IReadOnlyDictionary<string, RuleValue> v) => new(RuleValueKind.Map, v);

    // ── Accessors ─────────────────────────────────────────────────────────────

    /// <summary>True when the kind is <see cref="RuleValueKind.Null"/>.</summary>
    public bool IsNull => Kind == RuleValueKind.Null;
    /// <summary>True for <see cref="RuleValueKind.Int"/> or <see cref="RuleValueKind.Double"/>.</summary>
    public bool IsNumber => Kind is RuleValueKind.Int or RuleValueKind.Double;

    /// <summary>The boolean payload.</summary>
    public bool AsBool => (bool)_payload!;
    /// <summary>The integer payload.</summary>
    public long AsInt => (long)_payload!;
    /// <summary>The double payload.</summary>
    public double AsDouble => (double)_payload!;
    /// <summary>The numeric payload as a double (works for Int and Double).</summary>
    public double AsNumber => Kind == RuleValueKind.Int ? (long)_payload! : (double)_payload!;
    /// <summary>The string payload.</summary>
    public string AsString => (string)_payload!;
    /// <summary>The bytes payload.</summary>
    public byte[] AsBytes => (byte[])_payload!;
    /// <summary>The timestamp payload.</summary>
    public DateTimeOffset AsTimestamp => (DateTimeOffset)_payload!;
    /// <summary>The path payload.</summary>
    public string AsPath => (string)_payload!;
    /// <summary>The list payload.</summary>
    public IReadOnlyList<RuleValue> AsList => (IReadOnlyList<RuleValue>)_payload!;
    /// <summary>The map payload.</summary>
    public IReadOnlyDictionary<string, RuleValue> AsMap => (IReadOnlyDictionary<string, RuleValue>)_payload!;

    // ── Comparison ────────────────────────────────────────────────────────────

    /// <summary>
    /// Orders this value against <paramref name="other"/>. Returns <c>false</c> (un-orderable) for null/list/map
    /// or across non-numeric different kinds; numbers compare across int/double.
    /// </summary>
    public bool TryCompareTo(RuleValue other, out int result)
    {
        result = 0;
        if (IsNumber && other.IsNumber) { result = AsNumber.CompareTo(other.AsNumber); return true; }
        if (Kind != other.Kind) return false;
        switch (Kind)
        {
            case RuleValueKind.Bool: result = AsBool.CompareTo(other.AsBool); return true;
            case RuleValueKind.String: result = string.CompareOrdinal(AsString, other.AsString); return true;
            case RuleValueKind.Path: result = string.CompareOrdinal(AsPath, other.AsPath); return true;
            case RuleValueKind.Timestamp: result = AsTimestamp.CompareTo(other.AsTimestamp); return true;
            case RuleValueKind.Bytes: result = AsBytes.AsSpan().SequenceCompareTo(other.AsBytes); return true;
            default: return false;   // Null, List, Map are not orderable
        }
    }

    /// <inheritdoc/>
    public bool Equals(RuleValue other)
    {
        if (IsNumber && other.IsNumber) return AsNumber.Equals(other.AsNumber);
        if (Kind != other.Kind) return false;
        return Kind switch
        {
            RuleValueKind.Null => true,
            RuleValueKind.Bool => AsBool == other.AsBool,
            RuleValueKind.String => string.Equals(AsString, other.AsString, StringComparison.Ordinal),
            RuleValueKind.Path => string.Equals(AsPath, other.AsPath, StringComparison.Ordinal),
            RuleValueKind.Timestamp => AsTimestamp.Equals(other.AsTimestamp),
            RuleValueKind.Bytes => AsBytes.AsSpan().SequenceEqual(other.AsBytes),
            RuleValueKind.List => ListEquals(AsList, other.AsList),
            RuleValueKind.Map => MapEquals(AsMap, other.AsMap),
            _ => false,
        };
    }

    private static bool ListEquals(IReadOnlyList<RuleValue> a, IReadOnlyList<RuleValue> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!a[i].Equals(b[i])) return false;
        return true;
    }

    private static bool MapEquals(IReadOnlyDictionary<string, RuleValue> a, IReadOnlyDictionary<string, RuleValue> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
            if (!b.TryGetValue(k, out var bv) || !v.Equals(bv)) return false;
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is RuleValue v && Equals(v);

    /// <summary>Structural equality.</summary>
    public static bool operator ==(RuleValue left, RuleValue right) => left.Equals(right);

    /// <summary>Structural inequality.</summary>
    public static bool operator !=(RuleValue left, RuleValue right) => !left.Equals(right);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        if (IsNumber) return AsNumber.GetHashCode();
        var hash = new HashCode();
        hash.Add(Kind);
        switch (Kind)
        {
            case RuleValueKind.Bool: hash.Add(AsBool); break;
            case RuleValueKind.String: hash.Add(AsString, StringComparer.Ordinal); break;
            case RuleValueKind.Path: hash.Add(AsPath, StringComparer.Ordinal); break;
            case RuleValueKind.Timestamp: hash.Add(AsTimestamp); break;
            case RuleValueKind.Bytes: hash.AddBytes(AsBytes); break;
            case RuleValueKind.List: foreach (var e in AsList) hash.Add(e); break;
            case RuleValueKind.Map: { var x = 0; foreach (var (k, v) in AsMap) x ^= HashCode.Combine(k, v); hash.Add(x); break; }
        }
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        RuleValueKind.Null => "null",
        RuleValueKind.Bool => AsBool ? "true" : "false",
        RuleValueKind.Int => AsInt.ToString(CultureInfo.InvariantCulture),
        RuleValueKind.Double => AsDouble.ToString(CultureInfo.InvariantCulture),
        RuleValueKind.String => $"\"{AsString}\"",
        RuleValueKind.Path => AsPath,
        RuleValueKind.Timestamp => AsTimestamp.ToString("O"),
        RuleValueKind.Bytes => Convert.ToBase64String(AsBytes),
        RuleValueKind.List => $"[{string.Join(", ", AsList)}]",
        RuleValueKind.Map => $"{{{string.Join(", ", AsMap.Select(kv => $"{kv.Key}: {kv.Value}"))}}}",
        _ => "?",
    };
}
