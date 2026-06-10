using System.Text.Json.Serialization;

namespace Winche.Rules;

/// <summary>
/// A document operation, mirroring Firestore's methods. The umbrellas <c>read</c> (= get + list) and
/// <c>write</c> (= create + update + delete) are expressed as sets via <see cref="RuleOperations"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RuleOperation>))]
public enum RuleOperation
{
    /// <summary>Read a single document.</summary>
    Get,
    /// <summary>Query/list a collection.</summary>
    List,
    /// <summary>Create a document that does not yet exist.</summary>
    Create,
    /// <summary>Modify an existing document.</summary>
    Update,
    /// <summary>Delete a document.</summary>
    Delete,
}

/// <summary>Convenience sets for the Firestore method umbrellas.</summary>
public static class RuleOperations
{
    /// <summary>get + list.</summary>
    public static IReadOnlySet<RuleOperation> Read { get; } =
        new HashSet<RuleOperation> { RuleOperation.Get, RuleOperation.List };

    /// <summary>create + update + delete.</summary>
    public static IReadOnlySet<RuleOperation> Write { get; } =
        new HashSet<RuleOperation> { RuleOperation.Create, RuleOperation.Update, RuleOperation.Delete };

    /// <summary>All five operations.</summary>
    public static IReadOnlySet<RuleOperation> All { get; } =
        new HashSet<RuleOperation>(Enum.GetValues<RuleOperation>());

    /// <summary>A set from explicit operations.</summary>
    public static IReadOnlySet<RuleOperation> Of(params RuleOperation[] ops) => new HashSet<RuleOperation>(ops);
}
