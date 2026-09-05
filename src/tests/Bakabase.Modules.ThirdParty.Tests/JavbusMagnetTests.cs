using Bakabase.Modules.ThirdParty.ThirdParties.Javbus;
using Bakabase.Modules.ThirdParty.ThirdParties.Javbus.Models;

namespace Bakabase.Modules.ThirdParty.Tests
{
    /// <summary>
    /// Offline coverage for the two pure pieces of the Javbus batch tool: the
    /// magnet-table parser and the pick-one-magnet policy.
    /// </summary>
    [TestClass]
    public class JavbusMagnetTests
    {
        [DataTestMethod]
        [DataRow("4.35GB", 4670776934L)]
        [DataRow("986MB", 1033895936L)]
        [DataRow("1.5 GB", 1610612736L)]
        [DataRow("700KB", 716800L)]
        [DataRow("", 0L)]
        [DataRow("unknown", 0L)]
        public void ParseSize_ReadsWhatTheSitePrints(string text, long expected)
        {
            Assert.AreEqual(expected, JavbusMagnetSelector.ParseSize(text));
        }

        [DataTestMethod]
        [DataRow("SSIS-001 中文字幕", JavbusMagnetTag.SubtitleKeyword)]
        [DataRow("SSIS-001-C", JavbusMagnetTag.SubtitleSuffix)]
        [DataRow("SSIS-001-UC", JavbusMagnetTag.SubtitleSuffix)]
        [DataRow("SSIS-001 高清", JavbusMagnetTag.Chinese)]
        [DataRow("SSIS-001-1080p", JavbusMagnetTag.Plain)]
        // -CHN / -COMPLETE start like the subtitle marker but aren't one.
        [DataRow("SSIS-001-CHN", JavbusMagnetTag.Plain)]
        [DataRow("SSIS-001-COMPLETE", JavbusMagnetTag.Plain)]
        public void DetectTag_ScoresSubtitleHints(string name, JavbusMagnetTag expected)
        {
            Assert.AreEqual(expected, JavbusMagnetSelector.DetectTag(name));
        }

        [TestMethod]
        public void Select_PrefersTheLargestTier_OverASubtitledButSmallerRelease()
        {
            var picked = JavbusMagnetSelector.Select([
                Magnet("SSIS-001 中文字幕", 800_000_000),
                Magnet("SSIS-001", 6_000_000_000)
            ], 0.3m);

            Assert.AreEqual("SSIS-001", picked!.Name);
        }

        [TestMethod]
        public void Select_InsideTheSameTier_PrefersTheSubtitleHint()
        {
            var picked = JavbusMagnetSelector.Select([
                Magnet("SSIS-001", 6_000_000_000),
                Magnet("SSIS-001 中文字幕", 5_000_000_000),
                Magnet("SSIS-001-C", 5_500_000_000)
            ], 0.3m);

            Assert.AreEqual("SSIS-001 中文字幕", picked!.Name);
        }

        [TestMethod]
        public void Select_WithZeroTolerance_OnlyTheLargestQualifies()
        {
            var picked = JavbusMagnetSelector.Select([
                Magnet("SSIS-001 中文字幕", 5_999_999_999),
                Magnet("SSIS-001", 6_000_000_000)
            ], 0m);

            Assert.AreEqual("SSIS-001", picked!.Name);
        }

        [TestMethod]
        public void Select_WhenNoSizeParsed_StillPicksOnTags()
        {
            var picked = JavbusMagnetSelector.Select([
                Magnet("SSIS-001", 0),
                Magnet("SSIS-001 中文字幕", 0)
            ], 0.3m);

            Assert.AreEqual("SSIS-001 中文字幕", picked!.Name);
        }

        [TestMethod]
        public void Select_WithNoCandidates_ReturnsNull()
        {
            Assert.IsNull(JavbusMagnetSelector.Select([], 0.3m));
        }

        [TestMethod]
        public void ParseMagnets_ReadsTheBareRowsTheAjaxEndpointReturns()
        {
            // Shape of the real response: no <table>, one <a> per cell, and a
            // trailing 字幕 badge that is not a magnet link.
            const string html = """
                                <tr>
                                  <td>
                                    <a href="magnet:?xt=urn:btih:AAA" title="t">  SSIS-001-C   </a>
                                    <a class="btn btn-mini-new btn-warning disabled">字幕</a>
                                  </td>
                                  <td><a href="magnet:?xt=urn:btih:AAA">5.32GB</a></td>
                                  <td><a href="magnet:?xt=urn:btih:AAA">2024-01-02</a></td>
                                </tr>
                                <tr>
                                  <td><a href="magnet:?xt=urn:btih:BBB">SSIS-001</a></td>
                                  <td><a href="magnet:?xt=urn:btih:BBB">1.20GB</a></td>
                                  <td><a href="magnet:?xt=urn:btih:BBB">2024-01-01</a></td>
                                </tr>
                                <tr><td>no magnet here</td></tr>
                                """;

            var magnets = JavbusClient.ParseMagnets(html);

            Assert.AreEqual(2, magnets.Count);
            Assert.AreEqual("SSIS-001-C", magnets[0].Name);
            Assert.AreEqual("5.32GB", magnets[0].Size);
            Assert.AreEqual("2024-01-02", magnets[0].Date);
            Assert.AreEqual("magnet:?xt=urn:btih:AAA", magnets[0].Link);
            Assert.AreEqual(JavbusMagnetTag.SubtitleSuffix, magnets[0].Tag);
            Assert.IsTrue(magnets[0].SizeInBytes > magnets[1].SizeInBytes);
        }

        [TestMethod]
        public void ParseMagnets_WithEmptyResponse_ReturnsNothing()
        {
            Assert.AreEqual(0, JavbusClient.ParseMagnets("").Count);
        }

        private static JavbusMagnet Magnet(string name, long sizeInBytes) => new()
        {
            Name = name,
            SizeInBytes = sizeInBytes,
            Link = $"magnet:?xt=urn:btih:{name}",
            Tag = JavbusMagnetSelector.DetectTag(name)
        };
    }
}
