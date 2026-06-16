using Winche.Rules.Matching;
using Xunit;

namespace Winche.Rules.Tests.Matching;

public class PathMatcherTests
{
    [Theory]
    [InlineData("users/{userId}", "users/u1", true)]
    [InlineData("users/{userId}", "users/u1/posts/p1", false)]   // single capture = exactly one segment
    [InlineData("users/{document=**}", "users/u1", true)]
    [InlineData("users/{document=**}", "users/u1/posts/p1", true)]
    [InlineData("users/{document=**}", "users", false)]          // recursive = one or more
    [InlineData("{document=**}", "anything/at/any/depth", true)]
    [InlineData("users/u1", "users/u1", true)]                   // literal
    [InlineData("users/u1", "users/u2", false)]
    public void IsMatch(string pattern, string path, bool expected) =>
        Assert.Equal(expected, PathMatcher.IsMatch(pattern, path));

    [Fact]
    public void IsMatch_RejectsBareWildcardPattern() =>
        Assert.Throws<System.ArgumentException>(() => PathMatcher.IsMatch("users/*/posts", "users/u1/posts"));
}
