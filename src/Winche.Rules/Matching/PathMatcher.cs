namespace Winche.Rules.Matching;

/// <summary>
/// Public entry point for Firestore-faithful path matching. Reused by hosts (e.g. the database's
/// document hooks) so there is a single grammar across the system: literal segments, <c>{id}</c>
/// single-segment captures, and a trailing <c>{name=**}</c> recursive capture. Throws
/// <see cref="System.ArgumentException"/> on malformed patterns (bare <c>*</c>, partial/unbalanced
/// braces, non-identifier capture names, a non-trailing recursive wildcard).
/// </summary>
public static class PathMatcher
{
    /// <summary>True when <paramref name="path"/> matches the Firestore-style <paramref name="pattern"/>.</summary>
    public static bool IsMatch(string pattern, string path) =>
        RulePath.TryMatch(RulePath.Parse(pattern), path, out _);
}
