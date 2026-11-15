using System.Text.Json.Serialization;

namespace Winche.Rules.Expressions;

/// <summary>A comparison operator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComparisonOperator>))]
public enum ComparisonOperator
{
    /// <summary>Equal.</summary>
    Eq,
    /// <summary>Not equal.</summary>
    Ne,
    /// <summary>Less than.</summary>
    Lt,
    /// <summary>Less than or equal.</summary>
    Le,
    /// <summary>Greater than.</summary>
    Gt,
    /// <summary>Greater than or equal.</summary>
    Ge,
}

/// <summary>
/// A node in a rule condition expression. Pure data, serializable (polymorphic on the <c>kind</c>
/// discriminator), and evaluated against an evaluation context (resource/request/auth/params).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LiteralExpression), "literal")]
[JsonDerivedType(typeof(VarExpression), "variable")]
[JsonDerivedType(typeof(MemberExpression), "member")]
[JsonDerivedType(typeof(IndexExpression), "index")]
[JsonDerivedType(typeof(CompareExpression), "comparison")]
[JsonDerivedType(typeof(AndExpression), "and")]
[JsonDerivedType(typeof(OrExpression), "or")]
[JsonDerivedType(typeof(NotExpression), "not")]
[JsonDerivedType(typeof(InExpression), "in")]
[JsonDerivedType(typeof(ConditionalExpression), "conditional")]
[JsonDerivedType(typeof(CallExpression), "call")]
public abstract record RuleExpression;

/// <summary>A constant value.</summary>
/// <param name="Value">The literal value.</param>
public sealed record LiteralExpression([property: JsonConverter(typeof(Json.RuleLiteralJsonConverter))] Value Value) : RuleExpression;

/// <summary>A root variable: <c>request</c>, <c>resource</c>, or a path-pattern capture (e.g. <c>userId</c>).</summary>
/// <param name="Name">The variable name.</param>
public sealed record VarExpression(string Name) : RuleExpression;

/// <summary>Member access <c>target.Name</c> (a field of a map).</summary>
/// <param name="Target">The target expression.</param>
/// <param name="Name">The member/field name.</param>
public sealed record MemberExpression(RuleExpression Target, string Name) : RuleExpression;

/// <summary>Index access <c>target[Index]</c> (a list element or map entry).</summary>
/// <param name="Target">The target expression.</param>
/// <param name="Index">The index/key expression.</param>
public sealed record IndexExpression(RuleExpression Target, RuleExpression Index) : RuleExpression;

/// <summary>A comparison <c>Left Op Right</c>.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Op">The operator.</param>
/// <param name="Right">The right operand.</param>
public sealed record CompareExpression(RuleExpression Left, ComparisonOperator Op, RuleExpression Right) : RuleExpression;

/// <summary>Logical AND of all operands (vacuously true when empty).</summary>
/// <param name="Operands">The operands.</param>
public sealed record AndExpression(IReadOnlyList<RuleExpression> Operands) : RuleExpression
{
    /// <inheritdoc/>
    public bool Equals(AndExpression? other) => other is not null && Operands.SequenceEqual(other.Operands);
    /// <inheritdoc/>
    public override int GetHashCode() => ExpressionList.Hash(Operands);
}

/// <summary>Logical OR of all operands (vacuously false when empty).</summary>
/// <param name="Operands">The operands.</param>
public sealed record OrExpression(IReadOnlyList<RuleExpression> Operands) : RuleExpression
{
    /// <inheritdoc/>
    public bool Equals(OrExpression? other) => other is not null && Operands.SequenceEqual(other.Operands);
    /// <inheritdoc/>
    public override int GetHashCode() => ExpressionList.Hash(Operands);
}

/// <summary>Logical negation.</summary>
/// <param name="Operand">The operand.</param>
public sealed record NotExpression(RuleExpression Operand) : RuleExpression;

/// <summary>Membership test <c>Item in Collection</c> (list element or map key).</summary>
/// <param name="Item">The item to look for.</param>
/// <param name="Collection">The list or map.</param>
public sealed record InExpression(RuleExpression Item, RuleExpression Collection) : RuleExpression;

/// <summary>A ternary <c>Condition ? Then : Else</c>.</summary>
/// <param name="Condition">The condition.</param>
/// <param name="Then">Value when true.</param>
/// <param name="Else">Value when false.</param>
public sealed record ConditionalExpression(RuleExpression Condition, RuleExpression Then, RuleExpression Else) : RuleExpression;

/// <summary>A built-in function call, e.g. <c>exists(path)</c>, <c>get(path)</c>, <c>size(x)</c>.</summary>
/// <param name="Name">The function name.</param>
/// <param name="Args">The arguments.</param>
public sealed record CallExpression(string Name, IReadOnlyList<RuleExpression> Args) : RuleExpression
{
    /// <inheritdoc/>
    public bool Equals(CallExpression? other) =>
        other is not null && Name == other.Name && Args.SequenceEqual(other.Args);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Name, ExpressionList.Hash(Args));
}

internal static class ExpressionList
{
    public static int Hash(IReadOnlyList<RuleExpression> items)
    {
        var hash = new HashCode();
        foreach (var i in items) hash.Add(i);
        return hash.ToHashCode();
    }
}
