using System.Text.Json;
using Winche.Rules.Expressions;

namespace Winche.Rules.Json;

/// <summary>Canonical JSON (de)serialization for rule expressions — rules-as-data.</summary>
public static class RuleJson
{
    /// <summary>The serializer options the rule model round-trips with.</summary>
    public static JsonSerializerOptions Options { get; } = new();

    /// <summary>Serializes a condition expression to JSON.</summary>
    public static string Serialize(RuleExpr expr) => JsonSerializer.Serialize(expr, Options);

    /// <summary>Deserializes a condition expression from JSON.</summary>
    public static RuleExpr DeserializeExpr(string json) =>
        JsonSerializer.Deserialize<RuleExpr>(json, Options) ?? throw new JsonException("Expression JSON deserialized to null.");

    /// <summary>Serializes a whole ruleset to JSON (rules-as-data).</summary>
    public static string Serialize(Ruleset ruleset) => JsonSerializer.Serialize(ruleset, Options);

    /// <summary>Deserializes a ruleset from JSON.</summary>
    public static Ruleset DeserializeRuleset(string json) =>
        JsonSerializer.Deserialize<Ruleset>(json, Options) ?? throw new JsonException("Ruleset JSON deserialized to null.");
}
