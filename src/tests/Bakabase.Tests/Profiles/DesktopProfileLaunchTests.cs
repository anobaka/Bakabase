using System;
using System.IO;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Profiles;

[TestClass]
public class DesktopProfileLaunchTests
{
    [TestMethod]
    public void Profile_arguments_accept_only_one_registered_id_never_a_path_or_uri()
    {
        Assert.IsNull(DesktopProfileSelection.ParseId([]));
        Assert.AreEqual("default", DesktopProfileSelection.ParseId(["--profile=default"]));
        var id = Guid.NewGuid().ToString("N");
        Assert.AreEqual(id, DesktopProfileSelection.ParseId(["--profile=" + id]));
        foreach (var invalid in new[] { "../other", "/data", "bakabase://tools/file-processor", "", "--bakabase-role=worker" })
            Assert.ThrowsException<ArgumentException>(() => DesktopProfileSelection.ParseId(["--profile=" + invalid]));
        Assert.ThrowsException<ArgumentException>(() => DesktopProfileSelection.ParseId(["--profile=default", "--profile=" + id]));
    }

    [TestMethod]
    public void Explicit_profile_anchor_is_not_the_environment_override()
    {
        var anchor = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "test-profile-anchor"));
        var profile = AppDataPathProfile.AllInOne with { AnchorDirectory = anchor };
        var resolved = DefaultAppDataPathResolver.Resolve(profile, System.Runtime.InteropServices.OSPlatform.Linux,
            _ => "/incorrect-environment", _ => "/unused", "Bakabase");
        Assert.AreEqual(anchor, resolved);
        Assert.IsFalse(profile.IsAllInOne, "A new data space must not import a historical default layout.");
    }

    [TestMethod]
    public void Opening_a_profile_does_not_inherit_environment_or_internal_setup_credentials()
    {
        var info = DesktopProfileSelection.CreateStartInfo("default");
        Assert.IsTrue(info.ArgumentList.Contains("--profile=default"));
        Assert.IsFalse(info.UseShellExecute);
        Assert.IsFalse(info.Environment.ContainsKey("BAKABASE_DATA_DIR"));
        Assert.IsFalse(info.Environment.ContainsKey("BAKABASE_SETUP_PIPE"));
        Assert.IsFalse(info.Environment.ContainsKey("BAKABASE_SETUP_SECRET"));
    }

    [TestMethod]
    public void External_protocol_links_cannot_choose_a_profile_or_arbitrary_path()
    {
        Assert.IsFalse(DesktopToolLink.TryNormalizeArguments(["--desktop-tool", DesktopToolLink.FileProcessor, "--profile=default"], out _));
        Assert.IsFalse(DesktopToolLink.TryNormalizeArguments(["--desktop-tool", "bakabase://tools/file-processor?profile=default"], out _));
    }

    [TestMethod]
    public void Upgrade_tracks_coordinators_of_one_installation_and_never_forces_another_instance_to_exit()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-installation-instances-" + Guid.NewGuid().ToString("N"));
        try
        {
            var installation = new DesktopInstallationInstances(root);
            using var first = installation.Register(101);
            installation.AssertOnlyCoordinator(101);
            using (installation.Register(202))
                Assert.ThrowsException<InvalidOperationException>(() => installation.AssertOnlyCoordinator(101));
            installation.AssertOnlyCoordinator(101); // stale lease files do not look running
            var anotherInstallation = new DesktopInstallationInstances(Path.Combine(root, "other"));
            using var unrelated = anotherInstallation.Register(303);
            installation.AssertOnlyCoordinator(101);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
