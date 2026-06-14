using Winche.Rules;
using Winche.Rules.Evaluation;
using Xunit;

namespace Winche.Rules.Tests;

public class RuleRequestTests
{
    [Fact]
    public void Defaults_AreNullAndNoParams()
    {
        var req = new RuleRequest();
        Assert.True(req.Resource.IsNull);
        Assert.True(req.Request.IsNull);
        Assert.Empty(req.Params);
        Assert.Null(req.Provider);
    }
}
