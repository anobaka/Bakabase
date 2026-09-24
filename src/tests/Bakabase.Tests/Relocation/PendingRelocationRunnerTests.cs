using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.App.SingleInstance;
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
    public async Task MergeOverwrite_CopiesAppJsonWhenTargetHasNone()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1 })
         .WithFile("app.json", "{\"app\":{\"version\":\"1.2.3\"}}");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);

        Assert.IsTrue(File.Exists(Path.Combine(h.TargetDir, "app.json")),
            "app.json must follow data when target has no existing app.json");
        Assert.AreEqual("{\"app\":{\"version\":\"1.2.3\"}}",
            File.ReadAllText(Path.Combine(h.TargetDir, "app.json")));
        Assert.IsFalse(File.Exists(
            Path.Combine(h.TargetDir, PendingRelocation.FileName)));
    }

    [TestMethod]
    public async Task MergeOverwrite_TargetAppJsonWins()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1 })
         .WithFile("app.json", "{\"app\":{\"version\":\"NEW\"}}")
         .WithExistingTargetFile("app.json", "{\"app\":{\"version\":\"OLD\"}}");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);

        // Target's pre-existing app.json is preserved as-is — its (older) Version is the
        // signal IMigrators need on the next boot. Source's app.json is left behind.
        Assert.AreEqual("{\"app\":{\"version\":\"OLD\"}}",
            File.ReadAllText(Path.Combine(h.TargetDir, "app.json")));
    }

    [TestMethod]
    public void EnumerateCopyableFiles_ExcludesBookkeepingByDefault()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1 })
         .WithFile(PendingRelocation.FileName, "{}")
         .WithFile(".redirect", "/tmp/somewhere");

        var copyable = PendingRelocationRunner.EnumerateCopyableFiles(h.CurrentDataDir).ToList();
        Assert.AreEqual(1, copyable.Count,
            "marker and .redirect must be excluded by default; app.json now follows data");
        StringAssert.EndsWith(copyable[0], "file1.bin");
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
    public void SqliteIntegrityCheck_ReleasesDatabaseForExclusiveAccess()
    {
        using var h = new RelocationTestHarness();
        const string databaseName = "bakabase_insideworld.db";
        h.WithSqliteDb(databaseName);
        var database = Path.Combine(h.CurrentDataDir, databaseName);
        var length = new FileInfo(database).Length;

        var result = RelocationIntegrityValidator.Validate(h.CurrentDataDir,
            [new RelocationIntegrityValidator.ExpectedFile(databaseName, length)], [databaseName]);

        Assert.IsTrue(result.Ok, result.FailureReason);
        // Windows refuses this while the integrity check retains a pooled handle.
        // Relocation must be free to replace or move the database after validation.
        using var exclusive = new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.AreEqual(length, exclusive.Length);
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

    [TestMethod]
    public void EnumerateCopyableFiles_ExcludesTheInstanceLock()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1 })
         .WithFile(DataDirectoryLock.FileName, "pid=1");

        var copyable = PendingRelocationRunner.EnumerateCopyableFiles(h.CurrentDataDir).ToList();
        Assert.AreEqual(1, copyable.Count, "the lock belongs to whoever runs on a directory, not to the data");
    }

    [TestMethod]
    public async Task MergeOverwrite_SucceedsWhileTheRunningAppHoldsBothLocks()
    {
        // The real situation at startup: this process owns the source (the entry point locked
        // it) and has just locked the target. A held lock file cannot even be read, so copying
        // it would fail the whole move.
        using var h = new RelocationTestHarness().WithCurrentDataDirAt("custom-data");
        h.WithFile("data/keep.bin", new byte[] { 1, 2, 3 });
        using var sourceLock = DataDirectoryLock.TryAcquire(h.CurrentDataDir).Lock!;
        using var targetLock = DataDirectoryLock.TryAcquire(h.TargetDir).Lock!;
        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();

        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, h.ReadFromTarget("data/keep.bin"));

        // The source is emptied but for the lock this process still holds: it stays owned
        // until SingleInstanceGuard.Retire lets go and removes the rest.
        Assert.IsTrue(Directory.Exists(h.CurrentDataDir));
        CollectionAssert.AreEqual(new[] { DataDirectoryLock.FileName },
            Directory.EnumerateFileSystemEntries(h.CurrentDataDir).Select(Path.GetFileName).ToArray());
        Assert.AreEqual(DataDirectoryLockStatus.HeldByAnotherProcess,
            DataDirectoryLock.TryAcquire(h.CurrentDataDir).Status, "still locked through the cleanup");
    }

    [TestMethod]
    public async Task PreviousDataDir_WithAnUnheldLockFile_IsDeletedWhole()
    {
        // A build without the guard (or a leftover file): nobody holds it, so it goes too.
        using var h = new RelocationTestHarness().WithCurrentDataDirAt("custom-data");
        h.WithFile("data/keep.bin", new byte[] { 1 })
         .WithFile(DataDirectoryLock.FileName, "pid=1");
        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var outcome = await h.RunAsync();
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);
        Assert.IsFalse(Directory.Exists(h.CurrentDataDir));
        Assert.IsFalse(File.Exists(Path.Combine(h.TargetDir, DataDirectoryLock.FileName)),
            "and was not copied to the target");
    }
}
