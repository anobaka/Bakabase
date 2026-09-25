using System.Linq;
using Bakabase.Abstractions.Components.Media;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;

namespace Bakabase.Tests;

[TestClass]
public class FfMpegMergeArgumentsTests
{
    private static readonly string[] Common =
        ["-hide_banner", "-nostdin", "-loglevel", "error", "-nostats", "-progress", "pipe:1", "-y"];

    private static string[] With(params string[] rest) => [..Common, ..rest];

    [TestMethod]
    public void Mux_Plain()
    {
        CollectionAssert.AreEqual(
            With("-i", "/w/v.m4s", "-i", "/w/a.m4s", "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-f", "mp4",
                "/w/out.mp4.partial"),
            FfMpegMergeArguments.Mux(new MediaMuxRequest("/w/v.m4s", "/w/a.m4s", "/w/out.mp4"), "/w/out.mp4.partial")
                .ToArray());
    }

    [TestMethod]
    public void Mux_Hvc1()
    {
        CollectionAssert.AreEqual(
            With("-i", "v", "-i", "a", "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-tag:v", "hvc1", "-f", "mp4",
                "o"),
            FfMpegMergeArguments.Mux(new MediaMuxRequest("v", "a", "x") {TagHevcAsHvc1 = true}, "o").ToArray());
    }

    [TestMethod]
    public void Mux_Strict()
    {
        CollectionAssert.AreEqual(
            With("-i", "v", "-i", "a", "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-strict", "-2", "-f", "mp4",
                "o"),
            FfMpegMergeArguments.Mux(new MediaMuxRequest("v", "a", "x") {AllowExperimentalCodecs = true}, "o")
                .ToArray());
    }

    [TestMethod]
    public void Mux_Both()
    {
        CollectionAssert.AreEqual(
            With("-i", "v", "-i", "a", "-map", "0:v:0", "-map", "1:a:0", "-c", "copy", "-tag:v", "hvc1", "-strict",
                "-2", "-f", "mp4", "o"),
            FfMpegMergeArguments.Mux(
                new MediaMuxRequest("v", "a", "x") {TagHevcAsHvc1 = true, AllowExperimentalCodecs = true}, "o").ToArray());
    }

    [TestMethod]
    public void Mux_VideoOnly()
    {
        CollectionAssert.AreEqual(
            With("-i", "v", "-map", "0:v:0", "-c", "copy", "-tag:v", "hvc1", "-f", "mp4", "o"),
            FfMpegMergeArguments.Mux(
                new MediaMuxRequest("v", null, "x") {TagHevcAsHvc1 = true, AllowExperimentalCodecs = true}, "o")
                .ToArray());
    }

    [TestMethod]
    public void Remux()
    {
        CollectionAssert.AreEqual(
            With("-i", "seg-1.flv", "-map", "0:v?", "-map", "0:a?", "-c", "copy", "-f", "mp4", "o"),
            FfMpegMergeArguments.Remux("seg-1.flv", "o").ToArray());
    }

    [TestMethod]
    public void Concat()
    {
        CollectionAssert.AreEqual(
            With("-f", "concat", "-safe", "0", "-i", "list.txt", "-map", "0:v?", "-map", "0:a?", "-c", "copy", "-f",
                "mp4", "o"),
            FfMpegMergeArguments.Concat("list.txt", "o").ToArray());
    }

    [TestMethod]
    public void ConcatList_KeepsOrderAndEscapesQuotes()
    {
        Assert.AreEqual(
            "ffconcat version 1.0\nfile '/w/seg-1.flv'\nfile '/w/it'\\''s seg-2.flv'\nfile 'C:\\w\\seg 3.flv'\n",
            FfMpegMergeArguments.ConcatList(["/w/seg-1.flv", "/w/it's seg-2.flv", "C:\\w\\seg 3.flv"]));
    }
}
