using System;
using System.IO;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class ExHentaiGalleryDirectoryClaimTests
{
    private string _root = null!;

    [TestInitialize]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "BakabaseGalleryClaims_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        if (Directory.Exists(_root + "-moved")) Directory.Delete(_root + "-moved", true);
    }

    [TestMethod]
    public void SameTitleGetsDistinctDirectoriesAndSameGalleryResumes()
    {
        const string firstKey = "4090464/c5a2138551";
        const string secondKey = "4090461/25e70c0312";
        var first = ExHentaiGalleryDirectoryClaim.Claim(_root, "[Misc] 无题", firstKey);
        var second = ExHentaiGalleryDirectoryClaim.Claim(_root, "[Misc] 无题", secondKey);

        Assert.AreEqual(Path.Combine(_root, "[Misc] 无题"), first);
        Assert.AreEqual(Path.Combine(_root, "[Misc] 无题 [g4090461]"), second);
        Assert.AreEqual(first, ExHentaiGalleryDirectoryClaim.Claim(_root, "[Misc] 无题", firstKey));
        Assert.AreEqual(second, ExHentaiGalleryDirectoryClaim.Claim(_root, "[Misc] 无题", secondKey));
        ExHentaiGalleryDirectoryClaim.EnsureOwned(first, firstKey);
        ExHentaiGalleryDirectoryClaim.EnsureOwned(second, secondKey);
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryDirectoryClaim.EnsureOwned(first, secondKey));
    }

    [TestMethod]
    public void MovedDirectoryFreesPlainNameWithoutKeepingAHistoricalReservation()
    {
        const string firstKey = "101/abc";
        const string secondKey = "102/def";
        var first = ExHentaiGalleryDirectoryClaim.Claim(_root, "x", firstKey);
        Directory.Move(first, _root + "-moved");

        var second = ExHentaiGalleryDirectoryClaim.Claim(_root, "x", secondKey);
        var firstRetry = ExHentaiGalleryDirectoryClaim.Claim(_root, "x", firstKey);

        Assert.AreEqual(Path.Combine(_root, "x"), second);
        Assert.AreEqual(Path.Combine(_root, "x [g101]"), firstRetry);
        ExHentaiGalleryDirectoryClaim.EnsureOwned(_root + "-moved", firstKey);
    }

    [TestMethod]
    public void ValidatedHintKeepsTheOriginalDirectoryWhenTitleChanges()
    {
        const string key = "201/abc";
        var original = ExHentaiGalleryDirectoryClaim.Claim(_root, "old title", key);
        Assert.AreEqual(original, ExHentaiGalleryDirectoryClaim.Claim(_root, "new title", key));

        Directory.Delete(original, true);
        var replacement = ExHentaiGalleryDirectoryClaim.Claim(_root, "new title", key);
        Assert.AreEqual(Path.Combine(_root, "new title"), replacement,
            "A stale hint must not reserve a missing directory.");
        Assert.AreEqual(replacement, ExHentaiGalleryDirectoryClaim.Claim(_root, "another title", key),
            "The replacement hint should support subsequent title changes.");
    }

    [TestMethod]
    public void NonemptyUnmarkedDirectoryIsUnknownButAnEmptyOneCanBeClaimed()
    {
        var legacy = Path.Combine(_root, "legacy");
        Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "image.jpg"), "existing file");

        var claimed = ExHentaiGalleryDirectoryClaim.Claim(_root, "legacy", "301/abc");
        Assert.AreEqual(Path.Combine(_root, "legacy [g301]"), claimed);
        Assert.IsFalse(File.Exists(Path.Combine(legacy, ExHentaiGalleryDirectoryClaim.MarkerFileName)));

        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.AreEqual(empty, ExHentaiGalleryDirectoryClaim.Claim(_root, "empty", "302/def"));
    }

    [TestMethod]
    public void MissingRootIsNotRecreated()
    {
        Directory.Delete(_root, true);
        Assert.ThrowsException<DirectoryNotFoundException>(() =>
            ExHentaiGalleryDirectoryClaim.Claim(_root, "x", "401/abc"));
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public void LinkedChildCannotRedirectGalleryFilesOutsideItsOwnedDirectory()
    {
        if (OperatingSystem.IsWindows()) return;

        var gallery = ExHentaiGalleryDirectoryClaim.Claim(_root, "gallery", "501/abc");
        var other = Path.Combine(_root, "other");
        Directory.CreateDirectory(other);
        Directory.CreateSymbolicLink(Path.Combine(gallery, "pages"), other);
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked-parent"), other);

        Assert.ThrowsException<IOException>(() => ExHentaiGalleryDirectoryClaim.EnsureOwnedOutputPath(
            _root, gallery, "501/abc", Path.Combine(gallery, "pages", "001.jpg")));
        Assert.ThrowsException<IOException>(() => ExHentaiGalleryDirectoryClaim.Claim(
            _root, Path.Combine("linked-parent", "another gallery"), "502/abc"));
        Assert.IsFalse(Directory.Exists(Path.Combine(other, "another gallery")));
        ExHentaiGalleryDirectoryClaim.EnsureOwnedOutputPath(_root, gallery, "501/abc",
            Path.Combine(gallery, "001.jpg"));
    }
}
