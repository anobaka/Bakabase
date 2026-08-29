using Bakabase.Service.Components.Playback;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// A wrong delivery decision does not throw — it shows up as a black player or a
/// silent video on someone else's device, so the ladder is pinned down case by case.
/// </summary>
[TestClass]
public class VideoDeliveryPlannerTests
{
    [TestMethod]
    public void Mp4_WithH264AndAac_PlaysDirectly()
    {
        var plan = VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", "h264", "aac");

        Assert.AreEqual(VideoDeliveryMethod.DirectPlay, plan.Method);
    }

    [TestMethod]
    public void Webm_WithH264AndOpus_PlaysDirectly()
    {
        var plan = VideoDeliveryPlanner.Plan("/media/lib/clip.webm", "h264", "opus");

        Assert.AreEqual(VideoDeliveryMethod.DirectPlay, plan.Method);
    }

    [TestMethod]
    public void Mkv_WithH264_IsRemuxed_NotSentAsIs()
    {
        // The bug this replaces: an h264-in-MKV file was streamed raw while claiming
        // to be video/mp4, which browsers reject or mis-demux.
        var plan = VideoDeliveryPlanner.Plan("/media/lib/ep1.mkv", "h264", "aac");

        Assert.AreEqual(VideoDeliveryMethod.Remux, plan.Method);
        Assert.IsTrue(plan.CopyAudio, "AAC needs no re-encoding, only the container changes");
    }

    [TestMethod]
    public void Ac3Audio_ForcesRemux_EvenInAGoodContainer()
    {
        // Chrome and Firefox have no AC3 licence, so this is the classic
        // "video plays, no sound" case — previously undetected because only the
        // video codec was probed.
        var plan = VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", "h264", "ac3");

        Assert.AreEqual(VideoDeliveryMethod.Remux, plan.Method);
        Assert.IsFalse(plan.CopyAudio, "AC3 has to be re-encoded to AAC");
    }

    [TestMethod]
    public void Mkv_WithH264AndDts_IsRemuxed_WithAudioReEncoded()
    {
        var plan = VideoDeliveryPlanner.Plan("/media/lib/ep1.mkv", "h264", "dts");

        Assert.AreEqual(VideoDeliveryMethod.Remux, plan.Method);
        Assert.IsFalse(plan.CopyAudio);
    }

    [TestMethod]
    public void SilentVideo_DoesNotForceARemux()
    {
        // No audio stream at all: ffprobe prints nothing, which must not be mistaken
        // for an undecodable track.
        Assert.AreEqual(VideoDeliveryMethod.DirectPlay,
            VideoDeliveryPlanner.Plan("/media/lib/silent.mp4", "h264", null).Method);
        Assert.AreEqual(VideoDeliveryMethod.DirectPlay,
            VideoDeliveryPlanner.Plan("/media/lib/silent.mp4", "h264", "").Method);
    }

    [TestMethod]
    public void NonPassThroughVideoCodecs_AreTranscoded()
    {
        // HEVC and AV1 decode only where the hardware supports them. Until the client
        // says what it can do, sending them unchanged would trade a working transcode
        // for a black player.
        foreach (var codec in new[] {"hevc", "h265", "av1", "vp9", "mpeg4", "wmv3", "theora"})
        {
            Assert.AreEqual(VideoDeliveryMethod.Transcode,
                VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", codec, "aac").Method,
                $"expected '{codec}' to be transcoded");
        }
    }

    [TestMethod]
    public void UnknownVideoCodec_IsTranscoded()
    {
        Assert.AreEqual(VideoDeliveryMethod.Transcode,
            VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", null, "aac").Method);
        Assert.AreEqual(VideoDeliveryMethod.Transcode,
            VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", "  ", "aac").Method);
    }

    [TestMethod]
    public void CodecNames_AreMatchedCaseInsensitively_AndTrimmed()
    {
        // ffprobe output arrives with a trailing newline, and casing varies.
        var plan = VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", " H264 ", " AAC ");

        Assert.AreEqual(VideoDeliveryMethod.DirectPlay, plan.Method);
    }

    [TestMethod]
    public void H264Aliases_AreRecognised()
    {
        foreach (var alias in new[] {"h264", "avc1", "avc"})
        {
            Assert.AreEqual(VideoDeliveryMethod.DirectPlay,
                VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", alias, "aac").Method,
                $"expected '{alias}' to be treated as H.264");
        }
    }

    [TestMethod]
    public void AcceptsABareExtension_AsWellAsAFullPath()
    {
        Assert.AreEqual(VideoDeliveryMethod.DirectPlay,
            VideoDeliveryPlanner.Plan(".mp4", "h264", "aac").Method);
        Assert.AreEqual(VideoDeliveryMethod.Remux,
            VideoDeliveryPlanner.Plan(".mkv", "h264", "aac").Method);
    }

    [TestMethod]
    public void MissingFileName_FallsBackToRemux_RatherThanGuessingTheContainer()
    {
        var plan = VideoDeliveryPlanner.Plan(null, "h264", "aac");

        Assert.AreEqual(VideoDeliveryMethod.Remux, plan.Method);
    }

    [TestMethod]
    public void EveryPlan_CarriesAReason()
    {
        foreach (var plan in new[]
                 {
                     VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", "h264", "aac"),
                     VideoDeliveryPlanner.Plan("/media/lib/ep1.mkv", "h264", "aac"),
                     VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", "h264", "ac3"),
                     VideoDeliveryPlanner.Plan("/media/lib/ep1.mp4", "hevc", "aac")
                 })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(plan.Reason));
        }
    }
}
