using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winche.Rules.Json;

/// <summary>
/// JSON for <see cref="RuleValue"/>. Unambiguous kinds use natural JSON (null/bool/string/list); kinds JSON
/// can't represent faithfully are tagged single-key objects: <c>{"$int":"5"}</c>, <c>{"$double":1.5}</c>,
/// <c>{"$timestamp":"…"}</c>, <c>{"$bytes":"base64"}</c>, <c>{"$path":"…"}</c>. A plain object (no <c>$</c>-tag) is a
/// map — so map field names must not begin with <c>$</c>.
/// </summary>
public sealed class RuleValueJsonConverter : JsonConverter<RuleValue>
{
    /// <inheritdoc/>
    public override RuleValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => RuleValue.Null,
            JsonTokenType.True => RuleValue.Bool(true),
            JsonTokenType.False => RuleValue.Bool(false),
            JsonTokenType.String => RuleValue.String(reader.GetString()!),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? RuleValue.Int(l) : RuleValue.Double(reader.GetDouble()),
            JsonTokenType.StartArray => ReadList(ref reader, options),
            JsonTokenType.StartObject => ReadObject(ref reader, options),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for a RuleValue."),
        };
    }

    private static RuleValue ReadList(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        var items = new List<RuleValue>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return RuleValue.List(items);
            items.Add(JsonSerializer.Deserialize<RuleValue>(ref reader, options));
        }
        throw new JsonException("Unterminated array in RuleValue.");
    }

    private static RuleValue ReadObject(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        if (!reader.Read()) throw new JsonException("Unterminated object in RuleValue.");
        if (reader.TokenType == JsonTokenType.EndObject)
            return RuleValue.Map(new Dictionary<string, RuleValue>());

        var firstName = reader.GetString()!;
        if (firstName.Length > 0 && firstName[0] == '$')
        {
            reader.Read();
            RuleValue scalar = firstName switch
            {
                "$int" => RuleValue.Int(long.Parse(reader.GetString()!, CultureInfo.InvariantCulture)),
                "$double" => RuleValue.Double(reader.GetDouble()),
                "$timestamp" => RuleValue.Timestamp(DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)),
                "$bytes" => RuleValue.Bytes(Convert.FromBase64String(reader.GetString()!)),
                "$path" => RuleValue.Path(reader.GetString()!),
                _ => throw new JsonException($"Unknown RuleValue tag '{firstName}'."),
            };
            reader.Read();
            return scalar;
        }

        var map = new Dictionary<string, RuleValue>();
        reader.Read(); 
        map[firstName] = JsonSerializer.Deserialize<RuleValue>(ref reader, options);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return RuleValue.Map(map);
            var name = reader.GetString()!;
            reader.Read();
            map[name] = JsonSerializer.Deserialize<RuleValue>(ref reader, options);
        }
        
        throw new JsonException("Unterminated map in RuleValue.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, RuleValue value, JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case RuleValueKind.Null: 
                writer.WriteNullValue(); 
                break;
            case RuleValueKind.Bool: 
                writer.WriteBooleanValue(value.AsBool); 
                break;
            case RuleValueKind.String: 
                writer.WriteStringValue(value.AsString); 
                break;
            case RuleValueKind.Int: 
                WriteTagged(writer, "$int", value.AsInt.ToString(CultureInfo.InvariantCulture));
                break;
            case RuleValueKind.Double: 
                writer.WriteStartObject(); 
                writer.WriteNumber("$double", value.AsDouble);
                writer.WriteEndObject(); 
                break;
            case RuleValueKind.Timestamp: 
                WriteTagged(writer, "$timestamp", value.AsTimestamp.ToString("O", CultureInfo.InvariantCulture)); 
                break;
            case RuleValueKind.Bytes: 
                WriteTagged(writer, "$bytes", Convert.ToBase64String(value.AsBytes)); 
                break;
            case RuleValueKind.Path: 
                WriteTagged(writer, "$path", value.AsPath); 
                break;
            case RuleValueKind.List:
                writer.WriteStartArray();
                foreach (var e in value.AsList) JsonSerializer.Serialize(writer, e, options);
                writer.WriteEndArray();
                break;
            case RuleValueKind.Map:
                writer.WriteStartObject();
                foreach (var (k, v) in value.AsMap) { writer.WritePropertyName(k); JsonSerializer.Serialize(writer, v, options); }
                writer.WriteEndObject();
                break;
            default: 
                throw new JsonException($"Unknown RuleValueKind {value.Kind}.");
        }
    }

    private static void WriteTagged(Utf8JsonWriter writer, string tag, string value)
    {
        writer.WriteStartObject();
        writer.WriteString(tag, value);
        writer.WriteEndObject();
    }
}
