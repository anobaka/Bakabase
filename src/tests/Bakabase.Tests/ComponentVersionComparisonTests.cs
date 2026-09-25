using Bakabase.InsideWorld.Business.Components.Dependency;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations;

namespace Bakabase.Tests;

[TestClass]
public sealed class ComponentVersionComparisonTests
{
    [TestMethod]
    [DataRow(null, "7.1", true, DisplayName = "nothing installed")]
    [DataRow("", "24.08", true, DisplayName = "nothing installed (empty)")]
    [DataRow("7.1.1", "6.1", false, DisplayName = "installed is newer")]
    [DataRow("6.0", "6.1", true)]
    [DataRow("6.1-static", "6.1", false, DisplayName = "build suffix is not an older prerelease")]
    [DataRow("6.1-static", "7.0", true)]
    [DataRow("7.1.1-tessus", "7.1.1", false)]
    [DataRow("2021-01-31-git-6c92557756-full_build-www.gyan.dev", "6.1", false,
        DisplayName = "unparsable installed version is not nagged about")]
    [DataRow("6.0-essentials_build-www.gyan.dev", "6.1", true, DisplayName = "gyan release build, older")]
    [DataRow("7.1-full_build-www.gyan.dev", "6.1", false, DisplayName = "gyan release build, newer")]
    [DataRow("n7.1.1", "6.1", false, DisplayName = "n-prefixed release tag")]
    [DataRow("n6.0", "6.1", true)]
    [DataRow("N-112345-g0123456789-20240101", "6.1", false, DisplayName = "nightly build is not nagged about")]
    [DataRow("2.5.0.1", "2.5.0.2", false, DisplayName = "fourth segment is truncated, as the installer does")]
    [DataRow("2.5.0.1", "2.6.0.0", true)]
    [DataRow("24.08", "24.08", false)]
    [DataRow("24.08", "24.09", true)]
    [DataRow("v24.08", "24.09", true)]
    [DataRow("1.0.0", null, false, DisplayName = "unknown latest")]
    [DataRow("1.0.0", "N/A", false, DisplayName = "no latest for this platform")]
    [DataRow(null, "N/A", false)]
    public void IsUpdateAvailable(string? installed, string? latest, bool expected)
    {
        Assert.AreEqual(expected, ComponentVersionComparison.IsUpdateAvailable(installed, latest));
    }

    [TestMethod]
    [DataRow("2.5.0.1", "2.5.0")]
    [DataRow("6.1-static", "6.1.0-static")]
    [DataRow("24.08", "24.8.0")]
    [DataRow("6.0-essentials_build-www.gyan.dev", "6.0.0")]
    [DataRow("n7.1.1", "7.1.1")]
    public void TryParse_IsTolerant(string version, string expected)
    {
        Assert.AreEqual(expected, ComponentVersionComparison.TryParse(version)?.ToString());
    }

    [TestMethod]
    [DataRow((string?)null)]
    [DataRow("")]
    [DataRow("N/A")]
    [DataRow("2021-01-31-git-6c92557756-full_build-www.gyan.dev")]
    [DataRow("N-112345-g0123456789-20240101")]
    public void TryParse_ReturnsNullForUnparsable(string? version)
    {
        Assert.IsNull(ComponentVersionComparison.TryParse(version));
    }

    /// <summary>The installer and the prompt agree whenever the installed version parses.</summary>
    [TestMethod]
    [DataRow("6.1-static", "6.1", false, DisplayName = "static build of the latest version: no download")]
    [DataRow("7.1.1-tessus", "7.1.1", false)]
    [DataRow("6.0", "6.1", true)]
    [DataRow("24.08", "24.08", false)]
    [DataRow(null, "24.08", true, DisplayName = "nothing installed")]
    [DataRow("2021-01-31-git-6c92557756-full_build-www.gyan.dev", "6.1", true,
        DisplayName = "an explicit install over an unparsable copy still proceeds")]
    public void Installer_DownloadsOnlyWhenTheUpdateWouldBeOffered(string? installed, string latest,
        bool expected)
    {
        Assert.AreEqual(expected, HttpSourceComponentService.ShouldDownload(installed, latest));
    }
}
