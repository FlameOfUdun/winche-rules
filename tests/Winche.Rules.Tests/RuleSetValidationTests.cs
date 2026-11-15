using Winche.Rules.Expressions;
using Winche.Rules.Text;
using Xunit;

using Winche.Values;
namespace Winche.Rules.Tests;

public class RuleSetValidationTests
{
    private static readonly AllowRule ReadAll = new([RuleOperation.Get], new LiteralExpression(RuleValues.True));

    private static MatchBlock Block(string path, params MatchBlock[] nested) => new(path, [ReadAll], nested);

    [Fact]
    public void ParsedRules_AreValid()
    {
        var rules = RuleText.ParseRuleSet("""
            match users/{uid} {
              allow read: if request.auth.uid == uid;
              match notes/{rest=**} { allow read; }
            }
            """);

        Assert.Empty(RuleSetValidation.Validate(rules));
    }

    [Fact]
    public void EmptyRuleSet_IsValid() => Assert.Empty(RuleSetValidation.Validate(new RuleSet([])));

    [Fact]
    public void BadPath_ReportsTheBlock_AndKeepsCheckingSiblings()
    {
        var issues = RuleSetValidation.Validate(new RuleSet([Block("users/{"), Block("a/{x=**}/b/{y=**}")]));

        Assert.Equal(2, issues.Count);
        Assert.Equal("users/{", issues[0].Path);
        Assert.Equal("a/{x=**}/b/{y=**}", issues[1].Path);
    }

    [Fact]
    public void NestedMatchUnderRecursiveWildcard_IsRejected()
    {
        var issue = Assert.Single(RuleSetValidation.Validate(new RuleSet([Block("docs/{rest=**}", Block("x/{id}"))])));
        Assert.Equal("docs/{rest=**}", issue.Path);
        Assert.Contains("cannot contain nested matches", issue.Message);
    }

    [Fact]
    public void TwoRecursiveWildcardsAcrossParents_ReportFullPath()
    {
        var issue = Assert.Single(RuleSetValidation.Validate(
            new RuleSet([Block("a/{x=**}/b", Block("c/{y=**}"))])));
        Assert.Equal("a/{x=**}/b/c/{y=**}", issue.Path);
    }

    [Fact]
    public void NullMatchLists_FromJson_AreTolerated()
    {
        Assert.Empty(RuleSetValidation.Validate(new RuleSet(null!)));
        Assert.Empty(RuleSetValidation.Validate(new RuleSet([new MatchBlock("a/{id}", [ReadAll], null!)])));
    }
}
