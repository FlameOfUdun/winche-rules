namespace Winche.Rules.Expressions;

/// <summary>
/// Fluent factory for rule condition expressions:
/// <code>
/// // resource.ownerId == request.auth.uid
/// Expr.Resource("ownerId").Eq(Expr.Auth("uid"))
///
/// // resource.public == true || resource.ownerId == request.auth.uid
/// Expr.Any(
///     Expr.Resource("public").Eq(true),
///     Expr.Resource("ownerId").Eq(Expr.Auth("uid")))
/// </code>
/// </summary>
public static class Expr
{
    /// <summary>
    /// A literal from a <see cref="RuleValue"/>.
    /// </summary>
    public static RuleExpression Value(RuleValue value) => new LiteralExpression(value);

    /// <summary>
    /// A string literal.
    /// </summary>
    public static RuleExpression Const(string value) => Value(RuleValue.String(value));

    /// <summary>
    /// An integer literal.
    /// </summary>
    public static RuleExpression Const(long value) => Value(RuleValue.Int(value));

    /// <summary>
    /// A double literal.
    /// </summary>
    public static RuleExpression Const(double value) => Value(RuleValue.Double(value));

    /// <summary>
    /// A boolean literal.
    /// </summary>
    public static RuleExpression Const(bool value) => Value(RuleValue.Bool(value));

    /// <summary>
    /// The null literal.
    /// </summary>
    public static RuleExpression Null { get; } = Value(RuleValue.Null);

    /// <summary>
    /// A root variable (<c>request</c>, <c>resource</c>, or a path capture).
    /// </summary>
    public static RuleExpression Var(string name) => new VarExpression(name);

    /// <summary>
    /// A path-pattern capture variable (e.g. <c>userId</c> from <c>users/{userId}</c>).
    /// </summary>
    public static RuleExpression Param(string name) => new VarExpression(name);

    /// <summary>
    /// The existing document — <c>resource</c> followed by <paramref name="path"/>.
    /// </summary>
    public static RuleExpression Resource(params string[] path) => Navigate(Var("resource"), path);

    /// <summary>
    /// The request — <c>request</c> followed by <paramref name="path"/>.
    /// </summary>
    public static RuleExpression Request(params string[] path) => Navigate(Var("request"), path);

    /// <summary>
    /// The incoming document — <c>request.resource</c> followed by <paramref name="path"/>.
    /// </summary>
    public static RuleExpression RequestResource(params string[] path) => Navigate(Request("resource"), path);

    /// <summary>
    /// The caller — <c>request.auth</c> followed by <paramref name="path"/> (e.g. <c>uid</c>, <c>token.role</c>).
    /// </summary>
    public static RuleExpression Auth(params string[] path) => Navigate(Request("auth"), path);

    /// <summary>
    /// The request time — <c>request.time</c>.
    /// </summary>
    public static RuleExpression Time() => Request("time");

    /// <summary>
    /// The operation — <c>request.method</c>.
    /// </summary>
    public static RuleExpression Method() => Request("method");

    /// <summary>
    /// A built-in function call.
    /// </summary>
    public static RuleExpression Call(string name, params RuleExpression[] args) => new CallExpression(name, args);

    /// <summary>
    /// Whether the document at <paramref name="path"/> exists.
    /// </summary>
    public static RuleExpression Exists(RuleExpression path) => Call("exists", path);

    /// <summary>
    /// The resource at <paramref name="path"/> (a map; navigate its fields with <c>.Field(...)</c>).
    /// </summary>
    public static RuleExpression Get(RuleExpression path) => Call("get", path);

    /// <summary>
    /// The size of a string/list/map.
    /// </summary>
    public static RuleExpression Size(RuleExpression value) => Call("size", value);

    /// <summary>
    /// A constant list of strings, e.g. for <c>hasOnly</c> or <c>in</c>.
    /// </summary>
    public static RuleExpression Strings(params string[] values) => Value(RuleValue.List([.. values.Select(RuleValue.String)]));

    /// <summary>
    /// Logical AND of all operands.
    /// </summary>
    public static RuleExpression All(params RuleExpression[] operands) => new AndExpression(operands);

    /// <summary>
    /// Logical OR of all operands.
    /// </summary>
    public static RuleExpression Any(params RuleExpression[] operands) => new OrExpression(operands);

    private static RuleExpression Navigate(RuleExpression root, IReadOnlyList<string> path)
    {
        var e = root;
        foreach (var seg in path) e = new MemberExpression(e, seg);
        return e;
    }
}

/// <summary>
/// Fluent combinators over <see cref="RuleExpression"/>.
/// </summary>
public static class RuleExprExtensions
{
    /// <summary>
    /// Member access <c>this.name</c>.
    /// </summary>
    public static RuleExpression Field(this RuleExpression target, string name) => new MemberExpression(target, name);

    /// <summary>
    /// Index access <c>this[index]</c>.
    /// </summary>
    public static RuleExpression Index(this RuleExpression target, RuleExpression index) => new IndexExpression(target, index);

    /// <summary>
    /// Equality.
    /// </summary>
    public static RuleExpression Eq(this RuleExpression left, RuleExpression right) => new CompareExpression(left, ComparisonOperator.Eq, right);

    /// <summary>
    /// Inequality.
    /// </summary>
    public static RuleExpression Ne(this RuleExpression left, RuleExpression right) => new CompareExpression(left, ComparisonOperator.Ne, right);

    /// <summary>
    /// Less-than.
    /// </summary>
    public static RuleExpression Lt(this RuleExpression left, RuleExpression right) => new CompareExpression(left, ComparisonOperator.Lt, right);

    /// <summary>
    /// Less-than-or-equal.
    /// </summary>
    public static RuleExpression Le(this RuleExpression left, RuleExpression right) => new CompareExpression(left, ComparisonOperator.Le, right);

    /// <summary>
    /// Greater-than.
    /// </summary>
    public static RuleExpression Gt(this RuleExpression left, RuleExpression right) => new CompareExpression(left, ComparisonOperator.Gt, right);

    /// <summary>
    /// Greater-than-or-equal.
    /// </summary>
    public static RuleExpression Ge(this RuleExpression left, RuleExpression right) => new CompareExpression(left, ComparisonOperator.Ge, right);

    /// <summary>
    /// Equality against a string constant.
    /// </summary>
    public static RuleExpression Eq(this RuleExpression left, string value) => left.Eq(Expr.Const(value));

    /// <summary>
    /// Equality against an integer constant.
    /// </summary>
    public static RuleExpression Eq(this RuleExpression left, long value) => left.Eq(Expr.Const(value));

    /// <summary>
    /// Equality against a boolean constant.
    /// </summary>
    public static RuleExpression Eq(this RuleExpression left, bool value) => left.Eq(Expr.Const(value));

    /// <summary>
    /// Membership <c>this in collection</c>.
    /// </summary>
    public static RuleExpression In(this RuleExpression item, RuleExpression collection) => new InExpression(item, collection);

    /// <summary>
    /// Logical AND with another expression.
    /// </summary>
    public static RuleExpression And(this RuleExpression left, RuleExpression right) => new AndExpression([left, right]);

    /// <summary>
    /// Logical OR with another expression.
    /// </summary>
    public static RuleExpression Or(this RuleExpression left, RuleExpression right) => new OrExpression([left, right]);

    /// <summary>
    /// Logical negation.
    /// </summary>
    public static RuleExpression Not(this RuleExpression operand) => new NotExpression(operand);

    // ── functions (see Evaluation.RuleFunctions) ──────────────────────────────

    /// <summary><c>this.size()</c>: length of a string, list or map.</summary>
    public static RuleExpression Size(this RuleExpression target) => Expr.Call("size", target);

    /// <summary><c>this.keys()</c>: a map's keys.</summary>
    public static RuleExpression Keys(this RuleExpression map) => Expr.Call("keys", map);

    /// <summary><c>this.hasOnly([...])</c>: every element of this list is one of <paramref name="allowed"/>.</summary>
    public static RuleExpression HasOnly(this RuleExpression list, params string[] allowed) => Expr.Call("hasOnly", list, Expr.Strings(allowed));

    /// <summary><c>this.hasAll([...])</c>: this list contains every one of <paramref name="required"/>.</summary>
    public static RuleExpression HasAll(this RuleExpression list, params string[] required) => Expr.Call("hasAll", list, Expr.Strings(required));

    /// <summary><c>this.hasAny([...])</c>: this list contains at least one of <paramref name="candidates"/>.</summary>
    public static RuleExpression HasAny(this RuleExpression list, params string[] candidates) => Expr.Call("hasAny", list, Expr.Strings(candidates));

    /// <summary><c>this.diff(before)</c>: how this map differs from <paramref name="before"/>.</summary>
    public static RuleExpression Diff(this RuleExpression after, RuleExpression before) => Expr.Call("diff", after, before);

    /// <summary><c>diff.affectedKeys()</c>: keys added, removed or changed.</summary>
    public static RuleExpression AffectedKeys(this RuleExpression diff) => Expr.Call("affectedKeys", diff);

    /// <summary><c>diff.addedKeys()</c>.</summary>
    public static RuleExpression AddedKeys(this RuleExpression diff) => Expr.Call("addedKeys", diff);

    /// <summary><c>diff.removedKeys()</c>.</summary>
    public static RuleExpression RemovedKeys(this RuleExpression diff) => Expr.Call("removedKeys", diff);

    /// <summary><c>diff.changedKeys()</c>.</summary>
    public static RuleExpression ChangedKeys(this RuleExpression diff) => Expr.Call("changedKeys", diff);

    /// <summary><c>diff.unchangedKeys()</c>.</summary>
    public static RuleExpression UnchangedKeys(this RuleExpression diff) => Expr.Call("unchangedKeys", diff);

    /// <summary><c>this is type</c>: string, int, float, number, bool, null, map, list, timestamp, bytes or path.</summary>
    public static RuleExpression Is(this RuleExpression value, string type) => Expr.Call("is", value, Expr.Const(type));

    /// <summary><c>this.matches(regex)</c>: the whole string matches <paramref name="regex"/>.</summary>
    public static RuleExpression Matches(this RuleExpression value, string regex) => Expr.Call("matches", value, Expr.Const(regex));

    /// <summary><c>this.lower()</c>.</summary>
    public static RuleExpression Lower(this RuleExpression value) => Expr.Call("lower", value);

    /// <summary><c>this.upper()</c>.</summary>
    public static RuleExpression Upper(this RuleExpression value) => Expr.Call("upper", value);

    /// <summary><c>this.get(key, default)</c>: a map's value at <paramref name="key"/>, or <paramref name="fallback"/> when absent.</summary>
    public static RuleExpression GetOr(this RuleExpression map, string key, RuleExpression fallback) => Expr.Call("get", map, Expr.Const(key), fallback);
}
