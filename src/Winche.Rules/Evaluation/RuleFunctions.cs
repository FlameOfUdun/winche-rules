using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Winche.Rules.Evaluation;

/// <summary>
/// The pure built-in functions (no document source): every argument is evaluated first and the result
/// depends only on the arguments. A wrong argument count or type is an evaluation error, which denies.
/// <list type="table">
///   <item><term><c>size(x)</c></term><description>length of a string, list or map</description></item>
///   <item><term><c>keys(map)</c></term><description>its keys, as a list of strings</description></item>
///   <item><term><c>hasOnly(list, list)</c> / <c>hasAll</c> / <c>hasAny</c></term><description>every element of the first is in the second / the first contains every / any element of the second</description></item>
///   <item><term><c>diff(after, before)</c></term><description>a map of key lists: <c>added</c>, <c>removed</c>, <c>changed</c>, <c>unchanged</c>, <c>affected</c></description></item>
///   <item><term><c>affectedKeys(diff)</c> etc.</term><description>one list of a diff: <c>affectedKeys</c>, <c>addedKeys</c>, <c>removedKeys</c>, <c>changedKeys</c>, <c>unchangedKeys</c></description></item>
///   <item><term><c>is(x, 'type')</c></term><description>type check: string, int, float, number, bool, null, map, list, timestamp, bytes, path</description></item>
///   <item><term><c>matches(string, regex)</c></term><description>the whole string matches (100 ms limit)</description></item>
///   <item><term><c>lower(s)</c> / <c>upper(s)</c></term><description>case conversion (invariant)</description></item>
///   <item><term><c>get(map, key, default)</c></term><description>the value at key, or default when absent</description></item>
///   <item><term><c>path(part, …)</c></term><description>a path from its parts (4.1.0): strings and ints are one segment each, paths add their segments; what <c>/users/$(uid)</c> means</description></item>
/// </list>
/// </summary>
public static class RuleFunctions
{
    /// <summary>The type names <c>is</c> accepts.</summary>
    public static readonly IReadOnlyList<string> TypeNames =
        ["string", "int", "float", "number", "bool", "null", "map", "list", "timestamp", "bytes", "path", "latlng"];

    /// <summary>The pure function names, plus the source-backed <c>exists</c> and <c>get(path)</c>.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "exists", "get", "size", "keys", "hasOnly", "hasAll", "hasAny", "diff",
        "affectedKeys", "addedKeys", "removedKeys", "changedKeys", "unchangedKeys",
        "is", "matches", "lower", "upper", "path",
    ];

    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);
    private static readonly TimeSpan RegexLimit = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Calls the pure function <paramref name="name"/>; false when there is no such pure function (the
    /// caller then reports an unknown function).
    /// </summary>
    public static bool TryCall(string name, IReadOnlyList<Value> args, out Value result)
    {
        result = name switch
        {
            "size" => Size(One(name, args)),
            "keys" => new ArrayValue([.. Map(name, One(name, args)).Keys.Order(StringComparer.Ordinal).Select(s => (Value)new StringValue(s))]),
            "hasOnly" => RuleValues.Bool(List(name, Two(name, args).A).All(e => Contains(List(name, args[1]), e))),
            "hasAll" => RuleValues.Bool(List(name, Two(name, args).B).All(e => Contains(List(name, args[0]), e))),
            "hasAny" => RuleValues.Bool(List(name, Two(name, args).B).Any(e => Contains(List(name, args[0]), e))),
            "diff" => Diff(Map(name, Two(name, args).A), Map(name, args[1])),
            "affectedKeys" => DiffPart(name, args, "affected"),
            "addedKeys" => DiffPart(name, args, "added"),
            "removedKeys" => DiffPart(name, args, "removed"),
            "changedKeys" => DiffPart(name, args, "changed"),
            "unchangedKeys" => DiffPart(name, args, "unchanged"),
            "is" => RuleValues.Bool(Is(Two(name, args).A, String(name, args[1]))),
            "matches" => RuleValues.Bool(Matches(String(name, Two(name, args).A), String(name, args[1]))),
            "lower" => new StringValue(String(name, One(name, args)).ToLowerInvariant()),
            "upper" => new StringValue(String(name, One(name, args)).ToUpperInvariant()),
            "get" when args.Count == 3 => Map(name, args[0]).TryGetValue(String(name, args[1]), out var v) ? v : args[2],
            "path" => JoinPath(args),
            _ => RuleValues.Null,
        };
        return name is "size" or "keys" or "hasOnly" or "hasAll" or "hasAny" or "diff" or "affectedKeys" or "addedKeys"
            or "removedKeys" or "changedKeys" or "unchangedKeys" or "is" or "matches" or "lower" or "upper" or "path"
            || (name == "get" && args.Count == 3);
    }

    /// <summary>
    /// A path from its parts. A string or int is one segment, so a value can't add segments of its own
    /// (an id of <c>"u1/admin"</c> is an error, not two segments); a path value adds all of its segments.
    /// </summary>
    private static ReferenceValue JoinPath(IReadOnlyList<Value> args)
    {
        if (args.Count == 0) throw new RuleEvaluationException("path() needs at least one part.");
        var segments = new List<string>();
        foreach (var a in args)
        {
            switch (a)
            {
                case StringValue { Value: var s }:
                    if (s.Length == 0 || s.Contains('/'))
                        throw new RuleEvaluationException($"A path segment can't be empty or contain '/': '{s}'.");
                    segments.Add(s);
                    break;
                case IntegerValue { Value: var n }:
                    segments.Add(n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ReferenceValue { Path: var path }:
                    segments.AddRange(path.Split('/', StringSplitOptions.RemoveEmptyEntries));
                    break;
                default:
                    throw new RuleEvaluationException($"A path segment must be a string, int or path, not a {RuleValues.TypeName(a)}.");
            }
        }
        return new ReferenceValue(string.Join('/', segments));
    }

    private static IntegerValue Size(Value arg) => new IntegerValue(arg switch
    {
        StringValue s => s.Value.Length,
        ArrayValue l => l.Values.Count,
        MapValue m => m.Fields.Count,
        _ => throw new RuleEvaluationException($"size() requires a string/list/map, got {RuleValues.TypeName(arg)}."),
    });

    private static bool Contains(IReadOnlyList<Value> list, Value item) => list.Any(e => RuleValues.AreEqual(e, item));

    private static MapValue Diff(IReadOnlyDictionary<string, Value> after, IReadOnlyDictionary<string, Value> before)
    {
        var added = after.Keys.Where(k => !before.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var removed = before.Keys.Where(k => !after.ContainsKey(k)).Order(StringComparer.Ordinal).ToList();
        var both = after.Keys.Where(before.ContainsKey).Order(StringComparer.Ordinal).ToList();
        var changed = both.Where(k => !RuleValues.AreEqual(after[k], before[k])).ToList();
        var unchanged = both.Where(k => RuleValues.AreEqual(after[k], before[k])).ToList();
        var affected = added.Concat(removed).Concat(changed).Order(StringComparer.Ordinal).ToList();
        static Value Keys(IEnumerable<string> keys) => RuleValues.Strings(keys);
        return new MapValue(new Dictionary<string, Value>(StringComparer.Ordinal)
        {
            ["added"] = Keys(added), ["removed"] = Keys(removed), ["changed"] = Keys(changed),
            ["unchanged"] = Keys(unchanged), ["affected"] = Keys(affected),
        });
    }

    private static Value DiffPart(string name, IReadOnlyList<Value> args, string part) =>
        Map(name, One(name, args)).TryGetValue(part, out var keys) && keys is ArrayValue
            ? keys
            : throw new RuleEvaluationException($"{name}() expects the result of diff().");

    private static bool Is(Value v, string type) => type switch
    {
        "number" => v is IntegerValue or DoubleValue,
        _ when TypeNames.Contains(type) => RuleValues.TypeName(v) == type,
        _ => throw new RuleEvaluationException($"Unknown type '{type}' (expected one of {string.Join(", ", TypeNames)})."),
    };

    private static bool Matches(string input, string pattern)
    {
        Regex regex;
        try
        {
            regex = Patterns.GetOrAdd(pattern, p => new Regex($"^(?:{p})$", RegexOptions.CultureInvariant, RegexLimit));
        }
        catch (ArgumentException ex)
        {
            throw new RuleEvaluationException($"Invalid regex '{pattern}': {ex.Message}");
        }
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            throw new RuleEvaluationException($"Regex '{pattern}' took too long.");
        }
    }

    private static Value One(string name, IReadOnlyList<Value> args) =>
        args.Count == 1 ? args[0] : throw new RuleEvaluationException($"{name}() takes 1 argument, got {args.Count}.");

    private static (Value A, Value B) Two(string name, IReadOnlyList<Value> args) =>
        args.Count == 2 ? (args[0], args[1]) : throw new RuleEvaluationException($"{name}() takes 2 arguments, got {args.Count}.");

    private static IReadOnlyList<Value> List(string name, Value v) =>
        v is ArrayValue l ? l.Values : throw new RuleEvaluationException($"{name}() expects a list, got {RuleValues.TypeName(v)}.");

    private static IReadOnlyDictionary<string, Value> Map(string name, Value v) =>
        v is MapValue m ? m.Fields : throw new RuleEvaluationException($"{name}() expects a map, got {RuleValues.TypeName(v)}.");

    private static string String(string name, Value v) =>
        v is StringValue s ? s.Value : throw new RuleEvaluationException($"{name}() expects a string, got {RuleValues.TypeName(v)}.");
}
