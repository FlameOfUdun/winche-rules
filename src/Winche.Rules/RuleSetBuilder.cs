using Winche.Rules.Expressions;
using Winche.Rules.Matching;

namespace Winche.Rules;

/// <summary>
/// Fluent builder for a <see cref="RuleSet"/>:
/// <code>
/// var rules = RuleSetBuilder.Build(r =>
///     r.Match("users/{userId}", u =>
///     {
///         u.Allow(RuleOperations.Read, Expr.Auth("uid").Eq(Expr.Param("userId")));
///         u.Allow(RuleOperations.Write, Expr.Auth("uid").Eq(Expr.Param("userId")));
///         u.Match("posts/{postId}", p =>
///             p.Allow(RuleOperations.Read, Expr.Const(true)));
///     }));
/// </code>
/// </summary>
public sealed class RuleSetBuilder
{
    private readonly List<MatchBlock> _matches = [];

    private RuleSetBuilder() { }

    /// <summary>Builds a ruleset.</summary>
    public static RuleSet Build(Action<RuleSetBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new RuleSetBuilder();
        configure(builder);
        Matching.RuleFlatten.Validate(builder._matches);
        return new RuleSet(builder._matches);
    }

    /// <summary>Adds a top-level <c>match</c> block.</summary>
    public RuleSetBuilder Match(string path, Action<MatchBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var match = new MatchBuilder(path);
        configure(match);
        _matches.Add(match.Build());
        return this;
    }
}

/// <summary>
/// Fluent builder for a <c>match</c> block (allow rules + nested matches).
/// </summary>
public sealed class MatchBuilder
{
    private readonly string _path;
    private readonly List<AllowRule> _allow = [];
    private readonly List<MatchBlock> _matches = [];

    internal MatchBuilder(string path) => _path = path;

    /// <summary>
    /// Adds an <c>allow</c> rule granting <paramref name="operations"/> when <paramref name="condition"/> holds.
    /// </summary>
    public MatchBuilder Allow(IEnumerable<RuleOperation> operations, RuleExpression condition)
    {
        _allow.Add(new AllowRule([.. operations], condition));
        return this;
    }

    /// <summary>Adds a nested <c>match</c> block whose path extends this one.</summary>
    public MatchBuilder Match(string path, Action<MatchBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var match = new MatchBuilder(path);
        configure(match);
        _matches.Add(match.Build());
        return this;
    }

    internal MatchBlock Build()
    {
        // Fail fast: validate the (relative) path pattern at build time rather than at evaluation.
        var parsed = PathPattern.Parse(_path);
        // A recursive wildcard consumes the rest of the path, so nested matches under it can never
        // be reached. Reject rather than silently producing dead rules (Firestore disallows this).
        if (_matches.Count > 0 && parsed[^1] is RecursiveSegment)
            throw new ArgumentException(
                $"match '{_path}' ends in a recursive wildcard and cannot contain nested matches; " +
                "nested rules would be unreachable.");
        return new(_path, _allow, _matches);
    }
}
