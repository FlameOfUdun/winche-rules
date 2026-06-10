namespace Winche.Rules.Expressions;

/// <summary>
/// Fluent factory for rule condition expressions. Maps Firestore's vocabulary to C#:
/// <code>
/// // resource.data.ownerId == request.auth.uid
/// Expr.Resource("data","ownerId").Eq(Expr.Auth("uid"))
///
/// // resource.data.public == true || resource.data.ownerId == request.auth.uid
/// Expr.Any(
///     Expr.Resource("data","public").Eq(true),
///     Expr.Resource("data","ownerId").Eq(Expr.Auth("uid")))
/// </code>
/// </summary>
public static class Expr
{
    // ── Literals ──────────────────────────────────────────────────────────────

    /// <summary>A literal from a <see cref="RuleValue"/>.</summary>
    public static RuleExpr Value(RuleValue value) => new LiteralExpr(value);
    /// <summary>A string literal.</summary>
    public static RuleExpr Const(string value) => Value(RuleValue.String(value));
    /// <summary>An integer literal.</summary>
    public static RuleExpr Const(long value) => Value(RuleValue.Int(value));
    /// <summary>A double literal.</summary>
    public static RuleExpr Const(double value) => Value(RuleValue.Double(value));
    /// <summary>A boolean literal.</summary>
    public static RuleExpr Const(bool value) => Value(RuleValue.Bool(value));
    /// <summary>The null literal.</summary>
    public static RuleExpr Null { get; } = Value(RuleValue.Null);

    // ── Roots ─────────────────────────────────────────────────────────────────

    /// <summary>A root variable (<c>request</c>, <c>resource</c>, or a path capture).</summary>
    public static RuleExpr Var(string name) => new VarExpr(name);
    /// <summary>A path-pattern capture variable (e.g. <c>userId</c> from <c>users/{userId}</c>).</summary>
    public static RuleExpr Param(string name) => new VarExpr(name);

    /// <summary>The existing document — <c>resource</c> followed by <paramref name="path"/>.</summary>
    public static RuleExpr Resource(params string[] path) => Navigate(Var("resource"), path);
    /// <summary>The request — <c>request</c> followed by <paramref name="path"/>.</summary>
    public static RuleExpr Request(params string[] path) => Navigate(Var("request"), path);
    /// <summary>The incoming document — <c>request.resource</c> followed by <paramref name="path"/>.</summary>
    public static RuleExpr RequestResource(params string[] path) => Navigate(Request("resource"), path);
    /// <summary>The caller — <c>request.auth</c> followed by <paramref name="path"/> (e.g. <c>uid</c>, <c>token.role</c>).</summary>
    public static RuleExpr Auth(params string[] path) => Navigate(Request("auth"), path);
    /// <summary>The request time — <c>request.time</c>.</summary>
    public static RuleExpr Time() => Request("time");
    /// <summary>The operation — <c>request.method</c>.</summary>
    public static RuleExpr Method() => Request("method");

    // ── Built-in functions ──────────────────────────────────────────────────

    /// <summary>A built-in function call.</summary>
    public static RuleExpr Call(string name, params RuleExpr[] args) => new CallExpr(name, args);
    /// <summary>Whether the document at <paramref name="path"/> exists.</summary>
    public static RuleExpr Exists(RuleExpr path) => Call("exists", path);
    /// <summary>The document at <paramref name="path"/> (a map; use <c>.Field("data")</c> to read fields).</summary>
    public static RuleExpr Get(RuleExpr path) => Call("get", path);
    /// <summary>The size of a string/list/map.</summary>
    public static RuleExpr Size(RuleExpr value) => Call("size", value);

    // ── Logic (n-ary) ─────────────────────────────────────────────────────────

    /// <summary>Logical AND of all operands.</summary>
    public static RuleExpr All(params RuleExpr[] operands) => new AndExpr(operands);
    /// <summary>Logical OR of all operands.</summary>
    public static RuleExpr Any(params RuleExpr[] operands) => new OrExpr(operands);

    private static RuleExpr Navigate(RuleExpr root, IReadOnlyList<string> path)
    {
        var e = root;
        foreach (var seg in path) e = new MemberExpr(e, seg);
        return e;
    }
}

/// <summary>Fluent combinators over <see cref="RuleExpr"/>.</summary>
public static class RuleExprExtensions
{
    /// <summary>Member access <c>this.name</c>.</summary>
    public static RuleExpr Field(this RuleExpr target, string name) => new MemberExpr(target, name);
    /// <summary>Index access <c>this[index]</c>.</summary>
    public static RuleExpr Index(this RuleExpr target, RuleExpr index) => new IndexExpr(target, index);

    /// <summary>Equality.</summary>
    public static RuleExpr Eq(this RuleExpr left, RuleExpr right) => new CompareExpr(left, CompareOp.Eq, right);
    /// <summary>Inequality.</summary>
    public static RuleExpr Ne(this RuleExpr left, RuleExpr right) => new CompareExpr(left, CompareOp.Ne, right);
    /// <summary>Less-than.</summary>
    public static RuleExpr Lt(this RuleExpr left, RuleExpr right) => new CompareExpr(left, CompareOp.Lt, right);
    /// <summary>Less-than-or-equal.</summary>
    public static RuleExpr Le(this RuleExpr left, RuleExpr right) => new CompareExpr(left, CompareOp.Le, right);
    /// <summary>Greater-than.</summary>
    public static RuleExpr Gt(this RuleExpr left, RuleExpr right) => new CompareExpr(left, CompareOp.Gt, right);
    /// <summary>Greater-than-or-equal.</summary>
    public static RuleExpr Ge(this RuleExpr left, RuleExpr right) => new CompareExpr(left, CompareOp.Ge, right);

    /// <summary>Equality against a string constant.</summary>
    public static RuleExpr Eq(this RuleExpr left, string value) => left.Eq(Expr.Const(value));
    /// <summary>Equality against an integer constant.</summary>
    public static RuleExpr Eq(this RuleExpr left, long value) => left.Eq(Expr.Const(value));
    /// <summary>Equality against a boolean constant.</summary>
    public static RuleExpr Eq(this RuleExpr left, bool value) => left.Eq(Expr.Const(value));

    /// <summary>Membership <c>this in collection</c>.</summary>
    public static RuleExpr In(this RuleExpr item, RuleExpr collection) => new InExpr(item, collection);
    /// <summary>Logical AND with another expression.</summary>
    public static RuleExpr And(this RuleExpr left, RuleExpr right) => new AndExpr([left, right]);
    /// <summary>Logical OR with another expression.</summary>
    public static RuleExpr Or(this RuleExpr left, RuleExpr right) => new OrExpr([left, right]);
    /// <summary>Logical negation.</summary>
    public static RuleExpr Not(this RuleExpr operand) => new NotExpr(operand);
}
