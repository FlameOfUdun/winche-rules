using Winche.Rules.Expressions;

namespace Winche.Rules;

/// <summary>
/// Fluent builder for a <see cref="RuleSet"/>:
/// <code>
/// var rules = RulesetBuilder.Build(r =>
///     r.Match("users/{userId}", u =>
///     {
///         u.Allow(RuleOperations.Read, Expr.Auth("uid").Eq(Expr.Param("userId")));
///         u.Allow(RuleOperations.Write, Expr.Auth("uid").Eq(Expr.Param("userId")));
///         u.Match("posts/{postId}", p =>
///             p.Allow(RuleOperations.Read, Expr.Const(true)));
///     }));
/// </code>
/// </summary>
public sealed class RulesetBuilder
{
    private readonly List<MatchBlock> _matches = [];

    private RulesetBuilder() { }

    /// <summary>Builds a ruleset.</summary>
    public static RuleSet Build(Action<RulesetBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new RulesetBuilder();
        configure(builder);
        return new RuleSet(builder._matches);
    }

    /// <summary>Adds a top-level <c>match</c> block.</summary>
    public RulesetBuilder Match(string path, Action<MatchBuilder> configure)
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

    internal MatchBlock Build() => new(_path, _allow, _matches);
}
