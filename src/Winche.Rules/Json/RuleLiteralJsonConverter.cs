using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winche.Rules.Json;

/// <summary>
/// How a rule's literal <see cref="Value"/> is stored in ruleset JSON. Unambiguous kinds use natural JSON
/// (null/bool/string/list); kinds JSON can't represent faithfully are tagged single-key objects:
/// <c>{"$integer":"5"}</c>, <c>{"$double":1.5}</c>, <c>{"$timestamp":"…"}</c>, <c>{"$bytes":"base64"}</c>,
/// <c>{"$path":"…"}</c>, <c>{"$latlng":[lat,lng]}</c>. A plain object (no <c>$</c>-tag) is a map, so map field names
/// must not begin with <c>$</c>. (The database's own wire format is a different, typed shape; this one keeps rules
/// readable.)
/// </summary>
public sealed class RuleLiteralJsonConverter : JsonConverter<Value>
{
    /// <inheritdoc/>
    public override bool HandleNull => true;

    /// <summary>Any value record (<see cref="MapValue"/>, <see cref="StringValue"/>, …), not only <see cref="Value"/>.</summary>
    public override bool CanConvert(Type typeToConvert) => typeof(Value).IsAssignableFrom(typeToConvert);

    /// <inheritdoc/>
    public override Value Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.TokenType switch
    {
        JsonTokenType.Null => RuleValues.Null,
        JsonTokenType.True => RuleValues.True,
        JsonTokenType.False => RuleValues.False,
        JsonTokenType.String => new StringValue(reader.GetString()!),
        JsonTokenType.Number => reader.TryGetInt64(out var l) ? new IntegerValue(l) : new DoubleValue(reader.GetDouble()),
        JsonTokenType.StartArray => ReadList(ref reader, options),
        JsonTokenType.StartObject => ReadObject(ref reader, options),
        _ => throw new JsonException($"Unexpected token {reader.TokenType} for a rule value."),
    };

    private ArrayValue ReadList(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var items = new List<Value>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return new ArrayValue(items);
            items.Add(Read(ref reader, typeof(Value), options));
        }
        throw new JsonException("Unterminated list in a rule value.");
    }

    private Value ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (!reader.Read()) throw new JsonException("Unterminated object in a rule value.");
        var map = new Dictionary<string, Value>(StringComparer.Ordinal);
        if (reader.TokenType == JsonTokenType.EndObject) return new MapValue(map);

        var first = reader.GetString()!;
        reader.Read();
        if (first.StartsWith('$'))
        {
            Value scalar = first switch
            {
                "$integer" => new IntegerValue(long.Parse(reader.GetString()!, CultureInfo.InvariantCulture)),
                "$double" => new DoubleValue(reader.GetDouble()),
                "$timestamp" => new TimestampValue(DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
                "$bytes" => new BytesValue(Convert.FromBase64String(reader.GetString()!)),
                "$path" => new ReferenceValue(reader.GetString()!),
                "$fileRef" => new FileRefValue(reader.GetString()!),
                "$userRef" => new UserRefValue(reader.GetString()!),
                "$latlng" => ReadLatLng(ref reader),
                _ => throw new JsonException($"Unknown rule value tag '{first}'."),
            };
            reader.Read();
            return scalar;
        }

        map[first] = Read(ref reader, typeof(Value), options);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return new MapValue(map);
            var name = reader.GetString()!;
            reader.Read();
            map[name] = Read(ref reader, typeof(Value), options);
        }
        throw new JsonException("Unterminated map in a rule value.");
    }

    private static GeoPointValue ReadLatLng(ref Utf8JsonReader reader)
    {
        reader.Read();
        var lat = reader.GetDouble();
        reader.Read();
        var lng = reader.GetDouble();
        reader.Read();
        return new GeoPointValue(lat, lng);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Value value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null or NullValue: writer.WriteNullValue(); break;
            case BooleanValue b: writer.WriteBooleanValue(b.Value); break;
            case StringValue s: writer.WriteStringValue(s.Value); break;
            case IntegerValue i: Tagged(writer, "$integer", i.Value.ToString(CultureInfo.InvariantCulture)); break;
            case DoubleValue d:
                writer.WriteStartObject();
                writer.WriteNumber("$double", d.Value);
                writer.WriteEndObject();
                break;
            case TimestampValue t: Tagged(writer, "$timestamp", t.Value.ToString("O", CultureInfo.InvariantCulture)); break;
            case BytesValue b: Tagged(writer, "$bytes", Convert.ToBase64String(b.Value)); break;
            case ReferenceValue r: Tagged(writer, "$path", r.Path); break;
            case FileRefValue f: Tagged(writer, "$fileRef", f.Path); break;
            case UserRefValue u: Tagged(writer, "$userRef", u.Uid); break;
            case GeoPointValue g:
                writer.WriteStartObject();
                writer.WriteStartArray("$latlng");
                writer.WriteNumberValue(g.Latitude);
                writer.WriteNumberValue(g.Longitude);
                writer.WriteEndArray();
                writer.WriteEndObject();
                break;
            case ArrayValue a:
                writer.WriteStartArray();
                foreach (var e in a.Values) Write(writer, e, options);
                writer.WriteEndArray();
                break;
            case MapValue m:
                writer.WriteStartObject();
                foreach (var (k, v) in m.Fields) { writer.WritePropertyName(k); Write(writer, v, options); }
                writer.WriteEndObject();
                break;
            default:
                throw new JsonException($"A rule can't hold a {value.GetType().Name}.");
        }
    }

    private static void Tagged(Utf8JsonWriter writer, string tag, string value)
    {
        writer.WriteStartObject();
        writer.WriteString(tag, value);
        writer.WriteEndObject();
    }
}
