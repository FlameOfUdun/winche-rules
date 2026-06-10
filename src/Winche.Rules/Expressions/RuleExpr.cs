using System.Text.Json.Serialization;

namespace Winche.Rules.Expressions;

/// <summary>A comparison operator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CompareOp>))]
public enum CompareOp
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
[JsonDerivedType(typeof(LiteralExpr), "lit")]
[JsonDerivedType(typeof(VarExpr), "var")]
[JsonDerivedType(typeof(MemberExpr), "member")]
[JsonDerivedType(typeof(IndexExpr), "index")]
[JsonDerivedType(typeof(CompareExpr), "compare")]
[JsonDerivedType(typeof(AndExpr), "and")]
[JsonDerivedType(typeof(OrExpr), "or")]
[JsonDerivedType(typeof(NotExpr), "not")]
[JsonDerivedType(typeof(InExpr), "in")]
[JsonDerivedType(typeof(ConditionalExpr), "cond")]
[JsonDerivedType(typeof(CallExpr), "call")]
public abstract record RuleExpr;

/// <summary>A constant value.</summary>
/// <param name="Value">The literal value.</param>
public sealed record LiteralExpr(RuleValue Value) : RuleExpr;

/// <summary>A root variable: <c>request</c>, <c>resource</c>, or a path-pattern capture (e.g. <c>userId</c>).</summary>
/// <param name="Name">The variable name.</param>
public sealed record VarExpr(string Name) : RuleExpr;

/// <summary>Member access <c>target.Name</c> (a field of a map).</summary>
/// <param name="Target">The target expression.</param>
/// <param name="Name">The member/field name.</param>
public sealed record MemberExpr(RuleExpr Target, string Name) : RuleExpr;

/// <summary>Index access <c>target[Index]</c> (a list element or map entry).</summary>
/// <param name="Target">The target expression.</param>
/// <param name="Index">The index/key expression.</param>
public sealed record IndexExpr(RuleExpr Target, RuleExpr Index) : RuleExpr;

/// <summary>A comparison <c>Left Op Right</c>.</summary>
/// <param name="Left">The left operand.</param>
/// <param name="Op">The operator.</param>
/// <param name="Right">The right operand.</param>
public sealed record CompareExpr(RuleExpr Left, CompareOp Op, RuleExpr Right) : RuleExpr;

/// <summary>Logical AND of all operands (vacuously true when empty).</summary>
/// <param name="Operands">The operands.</param>
public sealed record AndExpr(IReadOnlyList<RuleExpr> Operands) : RuleExpr
{
    /// <inheritdoc/>
    public bool Equals(AndExpr? other) => other is not null && Operands.SequenceEqual(other.Operands);
    /// <inheritdoc/>
    public override int GetHashCode() => ExprList.Hash(Operands);
}

/// <summary>Logical OR of all operands (vacuously false when empty).</summary>
/// <param name="Operands">The operands.</param>
public sealed record OrExpr(IReadOnlyList<RuleExpr> Operands) : RuleExpr
{
    /// <inheritdoc/>
    public bool Equals(OrExpr? other) => other is not null && Operands.SequenceEqual(other.Operands);
    /// <inheritdoc/>
    public override int GetHashCode() => ExprList.Hash(Operands);
}

/// <summary>Logical negation.</summary>
/// <param name="Operand">The operand.</param>
public sealed record NotExpr(RuleExpr Operand) : RuleExpr;

/// <summary>Membership test <c>Item in Collection</c> (list element or map key).</summary>
/// <param name="Item">The item to look for.</param>
/// <param name="Collection">The list or map.</param>
public sealed record InExpr(RuleExpr Item, RuleExpr Collection) : RuleExpr;

/// <summary>A ternary <c>Condition ? Then : Else</c>.</summary>
/// <param name="Condition">The condition.</param>
/// <param name="Then">Value when true.</param>
/// <param name="Else">Value when false.</param>
public sealed record ConditionalExpr(RuleExpr Condition, RuleExpr Then, RuleExpr Else) : RuleExpr;

/// <summary>A built-in function call, e.g. <c>exists(path)</c>, <c>get(path)</c>, <c>size(x)</c>.</summary>
/// <param name="Name">The function name.</param>
/// <param name="Args">The arguments.</param>
public sealed record CallExpr(string Name, IReadOnlyList<RuleExpr> Args) : RuleExpr
{
    /// <inheritdoc/>
    public bool Equals(CallExpr? other) =>
        other is not null && Name == other.Name && Args.SequenceEqual(other.Args);
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Name, ExprList.Hash(Args));
}

internal static class ExprList
{
    public static int Hash(IReadOnlyList<RuleExpr> items)
    {
        var hash = new HashCode();
        foreach (var i in items) hash.Add(i);
        return hash.ToHashCode();
    }
}
