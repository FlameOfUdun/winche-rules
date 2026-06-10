using Winche.Rules.Expressions;

namespace Winche.Rules;

/// <summary>
/// Grants <see cref="Operations"/> when <see cref="Condition"/> evaluates to true. Allow-only: a rule never
/// denies — it simply doesn't grant — and access is the default-deny OR of all matching rules (Firestore).
/// </summary>
/// <param name="Operations">The operations this rule grants.</param>
/// <param name="Condition">The condition that must hold to grant.</param>
public sealed record AllowRule(IReadOnlyList<RuleOperation> Operations, RuleExpr Condition)
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
    /// <inheritdoc/>
    public bool Equals(MatchBlock? other) =>
        other is not null && Path == other.Path && Allow.SequenceEqual(other.Allow) && Matches.SequenceEqual(other.Matches);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Path);
        foreach (var a in Allow) hash.Add(a);
        foreach (var m in Matches) hash.Add(m);
        return hash.ToHashCode();
    }
}

/// <summary>A complete set of rules: the top-level <c>match</c> blocks (rooted at the document path).</summary>
/// <param name="Matches">The top-level match blocks.</param>
public sealed record Ruleset(IReadOnlyList<MatchBlock> Matches)
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
    /// <returns>A new <see cref="Ruleset"/> whose <see cref="Matches"/> is the concatenation
    /// of all input rulesets' matches.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="rulesets"/> is <see langword="null"/>.
    /// </exception>
    public static Ruleset Merge(IEnumerable<Ruleset> rulesets)
    {
        ArgumentNullException.ThrowIfNull(rulesets);
        var merged = new List<MatchBlock>();
        foreach (var rs in rulesets)
            merged.AddRange(rs.Matches);
        return new Ruleset(merged);
    }

    /// <inheritdoc/>
    public bool Equals(Ruleset? other) => other is not null && Matches.SequenceEqual(other.Matches);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var m in Matches) hash.Add(m);
        return hash.ToHashCode();
    }
}
