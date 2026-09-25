using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliPlayUrlRulesTests
{
    private static BilibiliPlayUrlOutcome Decide(string fixture, bool pgc = false) =>
        BilibiliPlayUrlRules.Decide(BilibiliFixtures.Wrapper<VideoSource>(fixture), pgc);

    [DataTestMethod]
    [DataRow("playurl-dash.json", BilibiliPlayUrlOutcomeKind.Dash)]
    [DataRow("playurl-dash-video-only.json", BilibiliPlayUrlOutcomeKind.Dash)]
    [DataRow("playurl-dash-unknown-ids.json", BilibiliPlayUrlOutcomeKind.Dash)]
    [DataRow("playurl-durl-mp4.json", BilibiliPlayUrlOutcomeKind.Durl)]
    [DataRow("playurl-durl-flv.json", BilibiliPlayUrlOutcomeKind.Durl)]
    [DataRow("playurl-durl-multi.json", BilibiliPlayUrlOutcomeKind.Durl)]
    [DataRow("playurl-durl-flv-19seg.json", BilibiliPlayUrlOutcomeKind.Durl)]
    [DataRow("playurl-durl-old-drift.json", BilibiliPlayUrlOutcomeKind.Durl, DisplayName = "old transcode 0.06% short is not a preview")]
    [DataRow("playurl-code0-nostreams.json", BilibiliPlayUrlOutcomeKind.NoStreams)]
    public void StreamKinds(string fixture, BilibiliPlayUrlOutcomeKind kind)
    {
        Assert.AreEqual(kind, Decide(fixture).Kind);
    }

    [DataTestMethod]
    [DataRow("playurl-preview-upower.json", false, BilibiliSkipReason.PreviewOnly, null)]
    [DataRow("playurl-preview-short.json", false, BilibiliSkipReason.PreviewOnly, null)]
    [DataRow("playurl-preview-short.json", true, BilibiliSkipReason.PgcEpisodeNotSupported, null)]
    [DataRow("playurl-87008.json", false, BilibiliSkipReason.SupporterOnly, 87008)]
    [DataRow("playurl-404.json", false, BilibiliSkipReason.Unavailable, -404)]
    [DataRow("playurl-404.json", true, BilibiliSkipReason.PgcEpisodeNotSupported, -404)]
    [DataRow("playurl-400.json", true, BilibiliSkipReason.PgcEpisodeNotSupported, -400)]
    [DataRow("playurl-10403-region.json", false, BilibiliSkipReason.RegionRestrictedOrHidden, -10403)]
    [DataRow("playurl-10403-member.json", false, BilibiliSkipReason.PgcMemberOrPaid, -10403)]
    [DataRow("playurl-10403-member.json", true, BilibiliSkipReason.PgcMemberOrPaid, -10403)]
    public void Skips(string fixture, bool pgc, BilibiliSkipReason reason, int? code)
    {
        var o = Decide(fixture, pgc);
        Assert.AreEqual(BilibiliPlayUrlOutcomeKind.Skip, o.Kind);
        Assert.AreEqual(reason, o.Skip!.Reason);
        Assert.AreEqual(code, o.Skip.Code);
    }

    [TestMethod]
    public void CodesThatAreNotAContentStateOfThePageThrow()
    {
        var badRequest = Assert.ThrowsException<BilibiliApiException>(() => Decide("playurl-400.json"));
        Assert.IsFalse(TransientNetworkError.IsTransient(badRequest));
        Assert.ThrowsException<BilibiliApiException>(() => Decide("playurl-unknown-code.json"));
        Assert.ThrowsException<BilibiliApiException>(() => BilibiliPlayUrlRules.DecideError(-403, "x", false));
        var risk = Assert.ThrowsException<BilibiliTemporarilyUnavailableException>(() => Decide("playurl-352.json"));
        Assert.IsTrue(TransientNetworkError.IsTransient(risk));

        var naming = Assert.ThrowsException<BilibiliApiException>(() =>
            BilibiliPlayUrlRules.DecideError(-12345, "x", false, "playurl-naming"));
        StringAssert.Contains(naming.Message, "playurl-naming");
    }

    [TestMethod]
    public void ArchiveStateCodesOnPlayUrlSkip()
    {
        Assert.AreEqual(BilibiliSkipReason.Deleted, BilibiliPlayUrlRules.DecideError(62002, null, false).Reason);
        Assert.AreEqual(BilibiliSkipReason.PrivateToUploader, BilibiliPlayUrlRules.DecideError(62012, null, false).Reason);
        Assert.AreEqual(BilibiliSkipReason.UnderReview, BilibiliPlayUrlRules.DecideError(62004, null, false).Reason);
    }

    [TestMethod]
    public void MissingDataIsNoStreams()
    {
        Assert.AreEqual(BilibiliPlayUrlOutcomeKind.NoStreams,
            BilibiliPlayUrlRules.Decide(new DataWrapper<VideoSource> {Code = 0}, false).Kind);
    }

    [TestMethod]
    public void ASegmentWithoutUrlIsNotPlayable()
    {
        var data = BilibiliFixtures.Data<VideoSource>("playurl-durl-multi.json");
        data.Durl![1].Url = null;
        data.Durl[1].BackupUrl = null;
        Assert.AreEqual(BilibiliPlayUrlOutcomeKind.NoStreams,
            BilibiliPlayUrlRules.Decide(new DataWrapper<VideoSource> {Code = 0, Data = data}, false).Kind);
    }

    private static VideoSource Durl(long timelength, params long[] lengths) => new()
    {
        Timelength = timelength,
        Durl = lengths.Select((l, i) => new VideoSource.TDurl {Order = i + 1, Length = l, Url = "https://upos-sz-example.bilivideo.com/a/1.mp4"}).ToList(),
    };

    [TestMethod]
    public void PreviewTest()
    {
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(Durl(80384, 80384)), "equal");
        Assert.IsTrue(BilibiliPlayUrlRules.IsPreview(Durl(5062, 4851)), "211 ms gap on a 5 s clip");
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(Durl(80384, 80335)), "49 ms gap");
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(Durl(6839289, 6835185)), "0.06% drift of an old transcode");
        Assert.IsTrue(BilibiliPlayUrlRules.IsPreview(Durl(100000, 95800)), "worst real preview ratio 0.958");
        Assert.IsTrue(BilibiliPlayUrlRules.IsPreview(Durl(623642, 180322)));
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(Durl(0, 1000)), "timelength 0");
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(Durl(100000)), "no durl");
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(Durl(120000, 30000, 40000, 50000)), "multi-segment sum");

        var withDash = Durl(623642, 180322);
        withDash.Dash = BilibiliFixtures.Data<VideoSource>("playurl-dash.json").Dash;
        Assert.IsFalse(BilibiliPlayUrlRules.IsPreview(withDash), "DASH present");
    }
}
