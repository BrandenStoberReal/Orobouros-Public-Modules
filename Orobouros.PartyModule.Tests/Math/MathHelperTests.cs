using Orobouros.PartyModule.Helpers;

namespace Orobouros.PartyModule.Tests.Math;

public class MathHelperTests
{
    [Test]
    public void MathTest_FetchPostPage()
    {
        var examplePost = 5;
        var page = MathHelper.FetchPageForPost(examplePost);
        if (page == null) Assert.Fail();

        Assert.AreEqual(page, 1);
    }

    [Test]
    public void MathTest_FetchPostPage_Larger()
    {
        var examplePost = 75;
        var page = MathHelper.FetchPageForPost(examplePost);
        if (page == null) Assert.Fail();

        Assert.AreEqual(page, 2);
    }
}