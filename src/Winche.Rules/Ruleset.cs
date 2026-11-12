using System.Text.Json.Serialization;
using Winche.Rules.Expressions;

namespace Winche.Rules;

/// <summary>
/// Grants <see cref="Operations"/> when <see cref="Condition"/> evaluates to true. Allow-only: a rule never
/// denies — it simply doesn't grant — and access is the default-deny OR of all matching rules.
/// </summary>
/// <param name="Operations">The operations this rule grants.</param>
/// <param name="Condition">The condition that must hold to grant.</param>
public sealed record AllowRule(IReadOnlyList<RuleOperation> Operations, RuleExpression Condition)
{
    /// <inheritdoc/>
    public bool Equals(AllowRule? other) =>
        other is not null && Operations.SequenceEqual(other.Operations) && Condition.Equals(other.Condition);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var o in Operations) hash.Add(o);
        hash.Add(Condition);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A <c>match</c> block: a path pattern (relative to its parent), the <see cref="Allow"/> rules that apply to
/// documents at that path, and nested <see cref="Matches"/> whose paths extend it. Pattern segments are
/// literals, <c>{id}</c> captures, or a trailing <c>{name=**}</c> recursive capture.
/// </summary>
/// <param name="Path">The (relative) path pattern.</param>
/// <param name="Allow">Allow rules for documents at this path.</param>
/// <param name="Matches">Nested match blocks.</param>
public sealed record MatchBlock(string Path, IReadOnlyList<AllowRule> Allow, IReadOnlyList<MatchBlock> Matches)
{
    /// <summary>
    /// Functions defined in this block (4.1.0): callable from its conditions and nested blocks'. <see langword="null"/>
    /// (not written to JSON) when there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuleFunction>? Functions { get; init; }

    /// <inheritdoc/>
    public bool Equals(MatchBlock? other) =>
        other is not null && Path == other.Path && Allow.SequenceEqual(other.Allow) && Matches.SequenceEqual(other.Matches)
        && RuleFunction.SameList(Functions, other.Functions);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Path);
        foreach (var a in Allow) hash.Add(a);
        foreach (var m in Matches) hash.Add(m);
        foreach (var f in Functions ?? []) hash.Add(f);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A function defined in a rules file (4.1.0):
/// <code>
/// function isOwner(uid) {
///   let me = request.auth.uid;
///   return me == uid;
/// }
/// </code>
/// A call is the same as writing its body there, with the arguments in place of the parameters (and each
/// <c>let</c> in place of its name). Functions see <c>request</c>, <c>resource</c>, the path captures and the
/// functions where they are defined; they can't call themselves.
/// </summary>
/// <param name="Name">The function's name.</param>
/// <param name="Parameters">Its parameter names.</param>
/// <param name="Lets">Its <c>let</c> bindings, in order (each may use the parameters and earlier lets).</param>
/// <param name="Body">The expression after <c>return</c>.</param>
public sealed record RuleFunction(string Name, IReadOnlyList<string> Parameters, IReadOnlyList<LetBinding> Lets, RuleExpression Body)
{
    /// <inheritdoc/>
    public bool Equals(RuleFunction? other) =>
        other is not null && Name == other.Name && Parameters.SequenceEqual(other.Parameters)
        && (Lets ?? []).SequenceEqual(other.Lets ?? []) && Body.Equals(other.Body);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Name);
        foreach (var p in Parameters) hash.Add(p);
        foreach (var l in Lets ?? []) hash.Add(l);
        hash.Add(Body);
        return hash.ToHashCode();
    }

    internal static bool SameList(IReadOnlyList<RuleFunction>? a, IReadOnlyList<RuleFunction>? b) =>
        (a ?? []).SequenceEqual(b ?? []);
}

/// <summary>A <c>let name = value;</c> line in a function.</summary>
public sealed record LetBinding(string Name, RuleExpression Value);

/// <summary>
/// A complete set of rules: the top-level <c>match</c> blocks (rooted at the document path).
/// </summary>
/// <param name="Matches">The top-level match blocks.</param>
public sealed record RuleSet(IReadOnlyList<MatchBlock> Matches)
{
    /// <summary>
    /// Merges multiple rulesets into one by concatenating all of their top-level
    /// <see cref="Matches"/> in iteration order.
    /// </summary>
    /// <remarks>
    /// Block evaluation is OR/default-deny, so the order of blocks does not affect the
    /// authorization decision — a request is allowed if <em>any</em> block grants it.
    /// An empty <paramref name="rulesets"/> sequence returns an empty (deny-all) ruleset.
    /// </remarks>
    /// <param name="rulesets">The rulesets to merge. Must not be <see langword="null"/>.</param>
    /// <returns>A new <see cref="RuleSet"/> whose <see cref="Matches"/> is the concatenation
    /// of all input rulesets' matches.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="rulesets"/> is <see langword="null"/>.
    /// </exception>
    public static RuleSet Merge(IEnumerable<RuleSet> rulesets)
    {
        ArgumentNullException.ThrowIfNull(rulesets);

        var merged = new List<MatchBlock>();

        foreach (var rs in rulesets)
        {
            // A ruleset's top-level functions are for its own blocks: carry them into those blocks, so merged
            // rulesets can't see (or clash with) each other's.
            if (rs.Functions is not { Count: > 0 } functions)
            {
                merged.AddRange(rs.Matches);
                continue;
            }
            foreach (var block in rs.Matches)
                merged.Add(block with { Functions = [.. functions, .. block.Functions ?? []] });
        }

        return new RuleSet(merged);
    }

    /// <inheritdoc/>
    public bool Equals(RuleSet? other) =>
        other is not null && Matches.SequenceEqual(other.Matches) && RuleFunction.SameList(Functions, other.Functions);

    /// <summary>
    /// Top-level functions (4.1.0), callable from every condition. <see langword="null"/> (not written to JSON) when
    /// there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<RuleFunction>? Functions { get; init; }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var m in Matches) 
            hash.Add(m);
        foreach (var f in Functions ?? [])
            hash.Add(f);
            
        return hash.ToHashCode();
    }
}
