using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Matching;

namespace Winche.Rules.Querying;

/// <summary>
/// Decides whether a <c>list</c> query is allowed — Firestore's "rules are not filters": the query is
/// allowed only when its constraints <b>provably</b> guarantee every result satisfies a read rule;
/// otherwise it is rejected (the client must constrain the query). The analysis is <b>sound</b> — it never
/// returns <c>true</c> for a query that isn't provably safe — and intentionally incomplete (it rejects
/// queries it cannot prove, including any rule using <c>get()</c>/<c>exists()</c>, ternaries, <c>in</c>,
/// or per-result path wildcards).
/// </summary>
public static class QueryAnalyzer
{
    private enum Proof { True, False, Unknown }

    /// <summary>Whether the <paramref name="query"/> is allowed under <paramref name="ruleset"/>'s read rules.</summary>
    public static bool Allows(Ruleset ruleset, QueryConstraints query, RuleContext context)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var (pattern, allow) in RuleFlatten.Flatten(ruleset.Matches, []))
        {
            if (!allow.Operations.Contains(RuleOperation.List)) continue;
            if (!Governs(pattern, query.Collection, out var parentParams)) continue;

            var ctx = new RuleContext { Request = context.Request, Params = parentParams, Documents = context.Documents };
            if (Prove(allow.Condition, query, ctx) == Proof.True)
                return true;
        }
        return false;   // not provably safe → reject
    }

    // A block governs a list on `collection` when its last segment is a wildcard (the per-result doc id) and
    // its prefix matches the collection path (binding the parent wildcards to concrete values).
    // For a ParamSegment leaf the prefix must match the collection path exactly (same number of segments).
    // For a RecursiveSegment leaf the prefix only needs to match the leading segments of the collection path
    // (the collection may have additional segments beyond the prefix — e.g. subcollections); a lone recursive
    // segment (empty prefix) governs any collection.
    private static bool Governs(IReadOnlyList<PathSegment> pattern, string collection, out Dictionary<string, RuleValue> parentParams)
    {
        parentParams = [];
        if (pattern.Count == 0) return false;
        var leaf = pattern[^1];
        var prefix = pattern.Take(pattern.Count - 1).ToList();
        return leaf switch
        {
            ParamSegment => PathPattern.TryMatch(prefix, collection, out parentParams),
            RecursiveSegment => PathPattern.TryMatchPrefix(prefix, collection, out parentParams),
            _ => false,   // LiteralSegment — governs a specific document, not a collection list
        };
    }

    private static Proof Prove(RuleExpr expr, QueryConstraints query, RuleContext ctx)
    {
        switch (expr)
        {
            case LiteralExpr { Value.Kind: RuleValueKind.Bool } l:
                return l.Value.AsBool ? Proof.True : Proof.False;

            case NotExpr n:
                return Negate(Prove(n.Operand, query, ctx));

            case AndExpr a:
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

            case OrExpr o:
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

            case CompareExpr c:
                return ProveCompare(c, query, ctx);

            default:
                return Proof.Unknown;   // in / ternary / get() / exists() / size() / bare refs → can't prove
        }
    }

    private static Proof ProveCompare(CompareExpr c, QueryConstraints query, RuleContext ctx)
    {
        var left = Classify(c.Left, ctx);
        var right = Classify(c.Right, ctx);

        if (left.Kind == OperandKind.Const && right.Kind == OperandKind.Const)
        {
            var b = TryCompute(left.Value, c.Op, right.Value);
            return b is null ? Proof.Unknown : b.Value ? Proof.True : Proof.False;
        }
        if (left.Kind == OperandKind.Field && right.Kind == OperandKind.Const)
            return ProveAtom(left.Field!, c.Op, right.Value, query);
        if (left.Kind == OperandKind.Const && right.Kind == OperandKind.Field)
            return ProveAtom(right.Field!, Flip(c.Op), left.Value, query);

        return Proof.Unknown;   // field-field, or anything involving an unclassifiable operand
    }

    // Is the atom `resource.data.<field> op value` proven (or disproven) by the query's constraints?
    private static Proof ProveAtom(IReadOnlyList<string> field, CompareOp op, RuleValue value, QueryConstraints query)
    {
        foreach (var c in query.Constraints)
        {
            if (!c.Field.SequenceEqual(field)) continue;

            if (c.Op == CompareOp.Eq)
            {
                // The query pins the field to c.Value for every result, so the atom's truth is determined.
                var b = TryCompute(c.Value, op, value);
                if (b == true) return Proof.True;
                if (b == false) return Proof.False;
            }
            else if (c.Op == op && c.Value.Equals(value))
            {
                return Proof.True;   // exact range match (e.g. query age>=18 proves rule age>=18)
            }
        }
        return Proof.Unknown;
    }

    // ── Operand classification ────────────────────────────────────────────────

    private enum OperandKind { Field, Const, Unknown }

    private readonly struct Operand
    {
        public OperandKind Kind { get; private init; }
        public IReadOnlyList<string>? Field { get; private init; }
        public RuleValue Value { get; private init; }
        public static Operand AsField(IReadOnlyList<string> f) => new() { Kind = OperandKind.Field, Field = f };
        public static Operand AsConst(RuleValue v) => new() { Kind = OperandKind.Const, Value = v };
        public static readonly Operand Unknown = new() { Kind = OperandKind.Unknown };
    }

    private static Operand Classify(RuleExpr expr, RuleContext ctx)
    {
        if (TryResourceField(expr, out var path)) return Operand.AsField(path);
        if (TryConst(expr, ctx, out var value)) return Operand.AsConst(value);
        return Operand.Unknown;
    }

    // Recognizes `resource.data.<f1>.<f2>…` and returns [f1, f2, …].
    private static bool TryResourceField(RuleExpr expr, out IReadOnlyList<string> path)
    {
        var members = new List<string>();
        var e = expr;
        while (e is MemberExpr m)
        {
            members.Insert(0, m.Name);
            e = m.Target;
        }
        if (e is VarExpr { Name: "resource" } && members.Count >= 2 && members[0] == "data")
        {
            path = members.GetRange(1, members.Count - 1);
            return true;
        }
        path = [];
        return false;
    }

    // Evaluates the constant subset (literal / request / auth / params, no resource, no functions).
    private static bool TryConst(RuleExpr expr, RuleContext ctx, out RuleValue value)
    {
        switch (expr)
        {
            case LiteralExpr l:
                value = l.Value;
                return true;

            case VarExpr v:
                if (v.Name == "resource") break;                 // symbolic
                if (v.Name == "request") { value = ctx.Request; return true; }
                if (ctx.Params.TryGetValue(v.Name, out var p)) { value = p; return true; }
                break;                                            // unbound (per-result) wildcard → not const

            case MemberExpr m:
                if (TryConst(m.Target, ctx, out var t) && t.Kind == RuleValueKind.Map &&
                    t.AsMap.TryGetValue(m.Name, out var mv))
                {
                    value = mv;
                    return true;
                }
                break;

            case IndexExpr ix:
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool? TryCompute(RuleValue left, CompareOp op, RuleValue right) => op switch
    {
        CompareOp.Eq => left.Equals(right),
        CompareOp.Ne => !left.Equals(right),
        _ => left.TryCompareTo(right, out var c)
            ? op switch { CompareOp.Lt => c < 0, CompareOp.Le => c <= 0, CompareOp.Gt => c > 0, CompareOp.Ge => c >= 0, _ => false }
            : null,
    };

    private static CompareOp Flip(CompareOp op) => op switch
    {
        CompareOp.Lt => CompareOp.Gt,
        CompareOp.Le => CompareOp.Ge,
        CompareOp.Gt => CompareOp.Lt,
        CompareOp.Ge => CompareOp.Le,
        _ => op,
    };

    private static Proof Negate(Proof p) => p switch
    {
        Proof.True => Proof.False,
        Proof.False => Proof.True,
        _ => Proof.Unknown,
    };
}
