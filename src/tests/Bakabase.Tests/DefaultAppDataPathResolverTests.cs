using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Bakabase.Infrastructures.Components.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class DefaultAppDataPathResolverTests
{
    private static Func<string, string?> Env(Dictionary<string, string?> map) =>
        k => map.GetValueOrDefault(k);

    private static Func<Environment.SpecialFolder, string> Folder(
        Dictionary<Environment.SpecialFolder, string> map) =>
        f => map.TryGetValue(f, out var v) ? v : throw new KeyNotFoundException(f.ToString());

    [TestMethod]
    public void EnvVar_TakesPrecedence_OnAllPlatforms()
    {
        foreach (var platform in new[] { OSPlatform.Windows, OSPlatform.OSX, OSPlatform.Linux })
        {
            var path = DefaultAppDataPathResolver.Resolve(
                platform,
                Env(new() { ["BAKABASE_DATA_DIR"] = "/custom/bakabase" }),
                Folder(new()
                {
                    [Environment.SpecialFolder.LocalApplicationData] = "/local",
                    [Environment.SpecialFolder.UserProfile] = "/home/foo",
                    [Environment.SpecialFolder.ApplicationData] = "/app",
                }),
                "Bakabase");

            // Path.GetFullPath normalises but the input is already absolute.
            Assert.AreEqual(Path.GetFullPath("/custom/bakabase"), path,
                $"env-var override should win on {platform}");
        }
    }

    [TestMethod]
    public void EnvVar_RelativePath_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            DefaultAppDataPathResolver.Resolve(
                OSPlatform.Linux,
                Env(new() { ["BAKABASE_DATA_DIR"] = "relative/path" }),
                Folder(new() { [Environment.SpecialFolder.UserProfile] = "/home/foo" }),
                "Bakabase"));
    }

    [TestMethod]
    public void EnvVar_NormalizedToAbsolute()
    {
        // Path.GetFullPath collapses redundant segments — exercise that.
        var input = Path.Combine("/custom", ".", "bakabase", "..", "bakabase");
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Linux,
            Env(new() { ["BAKABASE_DATA_DIR"] = input }),
            Folder(new() { [Environment.SpecialFolder.UserProfile] = "/home/foo" }),
            "Bakabase");
        Assert.AreEqual(Path.GetFullPath(input), path);
    }

    [TestMethod]
    public void Windows_NoEnv_UsesLocalApplicationData()
    {
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Windows,
            Env(new()),
            Folder(new()
            {
                [Environment.SpecialFolder.LocalApplicationData] = @"C:\Users\foo\AppData\Local"
            }),
            "Bakabase");
        // Distinct from FolderName ("Bakabase") so the anchor sits OUTSIDE Velopack's install
        // root — uninstall / repair both nuke the install dir, and we don't want them taking
        // user data with them.
        Assert.AreEqual(
            Path.Combine(@"C:\Users\foo\AppData\Local", DefaultAppDataPathResolver.WindowsAppDataFolderName),
            path);
        Assert.AreEqual("Bakabase.AppData", DefaultAppDataPathResolver.WindowsAppDataFolderName);
    }

    [TestMethod]
    public void MacOS_NoEnv_UsesApplicationSupportUnderHome()
    {
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.OSX,
            Env(new()),
            Folder(new() { [Environment.SpecialFolder.UserProfile] = "/Users/foo" }),
            "Bakabase");
        Assert.AreEqual("/Users/foo/Library/Application Support/Bakabase", path);
    }

    [TestMethod]
    public void Linux_NoEnv_NoXdg_UsesLocalShareUnderHome()
    {
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Linux,
            Env(new()),
            Folder(new() { [Environment.SpecialFolder.UserProfile] = "/home/foo" }),
            "Bakabase");
        Assert.AreEqual("/home/foo/.local/share/Bakabase", path);
    }

    [TestMethod]
    public void Linux_NoEnv_WithXdg_UsesXdgPath()
    {
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Linux,
            Env(new() { ["XDG_DATA_HOME"] = "/data/share" }),
            Folder(new() { [Environment.SpecialFolder.UserProfile] = "/home/foo" }),
            "Bakabase");
        Assert.AreEqual("/data/share/Bakabase", path);
    }

    [TestMethod]
    public void Linux_NoEnv_BlankXdg_FallsBackToHome()
    {
        // XDG_DATA_HOME may be set but blank — must fall through to home, per resolver semantics.
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Linux,
            Env(new() { ["XDG_DATA_HOME"] = "   " }),
            Folder(new() { [Environment.SpecialFolder.UserProfile] = "/home/foo" }),
            "Bakabase");
        Assert.AreEqual("/home/foo/.local/share/Bakabase", path);
    }

    [TestMethod]
    public void Debug_UsesAppDataWithDebuggingSuffix()
    {
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Windows,
            Env(new()),
            Folder(new() { [Environment.SpecialFolder.ApplicationData] = @"C:\Users\foo\AppData\Roaming" }),
            "Bakabase",
            isDebug: true);
        Assert.AreEqual(Path.Combine(@"C:\Users\foo\AppData\Roaming", "Bakabase.Debugging"), path);
    }

    [TestMethod]
    public void EnvVar_WhitespaceOnly_FallsThroughToPlatformDefault()
    {
        // Whitespace-only env var should NOT short-circuit (treated as unset).
        var path = DefaultAppDataPathResolver.Resolve(
            OSPlatform.Linux,
            Env(new() { ["BAKABASE_DATA_DIR"] = "   " }),
            Folder(new() { [Environment.SpecialFolder.UserProfile] = "/home/foo" }),
            "Bakabase");
        Assert.AreEqual("/home/foo/.local/share/Bakabase", path);
    }
}
