using Winche.Rules;
using Winche.Rules.Expressions;
using Xunit;

namespace Winche.Rules.Tests;

public class PathValidationTests
{
    private static void Build(string path) =>
        RuleSetBuilder.Build(r =>
            r.Match(path, m => m.Allow(RuleOperations.Read, Expr.Const(true))));

    // ── P1: malformed patterns must be rejected, not silently literalized ──────
    [Theory]
    [InlineData("users/*/posts")]   // bare single wildcard
    [InlineData("users/*")]         // bare trailing single wildcard
    [InlineData("a/b**")]           // wildcard mixed with text, no braces
    [InlineData("users/{}")]        // empty capture
    [InlineData("users/{id")]       // unbalanced open brace
    [InlineData("users/id}")]       // unbalanced close brace
    [InlineData("a{id}")]           // braces don't wrap the whole segment
    [InlineData("users/{id=*}")]    // '=' allowed only in '{name=**}'
    [InlineData("users/{a b}")]     // capture name not an identifier
    [InlineData("{x=**}/b/{y=**}")] // two recursive wildcards
    public void Build_RejectsMalformedPattern(string path) =>
        Assert.Throws<ArgumentException>(() => Build(path));

    [Theory]
    [InlineData("users/{userId}")]
    [InlineData("files/{path=**}")]
    [InlineData("users/{userId}/posts/{postId}")]
    [InlineData("a/b/c")]
    public void Build_AcceptsValidPattern(string path)
    {
        var ex = Record.Exception(() => Build(path));
        Assert.Null(ex);
    }

    // ── P2: nested matches under a recursive wildcard are unreachable → reject ──
    [Fact]
    public void Build_NestedMatchUnderRecursive_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RuleSetBuilder.Build(r =>
                r.Match("files/{path=**}", m =>
                    m.Match("more/{id}", n =>
                        n.Allow(RuleOperations.Read, Expr.Const(true))))));
    }

    [Fact]
    public void Build_RecursiveWithoutNesting_Ok()
    {
        var ex = Record.Exception(() =>
            RuleSetBuilder.Build(r =>
                r.Match("files/{path=**}", m => m.Allow(RuleOperations.Read, Expr.Const(true)))));
        Assert.Null(ex);
    }
}
