using Winche.Rules.Evaluation;
using Winche.Rules.Expressions;
using Winche.Rules.Matching;

namespace Winche.Rules;

/// <summary>
/// One structural problem in a <see cref="RuleSet"/>. <see cref="Path"/> is the offending match block's full
/// (parent-joined) path when known, or <see langword="null"/> for a problem with the ruleset as a whole.
/// </summary>
public sealed record RuleValidationIssue(string? Path, string Message);

/// <summary>
/// Structural checks for a <see cref="RuleSet"/> that did not come from <see cref="Text.RuleText.ParseRuleSet"/>
/// (for example, rules stored as JSON): every match path parses, a match ending in a recursive wildcard has no
/// nested matches, and no match path (including its parents') has more than one recursive wildcard; its
/// functions have usable, unique names, are called with the right number of arguments, and aren't recursive.
/// </summary>
public static class RuleSetValidation
{
    /// <summary>Returns every problem found; an empty list means the ruleset is valid.</summary>
    public static IReadOnlyList<RuleValidationIssue> Validate(RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        var issues = new List<RuleValidationIssue>();
        Walk(ruleSet.Matches ?? [], [], "", issues);
        if (issues.Count == 0)
            foreach (var problem in FunctionChecks.Check(ruleSet))
                issues.Add(new(problem.Path, problem.Message));
        return issues;
    }

    /// <summary>
    /// Calls to functions that are neither built in nor defined where they're called (most likely a typo; such a
    /// call always fails, so its rule never grants), sorted and without repeats.
    /// </summary>
    public static IReadOnlyList<string> UnknownFunctions(RuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        void Expr(RuleExpression? x, FunctionScope scope)
        {
            switch (x)
            {
                case CallExpression c:
                    if (!RuleFunctions.Names.Contains(c.Name) && scope.Resolve(c.Name) is null) names.Add(c.Name);
                    foreach (var a in c.Args) Expr(a, scope);
                    break;
                case MemberExpression m: Expr(m.Target, scope); break;
                case IndexExpression ix: Expr(ix.Target, scope); Expr(ix.Index, scope); break;
                case CompareExpression cmp: Expr(cmp.Left, scope); Expr(cmp.Right, scope); break;
                case AndExpression a: foreach (var o in a.Operands) Expr(o, scope); break;
                case OrExpression o: foreach (var p in o.Operands) Expr(p, scope); break;
                case NotExpression n: Expr(n.Operand, scope); break;
                case InExpression i: Expr(i.Item, scope); Expr(i.Collection, scope); break;
                case ConditionalExpression cond: Expr(cond.Condition, scope); Expr(cond.Then, scope); Expr(cond.Else, scope); break;
            }
        }
        void Functions(IReadOnlyList<RuleFunction>? functions, FunctionScope scope)
        {
            foreach (var f in functions ?? [])
            {
                foreach (var l in f.Lets ?? []) Expr(l.Value, scope);
                Expr(f.Body, scope);
            }
        }
        void Block(MatchBlock b, FunctionScope outer)
        {
            var scope = outer.With(b.Functions);
            Functions(b.Functions, scope);
            foreach (var a in b.Allow ?? []) Expr(a.Condition, scope);
            foreach (var m in b.Matches ?? []) Block(m, scope);
        }
        var top = FunctionScope.Empty.With(ruleSet.Functions);
        Functions(ruleSet.Functions, top);
        foreach (var m in ruleSet.Matches ?? []) Block(m, top);
        return [.. names];
    }

    private static void Walk(
        IReadOnlyList<MatchBlock> blocks, IReadOnlyList<PathSegment> prefix, string prefixText, List<RuleValidationIssue> issues)
    {
        foreach (var block in blocks)
        {
            if (block is null)
            {
                issues.Add(new(prefixText.Length == 0 ? null : prefixText, "match block is empty."));
                continue;
            }

            var label = prefixText.Length == 0 ? block.Path ?? "" : $"{prefixText}/{block.Path}";
            var nested = block.Matches ?? [];

            IReadOnlyList<PathSegment> own;
            try
            {
                own = RulePath.Parse(block.Path ?? "");
            }
            catch (ArgumentException ex)
            {
                issues.Add(new(label, ex.Message));
                Walk(nested, prefix, label, issues);   // still report problems further down
                continue;
            }

            IReadOnlyList<PathSegment> full = [.. prefix, .. own];
            if (nested.Count > 0 && own.Count > 0 && own[^1] is RecursiveSegment)
                issues.Add(new(label,
                    $"match '{block.Path}' ends in a recursive wildcard and cannot contain nested matches; " +
                    "nested rules would be unreachable."));
            if (RulePath.RecursiveCount(full) > 1)
                issues.Add(new(label,
                    $"match '{label}' has more than one recursive wildcard (including its parents'); it could never match."));

            Walk(nested, full, label, issues);
        }
    }
}
