using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliExceptionsTests
{
    [TestMethod]
    public void TemporaryFailuresAreTransientAndUserActionable()
    {
        var e = new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.RiskControl, "playurl", -352);
        Assert.IsTrue(TransientNetworkError.IsTransient(e));
        Assert.IsInstanceOfType(e, typeof(IUserActionableException));
        Assert.AreEqual("Bilibili playurl is temporarily unavailable (RiskControl, code -352).", e.Message);

        var http = new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.RiskControl, "view", null,
            httpStatus: 412);
        Assert.AreEqual("Bilibili view is temporarily unavailable (RiskControl, HTTP 412).", http.Message);
        Assert.AreEqual(412, http.HttpStatus);
    }

    [TestMethod]
    public void FatalFailuresAreNotTransient()
    {
        Assert.IsFalse(TransientNetworkError.IsTransient(new BilibiliApiException("view", -12345, "x")));
        Assert.IsFalse(TransientNetworkError.IsTransient(new BilibiliProtocolException("view", "not JSON")));
        Assert.IsFalse(TransientNetworkError.IsTransient(new BilibiliNotLoggedInException("x")));
        Assert.IsInstanceOfType(new BilibiliNotLoggedInException("x"), typeof(IUserActionableException));
    }

    [TestMethod]
    public void ApiMessagesAreRedactedAndCapped()
    {
        var e = new BilibiliApiException("playurl", -12345,
            "see https://upos-sz-example.bilivideo.com/a/b.m4s?deadline=1&oi=2&upsig=3 and " +
            "//aisubtitle.hdslb.com/bfs/subtitle/1.json?auth_key=1-2-3-4 " + new string('x', 1000));
        BilibiliSecrets.AssertClean(e, allowRedactedUrls: true);
        Assert.IsFalse(e.Message.Contains('?'), e.Message);
        Assert.IsTrue(e.Message.Length < 300, e.Message);
        StringAssert.Contains(e.Message, "code -12345");
    }
}
