using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Remoting.Components.Console;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Where the desktop app looks for the retired thin client's pairings: exactly where that
/// client kept them, on every platform and in every build, including when the user moved its
/// data or pinned it with an environment variable.
/// </summary>
/// <remarks>
/// Resolved against a fake home directory, so every platform's layout is checked on any
/// machine and nothing real is read.
/// </remarks>
[TestClass]
public class LegacyClientLocationTests
{
    private string _home = null!;
    private Dictionary<string, string?> _environment = null!;

    [TestInitialize]
    public void Setup()
    {
        _home = Path.Combine(Path.GetTempPath(), "bakabase-legacy-location", Guid.NewGuid().ToString("N"));
        _environment = new Dictionary<string, string?>();
    }

    [TestCleanup]
    public void Cleanup() => ConsoleHarness.DeleteRoot(_home);

    private string Folder(Environment.SpecialFolder folder) => folder switch
    {
        Environment.SpecialFolder.LocalApplicationData => Path.Combine(_home, "AppData", "Local"),
        Environment.SpecialFolder.ApplicationData => Path.Combine(_home, "AppData", "Roaming"),
        Environment.SpecialFolder.UserProfile => _home,
        _ => throw new ArgumentOutOfRangeException(nameof(folder), folder, null)
    };

    private string? Resolve(OSPlatform platform, bool isDebug = false) =>
        LegacyClientConnectionSource.ResolveFile(platform, name => _environment.GetValueOrDefault(name), Folder,
            isDebug);

    private static string File(params string[] segments) =>
        Path.Combine([.. segments, "client", ClientConnectionStore.FileName]);

    [TestMethod]
    public void Each_platform_reads_the_client_profiles_own_folder()
    {
        Assert.AreEqual(File(_home, "AppData", "Local", "Bakabase.Client.AppData"), Resolve(OSPlatform.Windows));
        Assert.AreEqual(File(_home, "Library", "Application Support", "Bakabase.Client"), Resolve(OSPlatform.OSX));
        Assert.AreEqual(File(_home, ".local", "share", "Bakabase.Client"), Resolve(OSPlatform.Linux));

        _environment["XDG_DATA_HOME"] = Path.Combine(_home, "xdg");
        Assert.AreEqual(File(_home, "xdg", "Bakabase.Client"), Resolve(OSPlatform.Linux));
    }

    [TestMethod]
    public void Never_the_all_in_ones_own_folder()
    {
        foreach (var platform in new[] {OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux})
        {
            var allInOne = DefaultAppDataPathResolver.Resolve(AppDataPathProfile.AllInOne, platform,
                name => _environment.GetValueOrDefault(name), Folder, "Bakabase");

            Assert.IsFalse(Resolve(platform)!.StartsWith(allInOne + Path.DirectorySeparatorChar,
                StringComparison.Ordinal), platform.ToString());
        }
    }

    [TestMethod]
    public void A_debug_build_reads_a_debug_clients_folder()
    {
        foreach (var platform in new[] {OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux})
        {
            Assert.AreEqual(File(_home, "AppData", "Roaming", "Bakabase.Client.Debugging"), Resolve(platform, true),
                platform.ToString());
        }
    }

    [TestMethod]
    public void A_moved_data_folder_is_followed()
    {
        var anchor = Path.Combine(_home, "Library", "Application Support", "Bakabase.Client");
        var moved = Path.Combine(_home, "Volumes", "External", "Bakabase.Client");
        AnchorRedirect.Write(anchor, moved);

        Assert.AreEqual(File(moved), Resolve(OSPlatform.OSX));
    }

    [TestMethod]
    public void A_broken_redirect_falls_back_to_the_anchor_as_the_thin_client_did()
    {
        var anchor = Path.Combine(_home, "AppData", "Local", "Bakabase.Client.AppData");
        Directory.CreateDirectory(anchor);
        System.IO.File.WriteAllText(AnchorRedirect.GetRedirectPath(anchor), "relative/path");

        Assert.AreEqual(File(anchor), Resolve(OSPlatform.Windows));
    }

    [TestMethod]
    public void BAKABASE_CLIENT_DATA_DIR_wins_over_everything_the_redirect_included()
    {
        var pinned = Path.Combine(_home, "pinned");
        _environment[AppDataPathProfile.Client.EnvVarName] = pinned;

        // A redirect in the pinned folder is not followed, as the thin client did not follow
        // it: the variable names the data directory itself.
        AnchorRedirect.Write(pinned, Path.Combine(_home, "elsewhere"));

        foreach (var platform in new[] {OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux})
        {
            Assert.AreEqual(File(pinned), Resolve(platform), platform.ToString());
            Assert.AreEqual(File(pinned), Resolve(platform, true), $"{platform} debug");
        }

        // The all-in-one's own variable is not the thin client's.
        _environment.Remove(AppDataPathProfile.Client.EnvVarName);
        _environment[AppDataPathProfile.AllInOne.EnvVarName] = Path.Combine(_home, "all-in-one");
        Assert.AreEqual(File(_home, "Library", "Application Support", "Bakabase.Client"), Resolve(OSPlatform.OSX));
    }

    [TestMethod]
    public void A_relative_override_means_there_is_nothing_to_import()
    {
        _environment[AppDataPathProfile.Client.EnvVarName] = "relative";

        Assert.IsNull(Resolve(OSPlatform.Linux));
        Assert.IsNull(new LegacyClientConnectionSource(() => Resolve(OSPlatform.Linux)).Read());
    }

    [TestMethod]
    public async Task Reading_never_creates_or_changes_anything_there()
    {
        var file = Resolve(OSPlatform.OSX)!;
        var source = new LegacyClientConnectionSource(() => file);

        // Nothing there: nothing found, nothing created.
        Assert.IsNull(source.Read());
        Assert.IsFalse(Directory.Exists(Path.Combine(_home, "Library")));

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await System.IO.File.WriteAllTextAsync(file, """{"Servers":[{"ServerId":"a","DeviceKey":"k"}]}""");
        var written = System.IO.File.GetLastWriteTimeUtc(file);

        // Held open for writing by a thin client that is still running: still readable.
        await using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            Assert.AreEqual("a", source.Read()!.Servers[0].ServerId);
        }

        Assert.AreEqual(written, System.IO.File.GetLastWriteTimeUtc(file));
        CollectionAssert.AreEquivalent(new[] {file}, Directory.GetFiles(Path.GetDirectoryName(file)!));
    }
}
