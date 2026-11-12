using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Matching;

namespace Winche.Rules.Querying;

/// <summary>
/// Decides whether a query is allowed — "rules are not filters": the query is
/// allowed only when its constraints <b>provably</b> guarantee every result satisfies a read rule;
/// otherwise it is rejected (the client must constrain the query). The analysis is <b>sound</b> — it never
/// returns <c>true</c> for a query that isn't provably safe — and intentionally incomplete (it rejects
/// queries it cannot prove, including any rule using <c>get()</c>/<c>exists()</c>, ternaries, <c>in</c>,
/// or per-result path wildcards).
/// </summary>
public static class QueryAnalyzer
{
    /// <summary>
    /// Whether the <paramref name="query"/> is allowed under <paramref name="ruleset"/>'s read rules.
    /// </summary>
    public static bool Allows(RuleSet ruleset, QueryConstraints query, RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in RuleFlatten.Flatten(ruleset))
        {
            if (!rule.Allow.Operations.Contains(RuleOperation.List)) continue;
            var governs = query.CollectionGroup
                ? GovernsGroup(rule.Pattern, query.Collection, out var parentParams)
                : Governs(rule.Pattern, query.Collection, out parentParams);
            if (!governs) continue;

            var ctx = new RuleContext { Request = context.Request, Params = parentParams, Provider = context.Provider, Comparer = context.Comparer };
            if (Prove(rule.Allow.Condition, query, ctx) == Proof.True)
                return true;
        }
        return false;   // not provably safe → reject
    }

    private static bool Governs(IReadOnlyList<PathSegment> pattern, string collection, out Dictionary<string, RuleValue> parentParams)
    {
        parentParams = [];

        if (pattern.Count == 0) 
            return false;

        var leaf = pattern[^1];
        var prefix = pattern.Take(pattern.Count - 1).ToList();

        return leaf switch
        {
            ParamSegment => PathPattern.TryMatch(prefix, collection, out parentParams),
            RecursiveSegment => PathPattern.TryMatchPrefix(prefix, collection, out parentParams),
            _ => false,
        };
    }

    // A collection group query reads `id` under any parent, so a rule governs it only if it covers every
    // parent: {p=**}/id/{doc} (the parents' capture stays unbound — per result), {p=**}/{coll}/{doc} (coll
    // binds to id), or a bare {p=**} (everything).
    private static bool GovernsGroup(IReadOnlyList<PathSegment> pattern, string collectionId, out Dictionary<string, RuleValue> parentParams)
    {
        parentParams = [];
        switch (pattern)
        {
            case [RecursiveSegment]:
                return true;
            case [RecursiveSegment, LiteralSegment lit, ParamSegment] when lit.Value == collectionId:
                return true;
            case [RecursiveSegment, ParamSegment coll, ParamSegment]:
                parentParams[coll.Name] = RuleValue.String(collectionId);
                return true;
            default:
                return false;
        }
    }

    private static Proof Prove(RuleExpression expr, QueryConstraints query, RuleContext ctx)
    {
        switch (expr)
        {
            case LiteralExpression { Value.Kind: RuleValueKind.Bool } l:
                return l.Value.AsBool ? Proof.True : Proof.False;

            case NotExpression n:
                return Negate(Prove(n.Operand, query, ctx));

            case AndExpression a:
                {
                    var allTrue = true;
                    foreach (var op in a.Operands)
                    {
                        var p = Prove(op, query, ctx);
                        if (p == Proof.False) return Proof.False;
                        if (p != Proof.True) allTrue = false;
                    }
                    return allTrue ? Proof.True : Proof.Unknown;
                }

            case OrExpression o:
                {
                    var allFalse = true;
                    foreach (var op in o.Operands)
                    {
                        var p = Prove(op, query, ctx);
                        if (p == Proof.True) return Proof.True;
                        if (p != Proof.False) allFalse = false;
                    }
                    return allFalse ? Proof.False : Proof.Unknown;
                }

            case CompareExpression c:
                return ProveCompare(c, query, ctx);

            default:
                // Doesn't depend on the documents (only request, bound path captures, literals and pure
                // functions): it has the same value for every result, so just evaluate it.
                if (!IsConstant(expr, ctx)) return Proof.Unknown;
                return RuleEvaluator.EvaluateAsync(expr, ctx).GetAwaiter().GetResult() ? Proof.True : Proof.False;
        }
    }

    // No resource, no unbound path capture, no exists()/get(path): evaluation can't block or vary per document.
    private static bool IsConstant(RuleExpression expr, RuleContext ctx) => expr switch
    {
        LiteralExpression => true,
        VarExpression v => v.Name == "request" || (v.Name != "resource" && ctx.Params.ContainsKey(v.Name)),
        MemberExpression m => IsConstant(m.Target, ctx),
        IndexExpression ix => IsConstant(ix.Target, ctx) && IsConstant(ix.Index, ctx),
        CompareExpression c => IsConstant(c.Left, ctx) && IsConstant(c.Right, ctx),
        InExpression i => IsConstant(i.Item, ctx) && IsConstant(i.Collection, ctx),
        NotExpression n => IsConstant(n.Operand, ctx),
        AndExpression a => a.Operands.All(o => IsConstant(o, ctx)),
        OrExpression o => o.Operands.All(x => IsConstant(x, ctx)),
        ConditionalExpression c => IsConstant(c.Condition, ctx) && IsConstant(c.Then, ctx) && IsConstant(c.Else, ctx),
        CallExpression call => call.Name != "exists" && !(call.Name == "get" && call.Args.Count == 1)
                               && call.Args.All(a => IsConstant(a, ctx)),
        _ => false,
    };

    private static Proof ProveCompare(CompareExpression c, QueryConstraints query, RuleContext ctx)
    {
        var left = Classify(c.Left, ctx);
        var right = Classify(c.Right, ctx);

        if (left.Kind == OperandKind.Const && right.Kind == OperandKind.Const)
        {
            var b = TryCompute(ctx.Comparer, left.Value, c.Op, right.Value);
            return b is null ? Proof.Unknown : b.Value ? Proof.True : Proof.False;
        }
        if (left.Kind == OperandKind.Field && right.Kind == OperandKind.Const)
            return ProveAtom(ctx.Comparer, left.Field!, c.Op, right.Value, query);
        if (left.Kind == OperandKind.Const && right.Kind == OperandKind.Field)
            return ProveAtom(ctx.Comparer, right.Field!, Flip(c.Op), left.Value, query);

        return Proof.Unknown;
    }

    private static Proof ProveAtom(IRuleValueComparer cmp, IReadOnlyList<string> field, ComparisonOperator op, RuleValue value, QueryConstraints query)
    {
        foreach (var c in query.Constraints)
        {
            if (!c.Field.SequenceEqual(field)) continue;

            if (c.Operator == ComparisonOperator.Eq)
            {
                var b = TryCompute(cmp, c.Value, op, value);
                if (b == true) return Proof.True;
                if (b == false) return Proof.False;
            }
            else if (c.Operator == op && cmp.AreEqual(c.Value, value))
            {
                return Proof.True;
            }
        }

        return Proof.Unknown;
    }

    private enum Proof 
    { 
        True, 
        False, 
        Unknown, 
    }

    private enum OperandKind 
    { 
        Field, 
        Const, 
        Unknown 
        }

    private readonly struct Operand
    {
        public OperandKind Kind { get; private init; }
        public IReadOnlyList<string>? Field { get; private init; }
        public RuleValue Value { get; private init; }
        public static Operand AsField(IReadOnlyList<string> f) => new() { Kind = OperandKind.Field, Field = f };
        public static Operand AsConst(RuleValue v) => new() { Kind = OperandKind.Const, Value = v };
        public static readonly Operand Unknown = new() { Kind = OperandKind.Unknown };
    }

    private static Operand Classify(RuleExpression expr, RuleContext ctx)
    {
        if (TryResourceField(expr, out var path)) return Operand.AsField(path);
        if (TryConst(expr, ctx, out var value)) return Operand.AsConst(value);
        return Operand.Unknown;
    }

    private static bool TryResourceField(RuleExpression expr, out IReadOnlyList<string> path)
    {
        var members = new List<string>();
        var e = expr;
        while (e is MemberExpression m)
        {
            members.Insert(0, m.Name);
            e = m.Target;
        }
        if (e is VarExpression { Name: "resource" } && members.Count >= 1)
        {
            path = members;
            return true;
        }
        path = [];
        return false;
    }

    private static bool TryConst(RuleExpression expr, RuleContext ctx, out RuleValue value)
    {
        switch (expr)
        {
            case LiteralExpression l:
                value = l.Value;
                return true;

            case VarExpression v:
                if (v.Name == "resource") break;                 // symbolic
                if (v.Name == "request") { value = ctx.Request; return true; }
                if (ctx.Params.TryGetValue(v.Name, out var p)) { value = p; return true; }
                break;                                            // unbound (per-result) wildcard → not const

            case MemberExpression m:
                if (TryConst(m.Target, ctx, out var t) && t.Kind == RuleValueKind.Map &&
                    t.AsMap.TryGetValue(m.Name, out var mv))
                {
                    value = mv;
                    return true;
                }
                break;

            case IndexExpression ix:
                if (TryConst(ix.Target, ctx, out var tt) && TryConst(ix.Index, ctx, out var idx))
                {
                    if (tt.Kind == RuleValueKind.Map && idx.Kind == RuleValueKind.String && tt.AsMap.TryGetValue(idx.AsString, out var iv))
                    {
                        value = iv;
                        return true;
                    }
                    if (tt.Kind == RuleValueKind.List && idx.Kind == RuleValueKind.Int && idx.AsInt >= 0 && idx.AsInt < tt.AsList.Count)
                    {
                        value = tt.AsList[(int)idx.AsInt];
                        return true;
                    }
                }
                break;
        }
        value = RuleValue.Null;
        return false;
    }

    private static bool? TryCompute(IRuleValueComparer cmp, RuleValue left, ComparisonOperator op, RuleValue right) => op switch
    {
        ComparisonOperator.Eq => cmp.AreEqual(left, right),
        ComparisonOperator.Ne => !cmp.AreEqual(left, right),
        _ => cmp.TryCompare(left, right, out var c)
            ? op switch { ComparisonOperator.Lt => c < 0, ComparisonOperator.Le => c <= 0, ComparisonOperator.Gt => c > 0, ComparisonOperator.Ge => c >= 0, _ => false }
            : null,
    };

    private static ComparisonOperator Flip(ComparisonOperator op) => op switch
    {
        ComparisonOperator.Lt => ComparisonOperator.Gt,
        ComparisonOperator.Le => ComparisonOperator.Ge,
        ComparisonOperator.Gt => ComparisonOperator.Lt,
        ComparisonOperator.Ge => ComparisonOperator.Le,
        _ => op,
    };

    private static Proof Negate(Proof p) => p switch
    {
        Proof.True => Proof.False,
        Proof.False => Proof.True,
        _ => Proof.Unknown,
    };
}
