using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class DeferredEmptyDirectoryTreeTests
{
    private string fixture = null!, root = null!;
    [TestInitialize] public void Setup()
    {
        fixture = Path.Combine(Path.GetTempPath(), "deferred-directory-" + Guid.NewGuid().ToString("N"));
        root = Path.Combine(fixture, "owned");
        Directory.CreateDirectory(Path.Combine(root, "inner", "empty"));
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(fixture, true);

    [TestMethod]
    public void MarkerRemovalLeavesOnlyRecordedEmptyDirectoriesToFinalize()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows keeps the manual cleanup path.");
        var marker = Path.Combine(root, "inner", ".smbdeleteAAA0001.4");
        File.WriteAllText(marker, "pending");
        var dirs = DeferredEmptyDirectoryTree.CaptureMarkerTree(root)!.ToArray();
        CollectionAssert.AreEqual(new[] {"", "inner", "inner/empty"}, dirs.Select(d => d.Path).ToArray());
        Assert.AreEqual(DeferredEmptyDirectoryResult.Waiting, DeferredEmptyDirectoryTree.InspectEmptyTree(root, dirs));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Waiting, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
        Assert.IsTrue(File.Exists(marker));
        File.Delete(marker);
        Assert.IsNull(DeferredEmptyDirectoryTree.CaptureMarkerTree(root));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Completed, DeferredEmptyDirectoryTree.InspectEmptyTree(root, dirs));
        Assert.IsTrue(Directory.Exists(Path.Combine(root, "inner", "empty")));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Completed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
        Assert.IsFalse(Directory.Exists(root));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Completed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
    }

    [TestMethod]
    public void DarwinCollisionCounterRecognizesPunctuationOnlyInTheProviderFormat()
    {
        foreach (var letter in new[] {'[', '\\', ']', '^', '_', '`'})
        foreach (var prefix in new[] {$"{letter}AA", $"A{letter}A", $"AA{letter}"})
            Assert.IsTrue(FileProcessingFiles.IsSmbDeleteMarker($".smbdelete{prefix}05af4.4"));
        foreach (var name in new[] {".smbdelete_AA05af.4", ".smbdeleteAA_05af4.jpg", ".smbdelete[,A05af4.4",
                     ".smbdelete_AAname4.4", ".smbdelete/AA05af4.4", ".smbdelete_AA05af4.4.txt"})
            Assert.IsFalse(FileProcessingFiles.IsSmbDeleteMarker(name));
        Assert.IsTrue(FileProcessingFiles.IsSmbDeleteMarker(".smbdeleteABC.4"));
    }

    [TestMethod]
    public void MarkerContainingBackslashIsObservedOnlyAsARegularLeaf()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("A backslash is a path separator on Windows.");
        const string name = @".smbdelete\AA05af4.4";
        var marker = Path.Combine(root, name);
        File.WriteAllText(marker, "open");
        var dirs = DeferredEmptyDirectoryTree.CaptureMarkerTree(root)!;
        Assert.IsNotNull(dirs);
        Assert.AreEqual(DeferredEmptyDirectoryResult.Waiting, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
        Assert.IsTrue(File.Exists(marker));
        File.Delete(marker);
        Directory.CreateDirectory(marker);
        Assert.IsNull(DeferredEmptyDirectoryTree.CaptureMarkerTree(root));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
        Assert.IsTrue(Directory.Exists(marker));
        Directory.Delete(marker);
        File.CreateSymbolicLink(marker, Path.Combine(fixture, "missing"));
        Assert.IsNull(DeferredEmptyDirectoryTree.CaptureMarkerTree(root));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
    }

    [TestMethod]
    [DataRow("settled")]
    [DataRow("regularRemains")]
    [DataRow("replacedDirectory")]
    [DataRow("newDirectory")]
    [DataRow("newSymlink")]
    public async Task MixedViewCanSettleOnlyWithinItsInitiallyConfirmedDirectories(string change)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows keeps the manual cleanup path.");
        var marker = Path.Combine(root, ".smbdeleteAAA05af4.4");
        var stale = Path.Combine(root, "old.bin");
        File.WriteAllText(marker, "pending");
        File.WriteAllText(stale, "ordinary");
        var observing = DeferredEmptyDirectoryTree.CaptureAfterDeleteAsync(root);
        if (change != "regularRemains") File.Delete(stale);
        if (change == "newDirectory") Directory.CreateDirectory(Path.Combine(root, "new"));
        if (change == "newSymlink") File.CreateSymbolicLink(Path.Combine(root, "link"), marker);
        if (change == "replacedDirectory")
        {
            var empty = Path.Combine(root, "inner", "empty");
            Directory.Move(empty, Path.Combine(fixture, "old-directory"));
            Directory.CreateDirectory(empty);
        }
        var captured = await observing;
        Assert.AreEqual(change == "settled", captured != null);
        Assert.IsTrue(File.Exists(marker));
        if (captured != null)
            Assert.AreEqual(DeferredEmptyDirectoryResult.Waiting, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, captured));
        if (change == "regularRemains") Assert.AreEqual("ordinary", File.ReadAllText(stale));
    }

    [TestMethod]
    [DataRow("regular")]
    [DataRow("directory")]
    [DataRow("link")]
    [DataRow("dangling")]
    [DataRow("marker-link")]
    public void AnUnexpectedEntryRetainsTheEntireTree(string change)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows keeps the manual cleanup path.");
        var marker = Path.Combine(root, ".smbdeleteF1.4");
        File.WriteAllText(marker, "pending");
        var dirs = DeferredEmptyDirectoryTree.CaptureMarkerTree(root)!;
        var entry = Path.Combine(root, "inner", change == "marker-link" ? ".smbdeleteABC.4" : "new");
        if (change == "regular") File.WriteAllText(entry, "keep");
        else if (change == "directory") Directory.CreateDirectory(entry);
        else if (change == "dangling") File.CreateSymbolicLink(entry, Path.Combine(fixture, "missing"));
        else File.CreateSymbolicLink(entry, marker);
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
        if (change != "directory") Assert.IsNull(DeferredEmptyDirectoryTree.CaptureMarkerTree(root));
        Assert.IsTrue(Directory.Exists(Path.Combine(root, "inner", "empty")));
        Assert.IsTrue(File.Exists(marker));
    }

    [TestMethod]
    public void DoesNotFollowReplacedRootOrAncestor()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows keeps the manual cleanup path.");
        var marker = Path.Combine(root, "inner", ".smbdelete123");
        File.WriteAllText(marker, "pending");
        var dirs = DeferredEmptyDirectoryTree.CaptureMarkerTree(root)!;
        File.Delete(marker);
        var elsewhere = Path.Combine(fixture, "elsewhere");
        Directory.Move(root, elsewhere);
        Directory.CreateSymbolicLink(root, elsewhere);
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, dirs));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed,
            DeferredEmptyDirectoryTree.TryRemoveEmptyTree(Path.Combine(root, "inner"), new[] {new DeferredDirectoryIdentity("", 1, 1)}));
        Assert.IsTrue(Directory.Exists(Path.Combine(elsewhere, "inner", "empty")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AReplacementWithTheSameNameIsPreserved(bool replaceRoot)
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows keeps the manual cleanup path.");
        var marker = Path.Combine(root, ".smbdeleteABC");
        File.WriteAllText(marker, "pending");
        var receipt = DeferredEmptyDirectoryTree.CaptureMarkerTree(root)!;
        File.Delete(marker);
        var original = replaceRoot ? root : Path.Combine(root, "inner", "empty");
        Directory.Move(original, Path.Combine(fixture, "previous"));
        Directory.CreateDirectory(original);
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.InspectEmptyTree(root, receipt));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, receipt));
        Assert.IsTrue(Directory.Exists(original));
    }

    [TestMethod]
    public void DurableDirectoryIdentitySurvivesRenumberingAndOlderReceiptsRemainStrict()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Windows keeps the manual cleanup path.");
        var marker = Path.Combine(root, ".smbdeleteABC");
        File.WriteAllText(marker, "pending");
        var actual = DeferredEmptyDirectoryTree.CaptureMarkerTree(root)!;
        if (actual.Any(entry => entry.CleanupIdentity == null))
            Assert.Inconclusive("The test volume does not support writable directory attributes.");
        Assert.AreEqual(actual.Count, actual.Select(entry => entry.CleanupIdentity).Distinct().Count());
        var renumbered = actual.Select(entry => entry with { Inode = ~entry.Inode }).ToArray();
        var legacy = renumbered.Select(entry => entry with { CleanupIdentity = null }).ToArray();
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.InspectEmptyTree(root, legacy));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Waiting, DeferredEmptyDirectoryTree.InspectEmptyTree(root, renumbered));
        var wrong = actual.Select(entry => entry.Path == "inner" ? entry with { CleanupIdentity = Guid.NewGuid() } : entry).ToArray();
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.InspectEmptyTree(root, wrong));
        Assert.IsTrue(File.Exists(marker));
        File.Delete(marker);
        Assert.AreEqual(DeferredEmptyDirectoryResult.Completed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, renumbered));
        Assert.IsFalse(Directory.Exists(root));
    }

    [TestMethod]
    public void InvalidReceiptsCannotAuthorizeCleanup()
    {
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, new[] {new DeferredDirectoryIdentity("", 1, 1), new DeferredDirectoryIdentity("../other", 1, 1)}));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, new[] {new DeferredDirectoryIdentity("", 1, 1), new DeferredDirectoryIdentity("unlisted/deeper", 1, 1)}));
        Assert.AreEqual(DeferredEmptyDirectoryResult.Changed, DeferredEmptyDirectoryTree.TryRemoveEmptyTree(root, new[] {new DeferredDirectoryIdentity("", 1, 1), new DeferredDirectoryIdentity("", 1, 1)}));
        Assert.IsTrue(Directory.Exists(root));
    }
}
