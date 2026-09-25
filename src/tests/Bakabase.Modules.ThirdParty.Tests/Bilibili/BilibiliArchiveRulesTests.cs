using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliArchiveRulesTests
{
    private static BilibiliViewOutcome Decide(string fixture, long aid = 1001, int depth = 0) =>
        BilibiliArchiveRules.DecideView(BilibiliFixtures.Wrapper<Post>(fixture), aid, depth);

    [TestMethod]
    public void PagesListedProceeds()
    {
        Assert.AreEqual(BilibiliViewOutcomeKind.Proceed, Decide("view-ok-2pages.json").Kind);
    }

    [DataTestMethod]
    [DataRow("view-62002.json", BilibiliSkipReason.Deleted, 62002)]
    [DataRow("view-62012.json", BilibiliSkipReason.PrivateToUploader, 62012)]
    [DataRow("view-62004.json", BilibiliSkipReason.UnderReview, 62004)]
    public void ContentStatesSkip(string fixture, BilibiliSkipReason reason, int code)
    {
        var o = Decide(fixture);
        Assert.AreEqual(BilibiliViewOutcomeKind.Skip, o.Kind);
        Assert.AreEqual(reason, o.Skip!.Reason);
        Assert.AreEqual(code, o.Skip.Code);
    }

    [TestMethod]
    public void NotFoundAsksPageListAndAccessDeniedFallsBackToIt()
    {
        Assert.AreEqual(BilibiliViewOutcomeKind.CheckExistence, Decide("view-404.json").Kind);
        Assert.AreEqual(BilibiliViewOutcomeKind.PageListFallback, Decide("view-403.json").Kind);
    }

    [TestMethod]
    public void AForwardIsFollowedOnceAndNeverToItself()
    {
        var o = Decide("view-forward.json", aid: 1008);
        Assert.AreEqual(BilibiliViewOutcomeKind.FollowForward, o.Kind);
        Assert.AreEqual(1001L, o.ForwardAid);

        Assert.AreEqual(BilibiliSkipReason.Deleted, Decide("view-forward.json", aid: 1008, depth: 1).Skip!.Reason);
        Assert.AreEqual(BilibiliSkipReason.Deleted, Decide("view-forward.json", aid: 1001).Skip!.Reason);
    }

    [TestMethod]
    public void InteractiveVideosSkip()
    {
        Assert.AreEqual(BilibiliSkipReason.InteractiveVideo, Decide("view-stein-gate.json").Skip!.Reason);
        Assert.IsTrue(BilibiliArchiveRules.IsInteractive(BilibiliFixtures.Data<Post>("view-stein-gate.json")));
        Assert.IsFalse(BilibiliArchiveRules.IsInteractive(BilibiliFixtures.Data<Post>("view-ok-2pages.json")));
    }

    [TestMethod]
    public void OtherContentCodesOnViewAreSkipsToo()
    {
        var supporter = BilibiliArchiveRules.DecideView(new DataWrapper<Post> {Code = 87008}, 1, 0);
        Assert.AreEqual(BilibiliSkipReason.SupporterOnly, supporter.Skip!.Reason);
        var region = BilibiliArchiveRules.DecideView(
            new DataWrapper<Post> {Code = -10403, Message = "抱歉您所在地区不可观看！"}, 1, 0);
        Assert.AreEqual(BilibiliSkipReason.RegionRestrictedOrHidden, region.Skip!.Reason);
    }

    [TestMethod]
    public void UnknownOrMisplacedCodesAreFatalNotSkips()
    {
        var unknown = Assert.ThrowsException<BilibiliApiException>(() => Decide("view-unknown-code.json"));
        Assert.AreEqual(-12345, unknown.Code);
        Assert.IsFalse(TransientNetworkError.IsTransient(unknown));
        BilibiliSecrets.AssertClean(unknown);

        // -400 is a content state for PGC playurl only.
        Assert.ThrowsException<BilibiliApiException>(() =>
            BilibiliArchiveRules.DecideView(new DataWrapper<Post> {Code = -400}, 1, 0));
        Assert.ThrowsException<BilibiliApiException>(() => Decide("view-101.json"));

        var risk = Assert.ThrowsException<BilibiliTemporarilyUnavailableException>(() => Decide("view-352-voucher.json"));
        Assert.IsTrue(TransientNetworkError.IsTransient(risk));

        Assert.ThrowsException<BilibiliProtocolException>(() =>
            BilibiliArchiveRules.DecideView(new DataWrapper<Post> {Code = 0, Data = null}, 1, 0));
    }

    [TestMethod]
    public void AfterNotFound()
    {
        var hidden = BilibiliArchiveRules.DecideAfterNotFound(BilibiliFixtures.Wrapper<List<PostPage>>("pagelist-ok.json"));
        Assert.AreEqual(BilibiliSkipReason.RegionRestrictedOrHidden, hidden.Reason);
        Assert.AreEqual(-404, hidden.Code);

        var gone = BilibiliArchiveRules.DecideAfterNotFound(BilibiliFixtures.Wrapper<List<PostPage>>("pagelist-404.json"));
        Assert.AreEqual(BilibiliSkipReason.Deleted, gone.Reason);
        Assert.AreEqual(-404, gone.Code);

        Assert.ThrowsException<BilibiliTemporarilyUnavailableException>(() =>
            BilibiliArchiveRules.DecideAfterNotFound(new DataWrapper<List<PostPage>> {Code = -352}));
        Assert.ThrowsException<BilibiliApiException>(() =>
            BilibiliArchiveRules.DecideAfterNotFound(new DataWrapper<List<PostPage>> {Code = -12345}));
    }

    [TestMethod]
    public void PageListFallback()
    {
        var (pages, skip) =
            BilibiliArchiveRules.DecidePageListFallback(BilibiliFixtures.Wrapper<List<PostPage>>("pagelist-ok.json"));
        Assert.IsNull(skip);
        CollectionAssert.AreEqual(new long[] {5001, 5002}, pages!.Select(p => p.Cid).ToArray());

        var (none, denied) =
            BilibiliArchiveRules.DecidePageListFallback(BilibiliFixtures.Wrapper<List<PostPage>>("pagelist-404.json"));
        Assert.IsNull(none);
        Assert.AreEqual(BilibiliSkipReason.AccessDenied, denied!.Reason);
        Assert.AreEqual(-403, denied.Code);
    }

    [DataTestMethod]
    [DataRow("view-upower-preview.json", BilibiliSkipReason.SupporterOnlyPreview)]
    [DataRow("view-upower-nopreview.json", BilibiliSkipReason.SupporterOnly)]
    [DataRow("view-upower-free.json", null, DisplayName = "限时免费 (exclusive, playable, pay=1) is not gated")]
    [DataRow("view-ok-2pages.json", null)]
    public void GateAccess(string fixture, BilibiliSkipReason? expected)
    {
        Assert.AreEqual(expected, BilibiliArchiveRules.GateAccess(BilibiliFixtures.Data<Post>(fixture))?.Reason);
    }

    [TestMethod]
    public void PgcRedirects()
    {
        var redirect = BilibiliFixtures.Data<Post>("view-pgc-redirect.json").RedirectUrl;
        Assert.IsTrue(BilibiliArchiveRules.IsPgcRedirect(redirect));
        Assert.AreEqual(321808L, BilibiliArchiveRules.TryGetEpisodeId(redirect));
        Assert.IsFalse(BilibiliArchiveRules.IsPgcRedirect(null));
        Assert.IsFalse(BilibiliArchiveRules.IsPgcRedirect("https://www.bilibili.com/video/BV1xx411c7m1"));
        Assert.IsNull(BilibiliArchiveRules.TryGetEpisodeId(null));
        Assert.IsNull(BilibiliArchiveRules.TryGetEpisodeId("https://www.bilibili.com/bangumi/play/ss33333"));
    }
}
