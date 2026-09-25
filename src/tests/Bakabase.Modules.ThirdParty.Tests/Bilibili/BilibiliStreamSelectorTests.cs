using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili;

[TestClass]
public class BilibiliStreamSelectorTests
{
    private const long Cid = 5001;

    private static VideoSource.TDash Dash(string fixture) => BilibiliFixtures.Data<VideoSource>(fixture).Dash!;

    [TestMethod]
    public void BestIdThenAvcAndTheBestAacRegardlessOfOrder()
    {
        var s = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash.json"))!;
        Assert.AreEqual(80, s.Video.Id);
        Assert.AreEqual(7, s.Video.CodecId);
        Assert.AreEqual("5001-v80-c7", s.Video.IdentityKey);
        StringAssert.StartsWith(s.Video.Urls[0], "https://upos-sz-example.bilivideo.com/", "PCDN base URL ranked last");
        StringAssert.StartsWith(s.Video.Urls[^1], "https://xy0x0x0x0xy.mcdn.bilivideo.cn:8082/");

        // dolby {type:2, audio:null} and flac {display:true, audio:null} fall through to dash.audio.
        Assert.AreEqual(BilibiliAudioKind.Aac, s.AudioKind);
        Assert.AreEqual(30280, s.Audio!.Id);
        Assert.AreEqual("5001-a30280", s.Audio.IdentityKey);
        Assert.IsNull(s.AacFallbackAudio);
    }

    [TestMethod]
    public void AudioOrderInTheAnswerDoesNotMatter()
    {
        var dash = Dash("playurl-dash.json");
        dash.Audio!.Reverse();
        Assert.AreEqual(30280, BilibiliStreamSelector.SelectDash(Cid, dash)!.Audio!.Id);
    }

    [TestMethod]
    public void CodecPreferenceIsASeam()
    {
        var s = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash.json"), [12])!;
        Assert.AreEqual(12, s.Video.CodecId);
        Assert.IsTrue(BilibiliStreamSelector.NeedsHvc1Tag(s.Video));
    }

    [TestMethod]
    public void VideoFallbackIsAvcAtTheSameIdThenTheNextLowerIdAndNeverDolbyVision()
    {
        var dash = Dash("playurl-dash.json");
        var hevc80 = BilibiliStreamSelector.SelectDash(Cid, dash, [12])!.Video;
        var tried = new HashSet<(int, int)> {(hevc80.Id, hevc80.CodecId)};
        var avc80 = BilibiliStreamSelector.SelectVideoFallback(Cid, dash, hevc80, tried)!;
        Assert.AreEqual((80, 7), (avc80.Id, avc80.CodecId));

        tried.Add((80, 7));
        var next = BilibiliStreamSelector.SelectVideoFallback(Cid, dash, avc80, tried)!;
        Assert.AreEqual((64, 7), (next.Id, next.CodecId));

        // 8K: 127 HEVC → 125 HEVC → 120 AVC; Dolby Vision (126) ranks below everything.
        var eightK = Dash("playurl-dash-8k.json");
        var chain = new List<int>();
        var failed = BilibiliStreamSelector.SelectDash(Cid, eightK)!.Video;
        var seen = new HashSet<(int, int)> {(failed.Id, failed.CodecId)};
        for (var i = 0; i < 2 && BilibiliStreamSelector.SelectVideoFallback(Cid, eightK, failed, seen) is { } f; i++)
        {
            chain.Add(f.Id);
            seen.Add((f.Id, f.CodecId));
            failed = f;
        }

        CollectionAssert.AreEqual(new[] {125, 120}, chain);
    }

    [TestMethod]
    public void FlacThenDolbyWithAnAacFallback()
    {
        var dash = Dash("playurl-dash-hires.json");
        var flac = BilibiliStreamSelector.SelectDash(Cid, dash)!;
        Assert.AreEqual(BilibiliAudioKind.Flac, flac.AudioKind);
        Assert.AreEqual(30251, flac.Audio!.Id);
        Assert.AreEqual(30280, flac.AacFallbackAudio!.Id);

        dash.Flac!.Audio = null;
        var dolby = BilibiliStreamSelector.SelectDash(Cid, dash)!;
        Assert.AreEqual(BilibiliAudioKind.DolbyEac3, dolby.AudioKind);
        Assert.AreEqual(30250, dolby.Audio!.Id);
        Assert.AreEqual(30280, dolby.AacFallbackAudio!.Id);
    }

    [TestMethod]
    public void HevcWhenNoAvcAtTheBestId()
    {
        var s = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash-8k.json"))!;
        Assert.AreEqual(127, s.Video.Id);
        Assert.AreEqual(12, s.Video.CodecId);
    }

    [TestMethod]
    public void DolbyVisionOnlyWhenNothingElseIsOffered()
    {
        var dash = Dash("playurl-dash-8k.json");
        dash.Video!.RemoveAll(v => v.Id == 127);
        var s = BilibiliStreamSelector.SelectDash(Cid, dash)!;
        Assert.AreEqual(125, s.Video.Id, "HDR10 keeps its rank; DV (126) is demoted");

        dash.Video.RemoveAll(v => v.Id != 126 && v.Id != 120);
        Assert.AreEqual(120, BilibiliStreamSelector.SelectDash(Cid, dash)!.Video.Id);

        var dvOnly = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash-dv-only.json"))!;
        Assert.AreEqual(126, dvOnly.Video.Id);
        Assert.IsFalse(BilibiliStreamSelector.NeedsHvc1Tag(dvOnly.Video), "dvh1 keeps its tag");
    }

    [TestMethod]
    public void UnknownIdsAndCodecsNeverThrow()
    {
        var s = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash-unknown-ids.json"))!;
        Assert.AreEqual(999, s.Video.Id);
        Assert.AreEqual(99, s.Video.CodecId);
        Assert.AreEqual(30999, s.Audio!.Id);
    }

    [TestMethod]
    public void VideoOnly()
    {
        var s = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash-video-only.json"))!;
        Assert.AreEqual(BilibiliAudioKind.None, s.AudioKind);
        Assert.IsNull(s.Audio);
        Assert.IsNull(s.AacFallbackAudio);
    }

    [TestMethod]
    public void NothingUsable()
    {
        Assert.IsNull(BilibiliStreamSelector.SelectDash(Cid, new VideoSource.TDash()));
        Assert.IsNull(BilibiliStreamSelector.SelectDash(Cid, new VideoSource.TDash
        {
            Video = [new VideoSource.TDash.TFragement {Id = 80, CodecId = 7, BaseUrl = null}],
        }));
    }

    [TestMethod]
    public void FindSameAfterARefresh()
    {
        var first = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash.json"))!;
        var refreshed = Dash("playurl-dash.json");
        Assert.AreEqual(first.Video.IdentityKey,
            BilibiliStreamSelector.FindSame(Cid, refreshed, first.Video, false)!.IdentityKey);
        Assert.AreEqual(first.Audio!.IdentityKey,
            BilibiliStreamSelector.FindSame(Cid, refreshed, first.Audio, true)!.IdentityKey);

        refreshed.Video!.RemoveAll(v => v.Id == 80 && v.CodecId == 7);
        Assert.IsNull(BilibiliStreamSelector.FindSame(Cid, refreshed, first.Video, false));

        var hires = BilibiliStreamSelector.SelectDash(Cid, Dash("playurl-dash-hires.json"))!;
        Assert.IsNotNull(BilibiliStreamSelector.FindSame(Cid, Dash("playurl-dash-hires.json"), hires.Audio!, true));
    }

    [TestMethod]
    public void DurlSegmentsInOrderWithExtensions()
    {
        var multi = BilibiliStreamSelector.SelectDurl(Cid, BilibiliFixtures.Data<VideoSource>("playurl-durl-multi.json").Durl);
        CollectionAssert.AreEqual(new[] {1, 2, 3}, multi.Select(s => s.Order).ToArray());
        Assert.IsTrue(multi.All(s => s.Extension == ".flv"));
        Assert.AreEqual("5001-d1-7001-1-64.flv", multi[0].IdentityKey);

        var mp4 = BilibiliStreamSelector.SelectDurl(Cid, BilibiliFixtures.Data<VideoSource>("playurl-durl-mp4.json").Durl);
        Assert.AreEqual(".mp4", mp4.Single().Extension);
        Assert.AreEqual(80384, mp4[0].LengthMs);
        Assert.AreEqual("5001-d1-25540578-1-16.mp4", mp4[0].IdentityKey);
        Assert.AreEqual("25540578-1-16.mp4", mp4[0].FileName);

        var nineteen = BilibiliStreamSelector.SelectDurl(Cid,
            BilibiliFixtures.Data<VideoSource>("playurl-durl-flv-19seg.json").Durl);
        CollectionAssert.AreEqual(Enumerable.Range(1, 19).ToArray(), nineteen.Select(s => s.Order).ToArray());

        Assert.AreEqual(0, BilibiliStreamSelector.SelectDurl(Cid, null).Count);
    }

    [TestMethod]
    public void DurlFindSameMatchesTheFullIdentity()
    {
        var segment = BilibiliStreamSelector.SelectDurl(Cid,
            BilibiliFixtures.Data<VideoSource>("playurl-durl-mp4.json").Durl)[0];
        Assert.IsNotNull(BilibiliStreamSelector.FindSame(Cid,
            BilibiliFixtures.Data<VideoSource>("playurl-durl-mp4.json").Durl, segment));
        // A refreshed answer that turned into a preview (order 1 too) is not the same segment.
        Assert.IsNull(BilibiliStreamSelector.FindSame(Cid,
            BilibiliFixtures.Data<VideoSource>("playurl-preview-short.json").Durl, segment));
    }

    private static BilibiliStreamCandidate Video(int id, int codecId, string? codecs) =>
        new(id, codecId, 1, codecs, ["https://upos-sz-example.bilivideo.com/a.m4s"], "k");

    [TestMethod]
    public void Hvc1Tagging()
    {
        Assert.IsTrue(BilibiliStreamSelector.NeedsHvc1Tag(Video(80, 12, "hev1.1.6.L120.90")));
        Assert.IsFalse(BilibiliStreamSelector.NeedsHvc1Tag(Video(80, 12, "hvc1.1.6.L120.90")));
        Assert.IsFalse(BilibiliStreamSelector.NeedsHvc1Tag(Video(126, 12, "dvh1.05.07")));
        Assert.IsFalse(BilibiliStreamSelector.NeedsHvc1Tag(Video(126, 12, null)));
        Assert.IsTrue(BilibiliStreamSelector.NeedsHvc1Tag(Video(80, 12, null)));
        Assert.IsFalse(BilibiliStreamSelector.NeedsHvc1Tag(Video(80, 7, "avc1.640032")));
    }
}
