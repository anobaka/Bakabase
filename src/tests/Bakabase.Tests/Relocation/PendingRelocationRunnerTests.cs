using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Relocation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Relocation;

[TestClass]
public class PendingRelocationRunnerTests
{
    [TestMethod]
    public async Task NoMarker_ReturnsNoOp()
    {
        using var h = new RelocationTestHarness()
            .WithFile("data/file1.bin", new byte[] { 1, 2, 3 });
        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.NoOp, outcome.Kind);
        Assert.IsNull(h.PersistedDataPath);
    }

    [TestMethod]
    public async Task UnknownSchemaVersion_ReturnsUnknown_NoDestructiveAction()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2, 3 });

        var marker = new PendingRelocation
        {
            SchemaVersion = 99,
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        };
        h.WithMarker(marker);

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.UnknownSchemaVersion, outcome.Kind);
        Assert.IsTrue(h.MarkerExistsInCurrent, "marker preserved");
        Assert.IsFalse(h.EnumerateTargetFiles().Any(),
            "target untouched when schema unknown");
    }

    [TestMethod]
    public async Task UseTarget_OnlyUpdatesAppJsonPointer()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2, 3 });
        Directory.CreateDirectory(h.TargetDir);
        // Pretend target already has data.
        File.WriteAllBytes(
            Path.Combine(h.TargetDir, "data.bin"),
            new byte[] { 9, 9, 9 });

        var marker = new PendingRelocation
        {
            Mode = RelocationMode.UseTarget,
            Target = h.TargetDir,
        };
        h.WithMarker(marker);

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind);
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(h.TargetDir)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(h.PersistedDataPath!)));
        Assert.IsFalse(h.MarkerExistsInCurrent);

        // Source data must remain untouched (user might be linking to a backup).
        Assert.IsTrue(File.Exists(Path.Combine(h.CurrentDataDir, "data/file1.bin")));
    }

    [TestMethod]
    public async Task UseTarget_DoesNotSetPrevDataPath()
    {
        using var h = new RelocationTestHarness();
        Directory.CreateDirectory(h.TargetDir);
        File.WriteAllBytes(Path.Combine(h.TargetDir, "data.bin"), new byte[] { 9 });
        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.UseTarget,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind);
        Assert.IsNull(h.PersistedPrevDataPath,
            "UseTarget did not move data; PrevDataPath must remain unchanged");
    }

    [TestMethod]
    public async Task MergeOverwrite_CopiesSourceFiles_ToEmptyTarget()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2, 3 })
         .WithFile("data/file2.bin", new byte[] { 4, 5, 6, 7 })
         .WithFile("configs/foo.json", "{\"x\":1}");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);

        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3 }, h.ReadFromTarget("data/file1.bin"));
        CollectionAssert.AreEqual(
            new byte[] { 4, 5, 6, 7 }, h.ReadFromTarget("data/file2.bin"));
        Assert.IsFalse(h.StagingExists);
        Assert.IsFalse(h.MarkerExistsInCurrent);
        Assert.IsFalse(h.MarkerExistsInTarget,
            "marker must not propagate to target");
        Assert.AreEqual(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(h.TargetDir)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(h.PersistedDataPath!)));
    }

    [TestMethod]
    public async Task MergeOverwrite_PreservesTargetOnlyFiles()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/keep.bin", new byte[] { 7, 7, 7 })
         .WithFile("configs/main.json", "{\"x\":1}")
         // target has files we don't conflict with — must survive
         .WithExistingTargetFile("third-party/cache/blob1.bin", "preserve me")
         .WithExistingTargetFile("orphan.txt", "preserve me too")
         // and one same-name file that should be overwritten
         .WithExistingTargetFile("data/keep.bin", "stale-source");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);

        // Same-name file replaced with source content.
        CollectionAssert.AreEqual(new byte[] { 7, 7, 7 }, h.ReadFromTarget("data/keep.bin"));
        // Target-only files preserved at every nesting level.
        Assert.AreEqual("preserve me",
            File.ReadAllText(Path.Combine(h.TargetDir, "third-party/cache/blob1.bin")));
        Assert.AreEqual("preserve me too",
            File.ReadAllText(Path.Combine(h.TargetDir, "orphan.txt")));
        // Source's own file landed at its expected path.
        Assert.AreEqual("{\"x\":1}",
            File.ReadAllText(Path.Combine(h.TargetDir, "configs/main.json")));
    }

    [TestMethod]
    public async Task StagingDirAlreadyExists_CleansUpAndRetries()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2, 3 });

        // Pre-create staging dir with garbage from a prior failed attempt.
        var staging = Path.Combine(h.TargetDir, PendingRelocationRunner.StagingDirName);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "old-junk.txt"), "from a previous attempt");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);
        Assert.IsFalse(File.Exists(Path.Combine(h.TargetDir, "old-junk.txt")));
    }

    [TestMethod]
    public async Task ExcludesAppJsonAndMarkerFromCopy()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1 })
         .WithFile("app.json", "{}");

        var copyable = PendingRelocationRunner.EnumerateCopyableFiles(h.CurrentDataDir).ToList();
        Assert.AreEqual(1, copyable.Count, "app.json must not appear in the copyable list");
        StringAssert.EndsWith(copyable[0], "file1.bin");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);

        Assert.IsFalse(File.Exists(Path.Combine(h.TargetDir, "app.json")),
            "app.json must not be copied — it lives at the anchor only");
        Assert.IsFalse(File.Exists(
            Path.Combine(h.TargetDir, PendingRelocation.FileName)));
    }

    [TestMethod]
    public async Task SqliteIntegrityCheckRunsAndPasses()
    {
        using var h = new RelocationTestHarness();
        h.WithSqliteDb("bakabase_insideworld.db");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);
    }

    [TestMethod]
    public async Task MergeOverwrite_RecordsPrevDataPathForRebasing()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2, 3 });

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);
        Assert.IsNotNull(h.PersistedPrevDataPath,
            "MergeOverwrite moved data; PrevDataPath must point at the source so stored absolute paths can be rebased");
    }

    [TestMethod]
    public async Task PreviousDataDir_DeletedAfterSuccessfulCopy()
    {
        using var h = new RelocationTestHarness().WithCurrentDataDirAt("custom-data");
        h.WithFile("data/keep.bin", new byte[] { 1, 2, 3 });

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);
        Assert.IsFalse(Directory.Exists(h.CurrentDataDir),
            "previous custom data dir should be deleted (it's not anchor and not target)");
    }
}
