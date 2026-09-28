using System;
using System.IO;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiGalleryOutputPathTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseGalleryOutput_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        var linkedRoot = _root + "-linked";
        if (Directory.Exists(linkedRoot)) Directory.Delete(linkedRoot);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        var outside = _root + "-outside";
        if (Directory.Exists(outside)) Directory.Delete(outside, true);
    }

    [TestMethod]
    public void ResolveCreatesAndReusesExistingNonemptyDirectoryWithoutSidecars()
    {
        var relative = Path.Combine("subfolder", "Gallery [g12345]");
        var expected = Path.Combine(_root, relative);

        Assert.AreEqual(expected, ExHentaiGalleryOutputPath.Resolve(_root, relative));
        var existingFile = Path.Combine(expected, "001.jpg");
        File.WriteAllText(existingFile, "already downloaded");
        Assert.AreEqual(expected, ExHentaiGalleryOutputPath.Resolve(_root, relative));
        Assert.AreEqual("already downloaded", File.ReadAllText(existingFile));
        Assert.IsFalse(File.Exists(Path.Combine(expected, ".bakabase-exhentai-gallery.json")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, ".bakabase-exhentai-gallery-index")));

        ExHentaiGalleryOutputPath.EnsureSafeOutputPath(_root, expected, existingFile);
    }

    [TestMethod]
    public void ResolveRequiresAnExistingRoot()
    {
        Directory.Delete(_root);
        Assert.ThrowsException<DirectoryNotFoundException>(() =>
            ExHentaiGalleryOutputPath.Resolve(_root, "Gallery [g12345]"));
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public void ResolveRejectsAbsoluteAndEscapingDirectories()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            ExHentaiGalleryOutputPath.Resolve(_root, Path.Combine("..", "elsewhere")));
        Assert.ThrowsException<ArgumentException>(() =>
            ExHentaiGalleryOutputPath.Resolve(_root, Path.Combine(_root, "absolute")));
        Assert.ThrowsException<ArgumentException>(() => ExHentaiGalleryOutputPath.Resolve(_root, "."));
        Assert.IsFalse(Directory.Exists(_root + "-outside"));
    }

    [TestMethod]
    public void EnsureSafeOutputPathRejectsFilesOutsideGalleryOrRoot()
    {
        var gallery = ExHentaiGalleryOutputPath.Resolve(_root, "Gallery [g12345]");
        var other = ExHentaiGalleryOutputPath.Resolve(_root, "Other [g67890]");

        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.EnsureSafeOutputPath(
            _root, gallery, Path.Combine(other, "001.jpg")));
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.EnsureSafeOutputPath(
            _root, _root + "-outside", Path.Combine(_root + "-outside", "001.jpg")));
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.EnsureSafeOutputPath(
            _root, gallery, Path.Combine(gallery, "..", "escape.jpg")));
    }

    [TestMethod]
    public void LinkedDirectoryAndOutputFileCannotRedirectWrites()
    {
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        var linkedParent = Path.Combine(_root, "linked-parent");
        try
        {
            Directory.CreateSymbolicLink(linkedParent, outside);
        }
        catch (Exception e) when (OperatingSystem.IsWindows() && e is UnauthorizedAccessException or IOException)
        {
            // Creating a symlink on Windows can require Developer Mode or elevated privileges.
            return;
        }

        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.Resolve(
            _root, Path.Combine("linked-parent", "Gallery")));
        Assert.IsFalse(Directory.Exists(Path.Combine(outside, "Gallery")));

        var gallery = ExHentaiGalleryOutputPath.Resolve(_root, "Gallery [g12345]");
        var linkedChild = Path.Combine(gallery, "pages");
        Directory.CreateSymbolicLink(linkedChild, outside);
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.EnsureSafeOutputPath(
            _root, gallery, Path.Combine(linkedChild, "001.jpg")));

        var outsideFile = Path.Combine(outside, "outside.jpg");
        File.WriteAllText(outsideFile, "unrelated");
        var linkedFile = Path.Combine(gallery, "linked.jpg");
        File.CreateSymbolicLink(linkedFile, outsideFile);
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.EnsureSafeOutputPath(
            _root, gallery, linkedFile));

        var linkedRoot = _root + "-linked";
        Directory.CreateSymbolicLink(linkedRoot, _root);
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryOutputPath.Resolve(linkedRoot, "another gallery"));
    }
}
