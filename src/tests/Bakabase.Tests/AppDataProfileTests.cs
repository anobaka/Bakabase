using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Bakabase.Infrastructures.Components.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// The all-in-one app and the removed thin client had AppData profiles of their own, and an
/// old thin client's data is still where the app looks for pairings to import. These tests
/// hold the two apart, and hold the existing one exactly where it has always been.
/// </summary>
[TestClass]
public class AppDataProfileTests
{
    private static Func<string, string?> Env(Dictionary<string, string?> map) =>
        k => map.GetValueOrDefault(k);

    private static Func<Environment.SpecialFolder, string> Folder(
        Dictionary<Environment.SpecialFolder, string> map) =>
        f => map.TryGetValue(f, out var v) ? v : throw new KeyNotFoundException(f.ToString());

    private static readonly Dictionary<Environment.SpecialFolder, string> Folders = new()
    {
        [Environment.SpecialFolder.LocalApplicationData] = "/local",
        [Environment.SpecialFolder.UserProfile] = "/home/foo",
        [Environment.SpecialFolder.ApplicationData] = "/app"
    };

    private static string Resolve(AppDataPathProfile profile, OSPlatform platform,
        Dictionary<string, string?>? env = null) =>
        DefaultAppDataPathResolver.Resolve(profile, platform, Env(env ?? []), Folder(Folders), "Bakabase");

    // The anchor is process-wide and AppService resolves it during its static
    // constructor, so whether it has been read by the time this class runs depends on
    // what ran before. Reset on both sides rather than inherit that.
    [TestInitialize]
    public void Setup() => AppDataAnchor.ResetForTests();

    [TestCleanup]
    public void Cleanup() => AppDataAnchor.ResetForTests();

    [TestMethod]
    public void The_all_in_one_profile_repeats_todays_constants()
    {
        // If any of these three drifts, existing installs lose their data on upgrade.
        Assert.AreEqual(DefaultAppDataPathResolver.EnvVarName, AppDataPathProfile.AllInOne.EnvVarName);
        Assert.AreEqual(DefaultAppDataPathResolver.FolderName, AppDataPathProfile.AllInOne.FolderName);
        Assert.AreEqual(DefaultAppDataPathResolver.WindowsAppDataFolderName,
            AppDataPathProfile.AllInOne.WindowsAppDataFolderName);
    }

    [TestMethod]
    public void The_profileless_overload_still_answers_for_the_all_in_one()
    {
        // Most callers and every existing test go through the old signature. It has to
        // keep meaning what it meant.
        foreach (var platform in new[] {OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux})
        {
            var old = DefaultAppDataPathResolver.Resolve(platform, Env([]), Folder(Folders), "Bakabase");

            Assert.AreEqual(Resolve(AppDataPathProfile.AllInOne, platform), old, platform.ToString());
        }
    }

    [TestMethod]
    public void The_two_builds_never_share_a_directory()
    {
        foreach (var platform in new[] {OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux})
        {
            Assert.AreNotEqual(
                Resolve(AppDataPathProfile.AllInOne, platform),
                Resolve(AppDataPathProfile.Client, platform),
                $"the two profiles collide on {platform}");
        }
    }

    [TestMethod]
    public void Each_build_reads_its_own_environment_override()
    {
        // Pointing the all-in-one somewhere must not drag the client along with it, or
        // a user who relocated their library would find the client had moved too.
        var env = new Dictionary<string, string?>
        {
            ["BAKABASE_DATA_DIR"] = Path.GetFullPath("/custom/all-in-one"),
            ["BAKABASE_CLIENT_DATA_DIR"] = Path.GetFullPath("/custom/client")
        };

        Assert.AreEqual(Path.GetFullPath("/custom/all-in-one"),
            Resolve(AppDataPathProfile.AllInOne, OSPlatform.Linux, env));
        Assert.AreEqual(Path.GetFullPath("/custom/client"),
            Resolve(AppDataPathProfile.Client, OSPlatform.Linux, env));
    }

    [TestMethod]
    public void The_client_ignores_the_all_in_ones_override()
    {
        var env = new Dictionary<string, string?> {["BAKABASE_DATA_DIR"] = Path.GetFullPath("/custom/all-in-one")};

        Assert.AreEqual(Resolve(AppDataPathProfile.Client, OSPlatform.Linux),
            Resolve(AppDataPathProfile.Client, OSPlatform.Linux, env));
    }

    [TestMethod]
    public void The_anchor_defaults_to_the_all_in_one()
    {
        // The existing entry point never calls Use, so the default is what it gets.
        Assert.AreSame(AppDataPathProfile.AllInOne, AppDataAnchor.Current);
    }

    [TestMethod]
    public void The_anchor_can_be_set_before_anything_reads_it()
    {
        AppDataAnchor.Use(AppDataPathProfile.Client);

        Assert.AreSame(AppDataPathProfile.Client, AppDataAnchor.Current);
    }

    [TestMethod]
    public void Switching_the_anchor_after_it_is_read_fails_loudly()
    {
        // By the time anything has read it a directory exists and a log file is open
        // under the old profile, so silently accepting the change would leave the two
        // halves of startup disagreeing about where the data lives.
        _ = AppDataAnchor.Current;

        Assert.ThrowsException<InvalidOperationException>(() =>
            AppDataAnchor.Use(AppDataPathProfile.Client));
    }

    [TestMethod]
    public void Re_declaring_the_same_profile_is_harmless()
    {
        AppDataAnchor.Use(AppDataPathProfile.Client);
        _ = AppDataAnchor.Current;

        AppDataAnchor.Use(AppDataPathProfile.Client);

        Assert.AreSame(AppDataPathProfile.Client, AppDataAnchor.Current);
    }

    [TestMethod]
    public void Reading_the_anchor_marks_it_resolved()
    {
        Assert.IsFalse(AppDataAnchor.IsResolved);
        _ = AppDataAnchor.Current;
        Assert.IsTrue(AppDataAnchor.IsResolved);
    }

    [TestMethod]
    public void Only_the_all_in_one_has_a_past_to_migrate()
    {
        // AppService's static constructor runs the pre-redirect migration only for the
        // profile that could have one; every other names a directory that never shipped.
        Assert.IsTrue(AppDataPathProfile.AllInOne.IsAllInOne);
        Assert.IsFalse(AppDataPathProfile.Client.IsAllInOne);
    }
}
