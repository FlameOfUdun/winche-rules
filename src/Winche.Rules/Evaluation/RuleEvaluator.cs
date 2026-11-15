using Winche.Rules.Expressions;

namespace Winche.Rules.Evaluation;

/// <summary>
/// Evaluates a condition expression in memory against a <see cref="RuleContext"/>.
/// A condition grants only when it evaluates to boolean <c>true</c>; any other result — including an
/// evaluation error (missing field, null navigation, type mismatch) — denies.
/// </summary>
public static class RuleEvaluator
{
    /// <summary>
    /// Evaluates <paramref name="condition"/>; returns <c>true</c> only if it yields boolean true.
    /// </summary>
    public static async Task<bool> EvaluateAsync(RuleExpression condition, RuleContext context, CancellationToken ct = default)
    {
        try
        {
            var result = await EvalAsync(condition, context, ct).ConfigureAwait(false);
            return result is BooleanValue { Value: true };
        }
        catch (RuleEvaluationException)
        {
            return false;
        }
    }

    private static async Task<Value> EvalAsync(RuleExpression expr, RuleContext ctx, CancellationToken ct)
    {
        switch (expr)
        {
            case LiteralExpression l:
                return l.Value;

            case VarExpression v:
                return ResolveVar(v.Name, ctx);

            case MemberExpression m:
                return Member(await EvalAsync(m.Target, ctx, ct).ConfigureAwait(false), m.Name);

            case IndexExpression ix:
                return Index(
                    await EvalAsync(ix.Target, ctx, ct).ConfigureAwait(false),
                    await EvalAsync(ix.Index, ctx, ct).ConfigureAwait(false));

            case CompareExpression c:
                return RuleValues.Bool(Compare(
                    c.Op,
                    await EvalAsync(c.Left, ctx, ct).ConfigureAwait(false),
                    await EvalAsync(c.Right, ctx, ct).ConfigureAwait(false)));

            case NotExpression n:
                return RuleValues.Bool(!await BoolAsync(n.Operand, ctx, ct).ConfigureAwait(false));

            case AndExpression a:
                foreach (var op in a.Operands)
                    if (!await BoolAsync(op, ctx, ct).ConfigureAwait(false))
                        return RuleValues.False;
                return RuleValues.True;

            case OrExpression o:
                foreach (var op in o.Operands)
                    if (await BoolAsync(op, ctx, ct).ConfigureAwait(false))
                        return RuleValues.True;
                return RuleValues.False;

            case InExpression i:
                return RuleValues.Bool(In(
                    await EvalAsync(i.Item, ctx, ct).ConfigureAwait(false),
                    await EvalAsync(i.Collection, ctx, ct).ConfigureAwait(false)));

            case ConditionalExpression cond:
                return await BoolAsync(cond.Condition, ctx, ct).ConfigureAwait(false)
                    ? await EvalAsync(cond.Then, ctx, ct).ConfigureAwait(false)
                    : await EvalAsync(cond.Else, ctx, ct).ConfigureAwait(false);

            case CallExpression call:
                return await CallAsync(call, ctx, ct).ConfigureAwait(false);

            default:
                throw new RuleEvaluationException($"Unsupported expression '{expr.GetType().Name}'.");
        }
    }

    private static async Task<bool> BoolAsync(RuleExpression e, RuleContext ctx, CancellationToken ct)
    {
        var v = await EvalAsync(e, ctx, ct).ConfigureAwait(false);
        return v is BooleanValue b ? b.Value : throw new RuleEvaluationException("Expected a boolean.");
    }

    private static Value ResolveVar(string name, RuleContext ctx) => name switch
    {
        "resource" => ctx.Resource,
        "request" => ctx.Request,
        _ => ctx.Params.TryGetValue(name, out var v) ? v : throw new RuleEvaluationException($"Unknown variable '{name}'."),
    };

    private static Value Member(Value target, string name) => target switch
    {
        MapValue m => m.Fields.TryGetValue(name, out var v) ? v : throw new RuleEvaluationException($"Field '{name}' is missing."),
        GeoPointValue g when name == "latitude" => new DoubleValue(g.Latitude),
        GeoPointValue g when name == "longitude" => new DoubleValue(g.Longitude),
        _ => throw new RuleEvaluationException($"Cannot read '.{name}' of a {RuleValues.TypeName(target)}."),
    };

    private static Value Index(Value target, Value index) => (target, index) switch
    {
        (ArrayValue list, IntegerValue { Value: var i }) => i >= 0 && i < list.Values.Count
            ? list.Values[(int)i]
            : throw new RuleEvaluationException("List index out of range."),
        (MapValue map, StringValue key) => map.Fields.TryGetValue(key.Value, out var v) ? v : throw new RuleEvaluationException("Map key is missing."),
        _ => throw new RuleEvaluationException($"Cannot index a {RuleValues.TypeName(target)} with a {RuleValues.TypeName(index)}."),
    };

    private static bool Compare(ComparisonOperator op, Value left, Value right)
    {
        switch (op)
        {
            case ComparisonOperator.Eq: return RuleValues.AreEqual(left, right);
            case ComparisonOperator.Ne: return !RuleValues.AreEqual(left, right);
            default:
                if (!RuleValues.TryCompare(left, right, out var c))
                    throw new RuleEvaluationException($"Cannot order {RuleValues.TypeName(left)} and {RuleValues.TypeName(right)}.");
                return op switch
                {
                    ComparisonOperator.Lt => c < 0,
                    ComparisonOperator.Le => c <= 0,
                    ComparisonOperator.Gt => c > 0,
                    ComparisonOperator.Ge => c >= 0,
                    _ => false,
                };
        }
    }

    private static bool In(Value item, Value collection) => collection switch
    {
        ArrayValue list => list.Values.Any(e => RuleValues.AreEqual(e, item)),
        MapValue map => item is StringValue key && map.Fields.ContainsKey(key.Value),
        _ => throw new RuleEvaluationException($"'in' requires a list or map, got {RuleValues.TypeName(collection)}."),
    };

    private static async Task<Value> CallAsync(CallExpression call, RuleContext ctx, CancellationToken ct)
    {
        switch (call.Name)
        {
            case "exists":
            case "get" when call.Args.Count == 1:
                var source = ctx.Provider ?? throw new RuleEvaluationException($"'{call.Name}()' requires a document source.");
                var path = (await EvalAsync(Arg(call, 0), ctx, ct).ConfigureAwait(false));
                var p = path switch
                {
                    ReferenceValue r => r.Path,
                    StringValue str => str.Value,
                    _ => throw new RuleEvaluationException($"'{call.Name}()' expects a path string."),
                };
                return call.Name == "exists"
                    ? RuleValues.Bool(await source.ExistsAsync(p, ct).ConfigureAwait(false))
                    : await source.GetAsync(p, ct).ConfigureAwait(false);

            default:
                var args = new List<Value>(call.Args.Count);
                foreach (var a in call.Args) args.Add(await EvalAsync(a, ctx, ct).ConfigureAwait(false));
                return RuleFunctions.TryCall(call.Name, args, out var result)
                    ? result
                    : throw new RuleEvaluationException($"Unknown function '{call.Name}'.");
        }
    }

    private static RuleExpression Arg(CallExpression call, int i) =>
        i < call.Args.Count ? call.Args[i] : throw new RuleEvaluationException($"'{call.Name}()' is missing argument {i}.");
}
