using Winche.Rules.Expressions;

namespace Winche.Rules.Evaluation;

/// <summary>
/// Evaluates a condition expression in memory against a <see cref="RuleContext"/> (Firestore semantics).
/// A condition grants only when it evaluates to boolean <c>true</c>; any other result — including an
/// evaluation error (missing field, null navigation, type mismatch) — denies.
/// </summary>
public static class RuleEvaluator
{
    /// <summary>Evaluates <paramref name="condition"/>; returns <c>true</c> only if it yields boolean true.</summary>
    public static async Task<bool> EvaluateAsync(RuleExpr condition, RuleContext context, CancellationToken ct = default)
    {
        try
        {
            var result = await EvalAsync(condition, context, ct).ConfigureAwait(false);
            return result.Kind == RuleValueKind.Bool && result.AsBool;
        }
        catch (RuleEvaluationException)
        {
            return false;
        }
    }

    private static async Task<RuleValue> EvalAsync(RuleExpr expr, RuleContext ctx, CancellationToken ct)
    {
        switch (expr)
        {
            case LiteralExpr l:
                return l.Value;

            case VarExpr v:
                return ResolveVar(v.Name, ctx);

            case MemberExpr m:
                return Member(await EvalAsync(m.Target, ctx, ct).ConfigureAwait(false), m.Name);

            case IndexExpr ix:
                return Index(
                    await EvalAsync(ix.Target, ctx, ct).ConfigureAwait(false),
                    await EvalAsync(ix.Index, ctx, ct).ConfigureAwait(false));

            case CompareExpr c:
                return RuleValue.Bool(Compare(
                    c.Op,
                    await EvalAsync(c.Left, ctx, ct).ConfigureAwait(false),
                    await EvalAsync(c.Right, ctx, ct).ConfigureAwait(false)));

            case NotExpr n:
                return RuleValue.Bool(!await BoolAsync(n.Operand, ctx, ct).ConfigureAwait(false));

            case AndExpr a:
                foreach (var op in a.Operands)
                    if (!await BoolAsync(op, ctx, ct).ConfigureAwait(false))
                        return RuleValue.Bool(false);
                return RuleValue.Bool(true);

            case OrExpr o:
                foreach (var op in o.Operands)
                    if (await BoolAsync(op, ctx, ct).ConfigureAwait(false))
                        return RuleValue.Bool(true);
                return RuleValue.Bool(false);

            case InExpr i:
                return RuleValue.Bool(In(
                    await EvalAsync(i.Item, ctx, ct).ConfigureAwait(false),
                    await EvalAsync(i.Collection, ctx, ct).ConfigureAwait(false)));

            case ConditionalExpr cond:
                return await BoolAsync(cond.Condition, ctx, ct).ConfigureAwait(false)
                    ? await EvalAsync(cond.Then, ctx, ct).ConfigureAwait(false)
                    : await EvalAsync(cond.Else, ctx, ct).ConfigureAwait(false);

            case CallExpr call:
                return await CallAsync(call, ctx, ct).ConfigureAwait(false);

            default:
                throw new RuleEvaluationException($"Unsupported expression '{expr.GetType().Name}'.");
        }
    }

    private static async Task<bool> BoolAsync(RuleExpr e, RuleContext ctx, CancellationToken ct)
    {
        var v = await EvalAsync(e, ctx, ct).ConfigureAwait(false);
        return v.Kind == RuleValueKind.Bool ? v.AsBool : throw new RuleEvaluationException("Expected a boolean.");
    }

    private static RuleValue ResolveVar(string name, RuleContext ctx) => name switch
    {
        "resource" => ctx.Resource,
        "request" => ctx.Request,
        _ => ctx.Params.TryGetValue(name, out var v) ? v : throw new RuleEvaluationException($"Unknown variable '{name}'."),
    };

    private static RuleValue Member(RuleValue target, string name)
    {
        if (target.Kind != RuleValueKind.Map)
            throw new RuleEvaluationException($"Cannot read '.{name}' of a {target.Kind}.");
        return target.AsMap.TryGetValue(name, out var v) ? v : throw new RuleEvaluationException($"Field '{name}' is missing.");
    }

    private static RuleValue Index(RuleValue target, RuleValue index)
    {
        switch (target.Kind)
        {
            case RuleValueKind.List when index.Kind == RuleValueKind.Int:
                var list = target.AsList;
                var i = index.AsInt;
                if (i < 0 || i >= list.Count) throw new RuleEvaluationException("List index out of range.");
                return list[(int)i];
            case RuleValueKind.Map when index.Kind == RuleValueKind.String:
                return target.AsMap.TryGetValue(index.AsString, out var v) ? v : throw new RuleEvaluationException("Map key is missing.");
            default:
                throw new RuleEvaluationException($"Cannot index a {target.Kind} with a {index.Kind}.");
        }
    }

    private static bool Compare(CompareOp op, RuleValue left, RuleValue right)
    {
        switch (op)
        {
            case CompareOp.Eq: return left.Equals(right);
            case CompareOp.Ne: return !left.Equals(right);
            default:
                if (!left.TryCompareTo(right, out var c))
                    throw new RuleEvaluationException($"Cannot order {left.Kind} and {right.Kind}.");
                return op switch
                {
                    CompareOp.Lt => c < 0,
                    CompareOp.Le => c <= 0,
                    CompareOp.Gt => c > 0,
                    CompareOp.Ge => c >= 0,
                    _ => false,
                };
        }
    }

    private static bool In(RuleValue item, RuleValue collection) => collection.Kind switch
    {
        RuleValueKind.List => collection.AsList.Any(e => e.Equals(item)),
        RuleValueKind.Map => item.Kind == RuleValueKind.String && collection.AsMap.ContainsKey(item.AsString),
        _ => throw new RuleEvaluationException($"'in' requires a list or map, got {collection.Kind}."),
    };

    private static async Task<RuleValue> CallAsync(CallExpr call, RuleContext ctx, CancellationToken ct)
    {
        switch (call.Name)
        {
            case "exists":
            case "get":
                var source = ctx.Documents ?? throw new RuleEvaluationException($"'{call.Name}()' requires a document source.");
                var path = (await EvalAsync(Arg(call, 0), ctx, ct).ConfigureAwait(false));
                if (path.Kind is not (RuleValueKind.String or RuleValueKind.Path))
                    throw new RuleEvaluationException($"'{call.Name}()' expects a path string.");
                var p = path.Kind == RuleValueKind.Path ? path.AsPath : path.AsString;
                return call.Name == "exists"
                    ? RuleValue.Bool(await source.ExistsAsync(p, ct).ConfigureAwait(false))
                    : await source.GetAsync(p, ct).ConfigureAwait(false);

            case "size":
                var arg = await EvalAsync(Arg(call, 0), ctx, ct).ConfigureAwait(false);
                return RuleValue.Int(arg.Kind switch
                {
                    RuleValueKind.String => arg.AsString.Length,
                    RuleValueKind.List => arg.AsList.Count,
                    RuleValueKind.Map => arg.AsMap.Count,
                    _ => throw new RuleEvaluationException($"size() requires a string/list/map, got {arg.Kind}."),
                });

            default:
                throw new RuleEvaluationException($"Unknown function '{call.Name}'.");
        }
    }

    private static RuleExpr Arg(CallExpr call, int i) =>
        i < call.Args.Count ? call.Args[i] : throw new RuleEvaluationException($"'{call.Name}()' is missing argument {i}.");
}
