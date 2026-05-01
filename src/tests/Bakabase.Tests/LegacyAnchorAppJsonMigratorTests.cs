using System;
using System.IO;
using Bakabase.Infrastructures.Components.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class LegacyAnchorAppJsonMigratorTests
{
    private string _root = null!;
    private string _anchor = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-legacymig-" + Guid.NewGuid().ToString("N"));
        _anchor = Path.Combine(_root, "anchor");
        Directory.CreateDirectory(_anchor);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string AnchorAppJson =>
        Path.Combine(_anchor, EffectiveAppDataResolver.AppOptionsFileName);

    private void WriteAnchorAppJson(string body) =>
        File.WriteAllText(AnchorAppJson, body);

    [TestMethod]
    public void NoOp_WhenRedirectAlreadyExists()
    {
        var target = Path.Combine(_root, "data");
        Directory.CreateDirectory(target);
        AnchorRedirect.Write(_anchor, target);
        WriteAnchorAppJson("{\"app\":{\"dataPath\":\"" + target.Replace("\\", "\\\\") + "\"}}");

        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);

        // Migrator must short-circuit on existing redirect; anchor app.json untouched.
        Assert.IsTrue(File.Exists(AnchorAppJson));
    }

    [TestMethod]
    public void NoOp_WhenAnchorHasNoAppJson()
    {
        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);
        Assert.IsFalse(AnchorRedirect.Exists(_anchor));
    }

    [TestMethod]
    public void NoOp_WhenAppJsonHasNoDataPath()
    {
        WriteAnchorAppJson("{\"app\":{\"version\":\"1.0\"}}");

        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);

        Assert.IsFalse(AnchorRedirect.Exists(_anchor));
        Assert.IsTrue(File.Exists(AnchorAppJson),
            "no DataPath was set — anchor app.json stays where it is");
    }

    [TestMethod]
    public void NoOp_WhenDataPathEqualsAnchor()
    {
        WriteAnchorAppJson("{\"app\":{\"dataPath\":\"" + _anchor.Replace("\\", "\\\\") + "\"}}");

        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);

        Assert.IsFalse(AnchorRedirect.Exists(_anchor));
        Assert.IsTrue(File.Exists(AnchorAppJson));
    }

    [TestMethod]
    public void Migrates_WhenDataPathPointsElsewhereAndTargetMissing()
    {
        var target = Path.Combine(_root, "data");
        WriteAnchorAppJson(
            "{\"app\":{\"dataPath\":\"" + target.Replace("\\", "\\\\") +
            "\",\"version\":\"2.0\"}}");

        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);

        Assert.IsFalse(File.Exists(AnchorAppJson),
            "anchor app.json must be removed after migration");
        var targetAppJson = Path.Combine(target, EffectiveAppDataResolver.AppOptionsFileName);
        Assert.IsTrue(File.Exists(targetAppJson),
            "app.json must have been copied to the user's data dir");
        StringAssert.Contains(File.ReadAllText(targetAppJson), "\"version\":\"2.0\"");
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void Migrates_WhenTargetAlreadyHasOwnAppJson_TargetWins()
    {
        var target = Path.Combine(_root, "data");
        Directory.CreateDirectory(target);
        var targetAppJson = Path.Combine(target, EffectiveAppDataResolver.AppOptionsFileName);
        File.WriteAllText(targetAppJson, "{\"app\":{\"version\":\"OLD\"}}");

        WriteAnchorAppJson(
            "{\"app\":{\"dataPath\":\"" + target.Replace("\\", "\\\\") +
            "\",\"version\":\"NEW\"}}");

        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);

        Assert.IsFalse(File.Exists(AnchorAppJson));
        Assert.AreEqual("{\"app\":{\"version\":\"OLD\"}}",
            File.ReadAllText(targetAppJson),
            "target's pre-existing app.json must not be overwritten — its older Version is the trigger for IMigrators");
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void Idempotent_OnSecondRun()
    {
        var target = Path.Combine(_root, "data");
        WriteAnchorAppJson(
            "{\"app\":{\"dataPath\":\"" + target.Replace("\\", "\\\\") + "\"}}");

        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);
        LegacyAnchorAppJsonMigrator.RunIfNeeded(_anchor);

        Assert.IsTrue(AnchorRedirect.Exists(_anchor));
    }
}
