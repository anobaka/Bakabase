using Bakabase.Modules.Downloader.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public class VerifiedTorrentFileTests
{
    private DirectoryInfo _root = null!;

    [TestInitialize]
    public void SetUp() => _root = Directory.CreateTempSubdirectory();

    [TestCleanup]
    public void CleanUp() => _root.Delete(true);

    [TestMethod]
    public void FinalizesTheEnginePartialAfterVerificationAndAcceptsTheFinalNameOnRetry()
    {
        var final = Path.Combine(_root.FullName, "video.mkv");
        var partial = final + ".!mt";
        File.WriteAllBytes(partial, [9, 7, 3]);
        for (var attempt = 0; attempt < 2; attempt++)
            Assert.AreEqual(final, BuiltInTorrentDownloader.FinalizeVerifiedFile(_root.FullName, final, partial, 3));
        CollectionAssert.AreEqual(new byte[] {9, 7, 3}, File.ReadAllBytes(final));
        Assert.IsFalse(File.Exists(partial));
    }

    [TestMethod]
    public void PreservesBothPathsOnConflictAndNeverPromotesAShortPartial()
    {
        var final = Path.Combine(_root.FullName, "file.bin");
        var partial = final + ".!mt";
        File.WriteAllBytes(partial, [1, 2]);
        Assert.ThrowsException<IOException>(() => BuiltInTorrentDownloader.FinalizeVerifiedFile(_root.FullName, final, partial, 3));
        Assert.IsFalse(File.Exists(final));
        File.WriteAllBytes(final, [8, 9]);
        Assert.ThrowsException<IOException>(() => BuiltInTorrentDownloader.FinalizeVerifiedFile(_root.FullName, final, partial, 2));
        CollectionAssert.AreEqual(new byte[] {8, 9}, File.ReadAllBytes(final));
        CollectionAssert.AreEqual(new byte[] {1, 2}, File.ReadAllBytes(partial));
    }

    [TestMethod]
    public void RejectsPathsOutsideTheJobBeforeRenamingOrCreatingFiles()
    {
        var final = Path.Combine(_root.FullName, "subdir", "empty.txt");
        Assert.ThrowsException<IOException>(() => BuiltInTorrentDownloader.FinalizeVerifiedFile(
            _root.FullName, final, Path.Combine(_root.FullName, "..", "elsewhere.!mt"), 0));
        Assert.IsFalse(File.Exists(final));
        Assert.AreEqual(final, BuiltInTorrentDownloader.FinalizeVerifiedFile(_root.FullName, final, final + ".!mt", 0));
        Assert.AreEqual(0L, new FileInfo(final).Length);
    }
}
