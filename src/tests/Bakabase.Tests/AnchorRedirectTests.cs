using System;
using System.IO;
using Bakabase.Infrastructures.Components.App;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class AnchorRedirectTests
{
    private string _root = null!;
    private string _anchor = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-redirect-" + Guid.NewGuid().ToString("N"));
        _anchor = Path.Combine(_root, "anchor");
        Directory.CreateDirectory(_anchor);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void TryRead_ReturnsNull_WhenFileMissing()
    {
        Assert.IsNull(AnchorRedirect.TryRead(_anchor));
        Assert.IsFalse(AnchorRedirect.Exists(_anchor));
    }

    [TestMethod]
    public void Write_AndRead_RoundTrips()
    {
        var target = Path.Combine(_root, "data");
        AnchorRedirect.Write(_anchor, target);

        Assert.IsTrue(AnchorRedirect.Exists(_anchor));
        var read = AnchorRedirect.TryRead(_anchor);
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            read);
    }

    [TestMethod]
    public void Write_RefusesAnchorAsTarget()
    {
        Assert.ThrowsException<InvalidOperationException>(() =>
            AnchorRedirect.Write(_anchor, _anchor));
    }

    [TestMethod]
    public void Write_RefusesRelativePath()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            AnchorRedirect.Write(_anchor, "relative/path"));
    }

    [TestMethod]
    public void TryRead_TrimsWhitespaceAndNewlines()
    {
        var target = Path.Combine(_root, "data");
        Directory.CreateDirectory(target);
        File.WriteAllText(
            Path.Combine(_anchor, AnchorRedirect.FileName),
            $"  {target}  \n");

        var read = AnchorRedirect.TryRead(_anchor);
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            read);
    }

    [TestMethod]
    public void TryRead_ReturnsNull_WhenContentBlank()
    {
        File.WriteAllText(Path.Combine(_anchor, AnchorRedirect.FileName), "   \n");
        Assert.IsNull(AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void TryRead_Throws_WhenContentIsRelativePath()
    {
        File.WriteAllText(Path.Combine(_anchor, AnchorRedirect.FileName), "relative/dir");
        Assert.ThrowsException<InvalidOperationException>(() =>
            AnchorRedirect.TryRead(_anchor));
    }

    [TestMethod]
    public void Delete_Idempotent()
    {
        AnchorRedirect.Delete(_anchor);
        AnchorRedirect.Write(_anchor, Path.Combine(_root, "data"));
        AnchorRedirect.Delete(_anchor);
        Assert.IsFalse(AnchorRedirect.Exists(_anchor));
    }
}
