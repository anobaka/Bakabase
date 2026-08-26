using Bakabase.Modules.ThirdParty.ThirdParties.Av;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// The cases below are the ones from docs/file-processor-av-grouping-design.md, so the defects that
/// document identified stay fixed.
/// </summary>
[TestClass]
public class AvProductCodeParserTests
{
    [DataTestMethod]
    // The shipped grouping preset handled only this one.
    [DataRow("XDVD-101", "XDVD-101")]
    // Missing separator — the preset required a literal hyphen.
    [DataRow("XDVD101", "XDVD-101")]
    // Mixed case — the preset used [A-Z] and matched neither of these.
    [DataRow("xdvd-101", "XDVD-101")]
    [DataRow("Xdvd-101", "XDVD-101")]
    // Other separators, which neither the preset nor the Freejavbt-style regex accepted.
    [DataRow("XDVD_103", "XDVD-103")]
    [DataRow("XDVD 104", "XDVD-104")]
    // Junk around the code.
    [DataRow("sssssssXDVD-101pl", "XDVD-101")]
    [DataRow("[hd]XDVD-102 [1080p]", "XDVD-102")]
    [DataRow("(2024)XDVD-105", "XDVD-105")]
    [DataRow("[JAV]ABP-123 [FHD].mp4", "ABP-123")]
    [DataRow("abp123.mkv", "ABP-123")]
    public void ParsesCode(string input, string expected)
    {
        Assert.AreEqual(expected, AvProductCodeParser.ParseNormalized(input));
    }

    [TestMethod]
    public void LeadingZerosArePreserved()
    {
        // XDVD-001 and XDVD-1 are different works, so the serial is never re-numbered.
        Assert.AreEqual("XDVD-001", AvProductCodeParser.ParseNormalized("XDVD-001"));
    }

    [TestMethod]
    public void UppercaseJunkYieldsNoCodeRatherThanAWrongOne()
    {
        // The shipped preset returned SSXDVD-101 here — a key that matches nothing else and
        // silently creates a wrong directory. With no case boundary and an 11-letter run there is
        // nothing in the string marking where the junk ends, so reporting nothing is the honest
        // answer and leaves the file visibly ungrouped.
        Assert.IsNull(AvProductCodeParser.ParseNormalized("SSSSSSSXDVD-101pl"));
    }

    [DataTestMethod]
    [DataRow("holiday photos")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow(null)]
    // A single-digit serial is below the minimum, matching the conventions already used elsewhere.
    [DataRow("XDVD-1")]
    // One leading letter is not a label.
    [DataRow("h264")]
    public void ReturnsNullWhenThereIsNoCode(string? input)
    {
        Assert.IsNull(AvProductCodeParser.ParseNormalized(input));
    }

    [TestMethod]
    public void LabelAndSerialAreExposedSeparately()
    {
        var parsed = AvProductCodeParser.Parse("xdvd_0099");

        Assert.IsNotNull(parsed);
        Assert.AreEqual("XDVD", parsed!.Label);
        Assert.AreEqual("0099", parsed.Serial);
    }

    [TestMethod]
    public void VariantsOfTheSameWorkShareOneKey()
    {
        // The whole point: these all land in a single XDVD-101 group.
        var variants = new[] {"XDVD-101", "XDVD101", "xdvd-101", "XDVD_101", "sssssssXDVD-101pl"};

        foreach (var v in variants)
        {
            Assert.AreEqual("XDVD-101", AvProductCodeParser.ParseNormalized(v), $"failed for {v}");
        }
    }
}
