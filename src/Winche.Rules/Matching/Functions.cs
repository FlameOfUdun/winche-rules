using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;

namespace Winche.Rules.Matching;

/// <summary>The functions visible somewhere: a block's own, then its parents', then the file's.</summary>
internal sealed class FunctionScope
{
    private readonly Dictionary<string, RuleFunction> _own;
    private readonly FunctionScope? _outer;

    public static readonly FunctionScope Empty = new(null, null);

    private FunctionScope(IReadOnlyList<RuleFunction>? functions, FunctionScope? outer)
    {
        _own = new(StringComparer.Ordinal);
        foreach (var f in functions ?? []) _own.TryAdd(f.Name, f);   // a duplicate is reported by FunctionChecks
        _outer = outer;
    }

    public FunctionScope With(IReadOnlyList<RuleFunction>? functions) =>
        functions is { Count: > 0 } ? new FunctionScope(functions, this) : this;

    /// <summary>The function a call to <paramref name="name"/> means here, and the scope it was defined in.</summary>
    public (RuleFunction Function, FunctionScope Defined)? Resolve(string name)
    {
        for (var s = this; s is not null; s = s._outer)
            if (s._own.TryGetValue(name, out var f)) return (f, s);
        return null;
    }
}

/// <summary>Expands calls to rules-file functions into their bodies, so evaluation and query analysis see plain expressions.</summary>
internal static class FunctionInliner
{
    private const int MaxDepth = 32;

    public static RuleExpression Inline(RuleExpression e, FunctionScope scope) => Inline(e, scope, 0);

    private static RuleExpression Inline(RuleExpression e, FunctionScope scope, int depth) => e switch
    {
        CallExpression call => InlineCall(call, scope, depth),
        MemberExpression m => m with { Target = Inline(m.Target, scope, depth) },
        IndexExpression ix => new IndexExpression(Inline(ix.Target, scope, depth), Inline(ix.Index, scope, depth)),
        CompareExpression c => c with { Left = Inline(c.Left, scope, depth), Right = Inline(c.Right, scope, depth) },
        AndExpression a => new AndExpression([.. a.Operands.Select(o => Inline(o, scope, depth))]),
        OrExpression o => new OrExpression([.. o.Operands.Select(x => Inline(x, scope, depth))]),
        NotExpression n => new NotExpression(Inline(n.Operand, scope, depth)),
        InExpression i => new InExpression(Inline(i.Item, scope, depth), Inline(i.Collection, scope, depth)),
        ConditionalExpression c => new ConditionalExpression(Inline(c.Condition, scope, depth), Inline(c.Then, scope, depth), Inline(c.Else, scope, depth)),
        _ => e,
    };

    private static RuleExpression InlineCall(CallExpression call, FunctionScope scope, int depth)
    {
        var args = call.Args.Select(a => Inline(a, scope, depth)).ToList();
        // Not a rules-file function (a built-in, or unknown), the wrong arity, or runaway recursion: leave the call,
        // which the evaluator runs as a built-in or reports as an error (a deny).
        if (scope.Resolve(call.Name) is not var (fn, defined) || fn.Parameters.Count != args.Count || depth >= MaxDepth)
            return new CallExpression(call.Name, args);

        var bindings = new Dictionary<string, RuleExpression>(StringComparer.Ordinal);
        for (var i = 0; i < args.Count; i++) bindings[fn.Parameters[i]] = args[i];
        foreach (var let in fn.Lets ?? []) bindings[let.Name] = Substitute(let.Value, bindings);
        return Inline(Substitute(fn.Body, bindings), defined, depth + 1);
    }

    /// <summary>Replaces each variable named in <paramref name="bindings"/> (without looking inside what replaces it).</summary>
    private static RuleExpression Substitute(RuleExpression e, IReadOnlyDictionary<string, RuleExpression> bindings) => e switch
    {
        VarExpression v => bindings.TryGetValue(v.Name, out var to) ? to : v,
        CallExpression c => new CallExpression(c.Name, [.. c.Args.Select(a => Substitute(a, bindings))]),
        MemberExpression m => m with { Target = Substitute(m.Target, bindings) },
        IndexExpression ix => new IndexExpression(Substitute(ix.Target, bindings), Substitute(ix.Index, bindings)),
        CompareExpression c => c with { Left = Substitute(c.Left, bindings), Right = Substitute(c.Right, bindings) },
        AndExpression a => new AndExpression([.. a.Operands.Select(o => Substitute(o, bindings))]),
        OrExpression o => new OrExpression([.. o.Operands.Select(x => Substitute(x, bindings))]),
        NotExpression n => new NotExpression(Substitute(n.Operand, bindings)),
        InExpression i => new InExpression(Substitute(i.Item, bindings), Substitute(i.Collection, bindings)),
        ConditionalExpression c => new ConditionalExpression(Substitute(c.Condition, bindings), Substitute(c.Then, bindings), Substitute(c.Else, bindings)),
        _ => e,
    };
}

/// <summary>Problems with a ruleset's functions: reserved or repeated names, wrong argument counts, recursion.</summary>
internal static class FunctionChecks
{
    /// <summary>
    /// One problem. <see cref="Function"/> is the definition it's about, or <see cref="In"/> the expression a bad
    /// <see cref="Call"/> is in; <see cref="Path"/> is the enclosing match's full path (null at the top level).
    /// </summary>
    public sealed record Problem(string Message, string? Path, RuleFunction? Function = null, RuleExpression? In = null, string? Call = null);

    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "timestamp", "bytes", "float", "path", "true", "false", "null", "in", "is", "request", "resource",
    };

    public static IEnumerable<Problem> Check(RuleSet ruleSet)
    {
        var problems = new List<Problem>();
        var top = FunctionScope.Empty.With(ruleSet.Functions);
        CheckDefinitions(ruleSet.Functions, top, null, problems);
        foreach (var block in ruleSet.Matches ?? []) CheckBlock(block, top, null, problems);
        return problems;
    }

    private static void CheckBlock(MatchBlock block, FunctionScope outer, string? parentPath, List<Problem> problems)
    {
        if (block is null) return;
        var path = parentPath is null ? block.Path : $"{parentPath}/{block.Path}";
        var scope = outer.With(block.Functions);
        CheckDefinitions(block.Functions, scope, path, problems);
        foreach (var allow in block.Allow ?? [])
            if (allow?.Condition is not null) CheckCalls(allow.Condition, scope, path, problems);
        foreach (var nested in block.Matches ?? []) CheckBlock(nested, scope, path, problems);
    }

    private static void CheckDefinitions(IReadOnlyList<RuleFunction>? functions, FunctionScope scope, string? path, List<Problem> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in functions ?? [])
        {
            if (Reserved.Contains(f.Name) || RuleFunctions.Names.Contains(f.Name))
                problems.Add(new($"'{f.Name}' is a built-in name; call the function something else", path, Function: f));
            else if (!seen.Add(f.Name))
                problems.Add(new($"'{f.Name}' is defined twice here", path, Function: f));

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in f.Parameters ?? [])
                if (!names.Add(p)) problems.Add(new($"'{f.Name}' has two parameters named '{p}'", path, Function: f));
            foreach (var let in f.Lets ?? [])
            {
                if (!names.Add(let.Name)) problems.Add(new($"'{let.Name}' is already a parameter or let in '{f.Name}'", path, Function: f));
                CheckCalls(let.Value, scope, path, problems);
            }
            if (f.Body is not null) CheckCalls(f.Body, scope, path, problems);

            if (Cycle(f, scope) is { } cycle)
                problems.Add(new(cycle.Count == 1
                    ? $"'{f.Name}' calls itself; functions can't be recursive"
                    : $"'{f.Name}' calls itself through {string.Join(" → ", cycle.Skip(1).Select(c => $"'{c}'"))}; functions can't be recursive", path, Function: f));
        }
    }

    private static void CheckCalls(RuleExpression e, FunctionScope scope, string? path, List<Problem> problems)
    {
        foreach (var call in Calls(e))
            if (scope.Resolve(call.Name) is var (fn, _) && fn.Parameters.Count != call.Args.Count)
                problems.Add(new($"'{call.Name}' takes {Count(fn.Parameters.Count, "argument")}, not {call.Args.Count}", path, In: e, Call: call.Name));
    }

    private static string Count(int n, string what) => $"{n} {what}{(n == 1 ? "" : "s")}";

    /// <summary>The functions on a path from <paramref name="start"/> back to itself, or null.</summary>
    private static List<string>? Cycle(RuleFunction start, FunctionScope scope)
    {
        var stack = new List<string>();
        var visiting = new HashSet<RuleFunction>(ReferenceEqualityComparer.Instance);
        List<string>? Visit(RuleFunction f, FunctionScope defined)
        {
            stack.Add(f.Name);
            visiting.Add(f);
            foreach (var call in (f.Lets ?? []).Select(l => l.Value).Append(f.Body).Where(x => x is not null).SelectMany(Calls))
            {
                if (defined.Resolve(call.Name) is not var (g, gDefined)) continue;
                if (ReferenceEquals(g, start)) return [.. stack];
                if (visiting.Contains(g)) continue;
                if (Visit(g, gDefined) is { } found) return found;
            }
            stack.RemoveAt(stack.Count - 1);
            return null;
        }
        return scope.Resolve(start.Name) is var (_, defined) ? Visit(start, defined) : null;
    }

    private static IEnumerable<CallExpression> Calls(RuleExpression e)
    {
        switch (e)
        {
            case CallExpression c:
                yield return c;
                foreach (var a in c.Args) foreach (var x in Calls(a)) yield return x;
                break;
            case MemberExpression m: foreach (var x in Calls(m.Target)) yield return x; break;
            case IndexExpression ix: foreach (var x in Calls(ix.Target).Concat(Calls(ix.Index))) yield return x; break;
            case CompareExpression c: foreach (var x in Calls(c.Left).Concat(Calls(c.Right))) yield return x; break;
            case AndExpression a: foreach (var x in a.Operands.SelectMany(Calls)) yield return x; break;
            case OrExpression o: foreach (var x in o.Operands.SelectMany(Calls)) yield return x; break;
            case NotExpression n: foreach (var x in Calls(n.Operand)) yield return x; break;
            case InExpression i: foreach (var x in Calls(i.Item).Concat(Calls(i.Collection))) yield return x; break;
            case ConditionalExpression c: foreach (var x in Calls(c.Condition).Concat(Calls(c.Then)).Concat(Calls(c.Else))) yield return x; break;
        }
    }
}
