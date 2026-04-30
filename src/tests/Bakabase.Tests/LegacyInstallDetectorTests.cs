using System;
using System.IO;
using Bakabase.Infrastructures.Components.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class LegacyInstallDetectorTests
{
    private string _root = null!;
    private string _installRoot = null!;
    private string _baseDir = null!;
    private string _legacyAppData = null!;
    private string _userDataDir = null!;

    [TestInitialize]
    public void Init()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-legacy-" + Guid.NewGuid().ToString("N"));
        _installRoot = Path.Combine(_root, "Bakabase-install");
        _baseDir = Path.Combine(_installRoot, "current");
        _legacyAppData = Path.Combine(_installRoot, "current", "AppData");
        _userDataDir = Path.Combine(_root, "user-data");

        Directory.CreateDirectory(_baseDir);
        // Sentinel that DataPathValidator looks for to identify a Velopack install root.
        File.WriteAllText(Path.Combine(_installRoot, "Update.exe"), "stub");
        Directory.CreateDirectory(_userDataDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void NoInstallRoot_NoNotice()
    {
        var d = LegacyInstallAppDataDetector.Detect(
            Path.GetTempPath(), _userDataDir, dismissedAt: null);
        Assert.IsFalse(d.ShouldNotify);
    }

    [TestMethod]
    public void LegacyAppDataAbsent_NoNotice()
    {
        // install root present, but no current/AppData inside
        var d = LegacyInstallAppDataDetector.Detect(
            _baseDir, _userDataDir, dismissedAt: null);
        Assert.IsFalse(d.ShouldNotify);
        StringAssert.Contains(d.Reason ?? "", "absent");
    }

    [TestMethod]
    public void LegacyAppDataEmpty_NoNotice()
    {
        Directory.CreateDirectory(_legacyAppData);
        var d = LegacyInstallAppDataDetector.Detect(
            _baseDir, _userDataDir, dismissedAt: null);
        Assert.IsFalse(d.ShouldNotify);
        StringAssert.Contains(d.Reason ?? "", "empty");
    }

    [TestMethod]
    public void LegacyAppDataPopulated_NoticePushed()
    {
        Directory.CreateDirectory(_legacyAppData);
        File.WriteAllText(Path.Combine(_legacyAppData, "bakabase_insideworld.db"), "stub");

        var d = LegacyInstallAppDataDetector.Detect(
            _baseDir, _userDataDir, dismissedAt: null);
        Assert.IsTrue(d.ShouldNotify);
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(_legacyAppData)),
            d.LegacyPath);
    }

    [TestMethod]
    public void LegacyAppDataIsCurrentDataDir_NoNotice()
    {
        // User explicitly kept their DataPath at the legacy location — don't shame them.
        Directory.CreateDirectory(_legacyAppData);
        File.WriteAllText(Path.Combine(_legacyAppData, "data.bin"), "stub");

        var d = LegacyInstallAppDataDetector.Detect(
            _baseDir, _legacyAppData, dismissedAt: null);
        Assert.IsFalse(d.ShouldNotify);
        StringAssert.Contains(d.Reason ?? "", "user choice");
    }

    [TestMethod]
    public void NoticeAlreadyDismissed_NoFire()
    {
        Directory.CreateDirectory(_legacyAppData);
        File.WriteAllText(Path.Combine(_legacyAppData, "data.bin"), "stub");

        var d = LegacyInstallAppDataDetector.Detect(
            _baseDir, _userDataDir, dismissedAt: DateTime.UtcNow);
        Assert.IsFalse(d.ShouldNotify);
        StringAssert.Contains(d.Reason ?? "", "Dismissed");
    }
}
