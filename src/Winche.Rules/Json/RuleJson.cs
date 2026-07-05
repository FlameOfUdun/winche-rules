using System.Text.Json;
using Winche.Rules.Expressions;

namespace Winche.Rules.Json;

/// <summary>
/// Canonical JSON (de)serialization for rule expressions — rules-as-data.
/// </summary>
public static class RuleJson
{
    /// <summary>
    /// The serializer options the rule model round-trips with.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new();

    /// <summary>
    /// Serializes a condition expression to JSON.
    /// </summary>
    public static string Serialize(RuleExpression expr) => JsonSerializer.Serialize(expr, Options);

    /// <summary>
    /// Deserializes a condition expression from JSON.
    /// </summary>
    public static RuleExpression DeserializeExpr(string json) => JsonSerializer.Deserialize<RuleExpression>(json, Options) ?? throw new JsonException("Expression JSON deserialized to null.");

    /// <summary>
    /// Serializes a whole ruleset to JSON (rules-as-data).
    /// </summary>
    public static string Serialize(RuleSet ruleset) => JsonSerializer.Serialize(ruleset, Options);

    /// <summary>
    /// Deserializes a ruleset from JSON.
    /// </summary>
    public static RuleSet DeserializeRuleSet(string json) => JsonSerializer.Deserialize<RuleSet>(json, Options) ?? throw new JsonException("RuleSet JSON deserialized to null.");
}
