using Winche.Values;

namespace Winche.Rules.Tests;

public class RefValueTests
{
    [Fact]
    public void File_and_user_references_have_type_names_and_equal_their_string()
    {
        Assert.Equal(("fileReference", "userReference", "documentReference"),
            (RuleValues.TypeName(new FileReferenceValue("a.png")), RuleValues.TypeName(new UserReferenceValue("u1")), RuleValues.TypeName(new DocumentReferenceValue("a/b"))));
        // resource.data.owner == request.auth.uid holds whether owner is a user reference or a string.
        Assert.True(RuleValues.AreEqual(new UserReferenceValue("u1"), new StringValue("u1")));
        Assert.True(RuleValues.AreEqual(new StringValue("scans/a.png"), new FileReferenceValue("scans/a.png")));
        Assert.False(RuleValues.AreEqual(new UserReferenceValue("u1"), new StringValue("u2")));
        Assert.False(RuleValues.AreEqual(new UserReferenceValue("x"), new FileReferenceValue("x")));   // different kinds of thing
    }
}
