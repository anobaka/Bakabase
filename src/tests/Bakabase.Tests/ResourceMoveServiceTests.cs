using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.ResourceMove;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BTaskDomain = Bakabase.Abstractions.Models.Domain.BTask;

namespace Bakabase.Tests;

/// <summary>
/// Behavior of the resource move pipeline: batch creation collapses nested selections and
/// validates destinations, the guard rejects overlapping batches, the executor moves files and
/// rewrites resource (and descendant) paths, a conflicting destination fails only its own
/// record, an interrupted-then-retried record whose files already landed skips the physical
/// move, and startup reconciliation flips dead Pending/Moving records to Interrupted.
/// </summary>
[TestClass]
public sealed class ResourceMoveServiceTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ResourceMoveTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private IResourceMoveService Service => _sp.GetRequiredService<IResourceMoveService>();
    private BakabaseDbContext Db => _sp.GetRequiredService<BakabaseDbContext>();
    private IResourceService ResourceService => _sp.GetRequiredService<IResourceService>();

    private static BTaskArgs FakeArgs(IServiceProvider sp) =>
        new(new PauseToken(), CancellationToken.None, new BTaskDomain("test", () => "test"),
            _ => Task.CompletedTask, sp);

    private string Dir(params string[] segments)
    {
        var path = Path.Combine([_testRoot, .. segments]);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<Resource> SeedResource(string path, bool isFile = false)
    {
        await ResourceService.AddOrPutRange([new Resource { Path = path, IsFile = isFile }]);
        return (await ResourceService.GetAll()).Single(r =>
            string.Equals(r.Path, path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task CreateBatch_CollapsesNestedSelection_CreatesPendingRecordsPerTopLevel()
    {
        var dirA = Dir("A");
        var dirSub = Dir("A", "Sub");
        Dir("Dest");
        var a = await SeedResource(dirA);
        var sub = await SeedResource(dirSub);

        var rsp = await Service.CreateBatch([a.Id, sub.Id], Path.Combine(_testRoot, "Dest"));

        rsp.Code.Should().Be(0);
        var records = await Db.Set<ResourceMoveRecordDbModel>().ToListAsync();
        records.Should().ContainSingle();
        records[0].ResourceId.Should().Be(a.Id);
        records[0].Status.Should().Be(ResourceMoveRecordStatus.Pending);
        records[0].DestPath.Should().EndWith("Dest/A");
    }

    [TestMethod]
    public async Task CreateBatch_DestinationInsideSource_Rejected()
    {
        var dirA = Dir("A");
        var dest = Dir("A", "Inner");
        var a = await SeedResource(dirA);

        var rsp = await Service.CreateBatch([a.Id], dest);

        rsp.Code.Should().NotBe(0);
        (await Db.Set<ResourceMoveRecordDbModel>().AnyAsync()).Should().BeFalse();
    }

    [TestMethod]
    public async Task CreateBatch_OverlappingActiveBatch_Rejected()
    {
        var dirA = Dir("A");
        Dir("A", "Sub");
        Dir("Dest1");
        Dir("Dest2");
        var a = await SeedResource(dirA);
        var sub = await SeedResource(Path.Combine(_testRoot, "A", "Sub"));

        // Batch 1 reserves A's subtree; the executor never runs in tests, so it stays reserved.
        (await Service.CreateBatch([a.Id], Path.Combine(_testRoot, "Dest1"))).Code.Should().Be(0);

        var rsp = await Service.CreateBatch([sub.Id], Path.Combine(_testRoot, "Dest2"));

        rsp.Code.Should().NotBe(0);
    }

    [TestMethod]
    public async Task ExecuteBatch_MovesDirectory_UpdatesResourceAndDescendantPaths()
    {
        var dirA = Dir("A");
        var dirSub = Dir("A", "Sub");
        await File.WriteAllTextAsync(Path.Combine(dirA, "f1.txt"), "1");
        await File.WriteAllTextAsync(Path.Combine(dirSub, "f2.txt"), "2");
        var destDir = Dir("Dest");
        var a = await SeedResource(dirA);
        var sub = await SeedResource(dirSub);

        Db.Set<ResourceMoveRecordDbModel>().Add(new ResourceMoveRecordDbModel
        {
            BatchId = "b1",
            ResourceId = a.Id,
            SourcePath = a.Path,
            DestPath = $"{destDir.Replace('\\', '/')}/A",
            Status = ResourceMoveRecordStatus.Pending,
            CreatedAt = DateTime.Now
        });
        await Db.SaveChangesAsync();

        await Service.ExecuteBatch("b1", FakeArgs(_sp));

        var record = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        record.Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        Directory.Exists(dirA).Should().BeFalse();
        File.Exists(Path.Combine(destDir, "A", "f1.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destDir, "A", "Sub", "f2.txt")).Should().BeTrue();

        var movedA = await ResourceService.Get(a.Id);
        var movedSub = await ResourceService.Get(sub.Id);
        movedA!.Path.Should().EndWith("Dest/A");
        movedSub!.Path.Should().EndWith("Dest/A/Sub");
    }

    [TestMethod]
    public async Task ExecuteBatch_DestinationOccupied_WaitsForDecisionButContinuesOthers()
    {
        var dirA = Dir("A");
        var dirB = Dir("B");
        await File.WriteAllTextAsync(Path.Combine(dirB, "f.txt"), "b");
        var destDir = Dir("Dest");
        Dir("Dest", "A"); // occupies A's destination
        var a = await SeedResource(dirA);
        var b = await SeedResource(dirB);

        var dest = destDir.Replace('\\', '/');
        Db.Set<ResourceMoveRecordDbModel>().AddRange(
            new ResourceMoveRecordDbModel
            {
                BatchId = "b2", ResourceId = a.Id, SourcePath = a.Path, DestPath = $"{dest}/A",
                Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now
            },
            new ResourceMoveRecordDbModel
            {
                BatchId = "b2", ResourceId = b.Id, SourcePath = b.Path, DestPath = $"{dest}/B",
                Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now
            });
        await Db.SaveChangesAsync();

        var act = () => Service.ExecuteBatch("b2", FakeArgs(_sp));
        await act.Should().ThrowAsync<BTaskSuspendedException>();

        var records = await Db.Set<ResourceMoveRecordDbModel>().ToListAsync();
        records.Single(r => r.ResourceId == a.Id).Status.Should().Be(ResourceMoveRecordStatus.WaitingForConflict);
        records.Single(r => r.ResourceId == b.Id).Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        File.Exists(Path.Combine(destDir, "B", "f.txt")).Should().BeTrue();
        (await ResourceService.Get(a.Id))!.Path.Should().Be(a.Path, "a failed record must not rewrite the path");
    }

    [TestMethod]
    public async Task ExecuteBatch_LegacySourceGone_DoesNotClaimUnprovenDestination()
    {
        // Simulates retrying an interrupted record whose files already landed: source is gone,
        // destination exists, the physical move demonstrably started in a prior attempt of
        // this record, but the DB still points at the old path.
        var destDir = Dir("Dest");
        Dir("Dest", "A");
        var sourcePath = $"{_testRoot.Replace('\\', '/')}/A"; // never created on disk
        var a = await SeedResource(sourcePath);

        Db.Set<ResourceMoveRecordDbModel>().Add(new ResourceMoveRecordDbModel
        {
            BatchId = "b3", ResourceId = a.Id, SourcePath = a.Path,
            DestPath = $"{destDir.Replace('\\', '/')}/A",
            Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now, Attempts = 1,
            PhysicalMoveStarted = true
        });
        await Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => Service.ExecuteBatch("b3", FakeArgs(_sp))).Should().ThrowAsync<BTaskSuspendedException>();
        var record = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        record.Status.Should().Be(ResourceMoveRecordStatus.WaitingForConflict);
        record.ConflictKind.Should().Be("legacyRecovery");
        (await ResourceService.Get(a.Id))!.Path.Should().Be(a.Path);
    }

    [TestMethod]
    public async Task ExecuteBatch_SourceGoneDestIsForeign_Fails()
    {
        // Same disk shape as the resume case (source gone, destination present), but no attempt
        // of this record ever ran the physical primitives — whatever occupies the destination is
        // someone else's content and must not be claimed as a completed move.
        var destDir = Dir("Dest");
        Dir("Dest", "A");
        var sourcePath = $"{_testRoot.Replace('\\', '/')}/A"; // never created on disk
        var a = await SeedResource(sourcePath);

        Db.Set<ResourceMoveRecordDbModel>().Add(new ResourceMoveRecordDbModel
        {
            BatchId = "b3f", ResourceId = a.Id, SourcePath = a.Path,
            DestPath = $"{destDir.Replace('\\', '/')}/A",
            Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now
        });
        await Db.SaveChangesAsync();

        var act = () => Service.ExecuteBatch("b3f", FakeArgs(_sp));
        await act.Should().ThrowAsync<BTaskException>();

        var record = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        record.Status.Should().Be(ResourceMoveRecordStatus.Failed);
        (await ResourceService.Get(a.Id))!.Path.Should().Be(a.Path, "the DB path must stay untouched");
    }

    [TestMethod]
    public async Task ExecuteBatch_LegacyPartialMove_RequiresExplicitManualSourceRestoration()
    {
        // A prior attempt moved f1 and was interrupted; source still holds f2, destination
        // holds f1. The resume must merge the remainder instead of failing on "destination
        // exists", and end with everything at the destination.
        var dirA = Dir("A");
        await File.WriteAllTextAsync(Path.Combine(dirA, "f2.txt"), "2");
        var destDir = Dir("Dest");
        var partialDest = Dir("Dest", "A");
        await File.WriteAllTextAsync(Path.Combine(partialDest, "f1.txt"), "1");
        var a = await SeedResource(dirA);

        Db.Set<ResourceMoveRecordDbModel>().Add(new ResourceMoveRecordDbModel
        {
            BatchId = "b3r", ResourceId = a.Id, SourcePath = a.Path,
            DestPath = $"{destDir.Replace('\\', '/')}/A",
            Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now, Attempts = 1,
            PhysicalMoveStarted = true
        });
        await Db.SaveChangesAsync();

        await FluentActions.Awaiting(() => Service.ExecuteBatch("b3r", FakeArgs(_sp))).Should().ThrowAsync<BTaskSuspendedException>();
        var record = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        record.ConflictKind.Should().Be("legacyRecovery");
        File.Copy(Path.Combine(partialDest, "f1.txt"), Path.Combine(dirA, "f1.txt"));
        (await Service.ResolveConflict(record.Id, new() { Action = "restoreSource", Scope = "once", ConflictVersion = record.ConflictVersion })).Code.Should().Be(0);
        record.Status.Should().Be(ResourceMoveRecordStatus.Cancelled);
        Directory.Exists(dirA).Should().BeTrue();
        File.ReadAllText(Path.Combine(partialDest, "f1.txt")).Should().Be("1");
        (await ResourceService.Get(a.Id))!.Path.Should().Be(a.Path);
    }

    [TestMethod]
    public async Task ExecuteBatch_SiblingPrefixDestination_MovesViaNativeRename()
    {
        // /root/A → /root/ABC/A: the destination string starts with the source string without
        // being under it, which the Bootstrap primitives falsely reject; the service must take
        // the native-rename fallback instead of failing.
        var dirA = Dir("A");
        await File.WriteAllTextAsync(Path.Combine(dirA, "f.txt"), "x");
        var destDir = Dir("ABC");
        var a = await SeedResource(dirA);

        Db.Set<ResourceMoveRecordDbModel>().Add(new ResourceMoveRecordDbModel
        {
            BatchId = "b3s", ResourceId = a.Id, SourcePath = a.Path,
            DestPath = $"{destDir.Replace('\\', '/')}/A",
            Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now
        });
        await Db.SaveChangesAsync();

        await Service.ExecuteBatch("b3s", FakeArgs(_sp));

        var record = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        record.Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        Directory.Exists(dirA).Should().BeFalse();
        File.Exists(Path.Combine(destDir, "A", "f.txt")).Should().BeTrue();
        (await ResourceService.Get(a.Id))!.Path.Should().EndWith("ABC/A");
    }

    [TestMethod]
    public async Task Preview_ListsResourcesInsideMovedDirectory_WithSelectionFlag()
    {
        // A resource inside a moved directory rides along even when it was never selected;
        // the preview must surface it so nothing moves silently.
        var dirA = Dir("A");
        var dirSub = Dir("A", "Sub");
        Dir("Unrelated");
        Dir("Dest");
        var a = await SeedResource(dirA);
        var sub = await SeedResource(dirSub);
        await SeedResource(Path.Combine(_testRoot, "Unrelated"));

        var unselected = await Service.Preview([a.Id], Path.Combine(_testRoot, "Dest"));

        unselected.Code.Should().Be(0);
        var item = unselected.Data!.Items.Single();
        var covered = item.CoveredResources.Single();
        covered.ResourceId.Should().Be(sub.Id);
        covered.WasSelected.Should().BeFalse();

        var bothSelected = await Service.Preview([a.Id, sub.Id], Path.Combine(_testRoot, "Dest"));

        // Collapsed into A's row, but still listed — flagged as having been selected.
        bothSelected.Data!.Items.Should().ContainSingle();
        bothSelected.Data!.Items.Single().CoveredResources.Single().WasSelected.Should().BeTrue();
    }

    [TestMethod]
    public async Task MarkInterruptedOnStartup_FlipsPendingAndMovingRecords()
    {
        Db.Set<ResourceMoveRecordDbModel>().AddRange(
            new ResourceMoveRecordDbModel
            {
                BatchId = "b4", ResourceId = 1, SourcePath = "/a", DestPath = "/b/a",
                Status = ResourceMoveRecordStatus.Moving, CreatedAt = DateTime.Now
            },
            new ResourceMoveRecordDbModel
            {
                BatchId = "b4", ResourceId = 2, SourcePath = "/c", DestPath = "/b/c",
                Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now
            },
            new ResourceMoveRecordDbModel
            {
                BatchId = "b5", ResourceId = 3, SourcePath = "/d", DestPath = "/b/d",
                Status = ResourceMoveRecordStatus.Succeeded, CreatedAt = DateTime.Now
            });
        await Db.SaveChangesAsync();

        await Service.MarkInterruptedOnStartup();

        var records = await Db.Set<ResourceMoveRecordDbModel>().AsNoTracking().ToListAsync();
        records.Single(r => r.ResourceId == 1).Status.Should().Be(ResourceMoveRecordStatus.Interrupted);
        records.Single(r => r.ResourceId == 2).Status.Should().Be(ResourceMoveRecordStatus.Interrupted);
        records.Single(r => r.ResourceId == 3).Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
    }

    [TestMethod]
    public void FileSystemHelpers_SameTempDirectory_IsSameFileSystem()
    {
        var a = Dir("FsA");
        var b = Dir("FsB");

        // Two siblings under the test root always share a mount; the helper must not report
        // false (null is acceptable when mounts cannot be resolved).
        ResourceMoveFileSystem.AreOnSameFileSystem(a, b).Should().NotBe(false);
    }

    [TestMethod]
    public void FileSystemHelpers_FilelessTreeIsDeleted_TreeWithFilesIsKept()
    {
        var fileless = Dir("Debris");
        Dir("Debris", "Empty1");
        Dir("Debris", "Empty1", "Empty2");
        ResourceMoveFileSystem.TryDeleteFilelessDirectoryTree(fileless);
        Directory.Exists(fileless).Should().BeFalse();

        var withFile = Dir("Partial");
        Dir("Partial", "Sub");
        File.WriteAllText(Path.Combine(withFile, "Sub", "f.txt"), "x");
        ResourceMoveFileSystem.TryDeleteFilelessDirectoryTree(withFile);
        File.Exists(Path.Combine(withFile, "Sub", "f.txt")).Should().BeTrue();
    }

    [TestMethod]
    public void Guard_OverlapIsSegmentBased_BothDirections()
    {
        var guard = new ResourceMoveGuard();

        guard.TryReserve("g1", [1], ["/media/a"], out _).Should().BeTrue();

        // /media/abc is NOT under /media/a
        guard.TryReserve("g2", [2], ["/media/abc"], out _).Should().BeTrue();

        // /media/a/sub is under the reserved /media/a
        guard.TryReserve("g3", [3], ["/media/a/sub"], out var conflict1).Should().BeFalse();
        conflict1.Should().Be("/media/a");

        // reserving an ancestor of a reserved path also conflicts
        guard.TryReserve("g4", [4], ["/media"], out _).Should().BeFalse();

        guard.Release("g1");
        guard.TryReserve("g5", [5], ["/media/a/sub"], out _).Should().BeTrue();
    }

    [TestMethod]
    public void Guard_DuplicateBatchId_RejectedInsteadOfOverwritten()
    {
        var guard = new ResourceMoveGuard();

        guard.TryReserve("dup", [1], ["/media/a"], out _).Should().BeTrue();

        // A second reservation under the same batch id must not swap out the live one —
        // otherwise a racing Retry could later release the running batch's reservation.
        guard.TryReserve("dup", [2], ["/media/b"], out var conflict).Should().BeFalse();
        conflict.Should().Be("/media/a");

        // The original reservation is intact.
        guard.IsResourceLocked(1).Should().BeTrue();
        guard.IsResourceLocked(2).Should().BeFalse();

        guard.Release("dup");
        guard.TryReserve("dup", [2], ["/media/b"], out _).Should().BeTrue();
    }
    [TestMethod]
    public async Task CreateBatch_IdempotencyAndDuplicateDestinations()
    {
        var source = Dir("A"); var dest = Dir("Dest");
        var a = await SeedResource(source);
        var options = new Bakabase.Abstractions.Models.Domain.ResourceMoveRequestOptions
            { Origin = "move-panel", SourceTabId = "tab-a", IdempotencyKey = "request-1" };
        var first = await Service.CreateBatch([a.Id], dest, options);
        var retry = await Service.CreateBatch([a.Id], dest, options);
        retry.Data!.BatchId.Should().Be(first.Data!.BatchId);
        (await Db.Set<ResourceMoveRecordDbModel>().CountAsync()).Should().Be(1);
        (await Service.CreateBatch([a.Id], Dir("Other"), options)).Code.Should().NotBe(0);
        await Service.CancelBatch(first.Data.BatchId);
        var second = await SeedResource(Dir("Different", "A"));
        (await Service.CreateBatch([a.Id, second.Id], dest)).Code.Should().NotBe(0);
    }

    [TestMethod]
    public async Task CancelQueued_ReleasesReservationAndNeverExecutes()
    {
        var source = Dir("A"); await File.WriteAllTextAsync(Path.Combine(source, "f.txt"), "source");
        var a = await SeedResource(source); var dest = Dir("Dest");
        var batch = (await Service.CreateBatch([a.Id], dest)).Data!;
        var guard = _sp.GetRequiredService<ResourceMoveGuard>();
        guard.IsResourceLocked(a.Id).Should().BeTrue();
        (await Service.CancelBatch(batch.BatchId)).Code.Should().Be(0);
        guard.IsResourceLocked(a.Id).Should().BeFalse();
        await Service.ExecuteBatch(batch.BatchId, FakeArgs(_sp));
        Directory.Exists(source).Should().BeTrue();
        Directory.Exists(Path.Combine(dest, "A")).Should().BeFalse();
        (await Service.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.Cancelled);
    }

    [TestMethod]
    public async Task CancelRunning_FinishesCurrentResourceAndCancelsRemaining()
    {
        var sourceA = Dir("A"); var sourceB = Dir("B"); var dest = Dir("Dest");
        await File.WriteAllTextAsync(Path.Combine(sourceA, "f.txt"), "current");
        await File.WriteAllTextAsync(Path.Combine(sourceB, "f.txt"), "remaining");
        var a = await SeedResource(sourceA); var b = await SeedResource(sourceB);
        var batch = (await Service.CreateBatch([a.Id, b.Id], dest)).Data!;
        var cancelled = false;
        var args = new BTaskArgs(new PauseToken(), CancellationToken.None, new BTaskDomain("test", () => "test"),
            async change =>
            {
                if (cancelled) return;
                var state = new BTaskDomain("test", () => "test"); change(state);
                if (state.Percentage > 0)
                {
                    cancelled = true;
                    await Service.CancelBatch(batch.BatchId);
                }
            }, _sp);
        await Service.ExecuteBatch(batch.BatchId, args);
        var records = await Service.GetRecords();
        records.Single(r => r.ResourceId == a.Id).Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        records.Single(r => r.ResourceId == b.Id).Status.Should().Be(ResourceMoveRecordStatus.Cancelled);
        File.ReadAllText(Path.Combine(dest, "A", "f.txt")).Should().Be("current");
        File.ReadAllText(Path.Combine(sourceB, "f.txt")).Should().Be("remaining");
        _sp.GetRequiredService<ResourceMoveGuard>().IsResourceLocked(a.Id).Should().BeFalse();
    }

    [TestMethod]
    public async Task ConflictResolution_OverwriteSafelyMergesAndPreservesDestinationOnlyFiles()
    {
        var source = Dir("A"); var dest = Dir("Dest"); var occupied = Dir("Dest", "A");
        await File.WriteAllTextAsync(Path.Combine(source, "same.txt"), "new");
        await File.WriteAllTextAsync(Path.Combine(occupied, "same.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(occupied, "keep.txt"), "keep");
        var a = await SeedResource(source);
        var batch = (await Service.CreateBatch([a.Id], dest, new() { Origin = "move-panel" })).Data!;
        await FluentActions.Awaiting(() => Service.ExecuteBatch(batch.BatchId, FakeArgs(_sp))).Should().ThrowAsync<BTaskSuspendedException>();
        var waiting = (await Service.GetRecords()).Single();
        waiting.Status.Should().Be(ResourceMoveRecordStatus.WaitingForConflict);
        _sp.GetRequiredService<ResourceMoveGuard>().IsResourceLocked(a.Id).Should().BeTrue();
        File.ReadAllText(Path.Combine(occupied, "same.txt")).Should().Be("old");
        (await Service.ResolveConflict(waiting.Id, new() { Action = "overwrite", Scope = "batch", ConflictVersion = waiting.ConflictVersion })).Code.Should().Be(0);
        await Service.ExecuteBatch(batch.BatchId, FakeArgs(_sp));
        File.ReadAllText(Path.Combine(occupied, "same.txt")).Should().Be("new");
        File.ReadAllText(Path.Combine(occupied, "keep.txt")).Should().Be("keep");
        Directory.Exists(source).Should().BeFalse();
        (await ResourceService.Get(a.Id))!.Path.Should().Be(occupied.Replace('\\', '/'));
    }

    [TestMethod]
    public async Task IdentityConflict_IsNeverAuthorizedByPanelOverwrite()
    {
        var source = Dir("A"); var dest = Dir("Dest");
        var a = await SeedResource(source); var existing = await SeedResource(Dir("Dest", "A"));
        var batch = (await Service.CreateBatch([a.Id], dest, new() { Origin = "move-panel", ConflictPolicy = "overwrite" })).Data!;
        await FluentActions.Awaiting(() => Service.ExecuteBatch(batch.BatchId, FakeArgs(_sp))).Should().ThrowAsync<BTaskSuspendedException>();
        var record = (await Service.GetRecords()).Single();
        record.ConflictKind.Should().Be("resourceIdentity"); record.CanOverwrite.Should().BeFalse();
        (await Service.ResolveConflict(record.Id, new() { Action = "overwrite", Scope = "panel", ConflictVersion = record.ConflictVersion })).Code.Should().NotBe(0);
        (await ResourceService.Get(a.Id))!.Path.Should().Be(a.Path);
        (await ResourceService.Get(existing.Id))!.Path.Should().Be(existing.Path);
    }

    [TestMethod]
    public async Task Startup_RestoresWaitingAndRecoveryReservations()
    {
        var a = await SeedResource(Dir("A")); var dest = Dir("Dest");
        var batch = (await Service.CreateBatch([a.Id], dest)).Data!;
        var row = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        row.Status = ResourceMoveRecordStatus.Moving; row.PhysicalMoveStarted = true;
        await Db.SaveChangesAsync();
        var guard = _sp.GetRequiredService<ResourceMoveGuard>(); guard.Release(batch.BatchId);
        await Service.MarkInterruptedOnStartup();
        (await Service.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.NeedsRecovery);
        guard.IsResourceLocked(a.Id).Should().BeTrue(); guard.HasRetainedReservations.Should().BeTrue();
        (await Service.DeleteRecord(row.Id)).Code.Should().NotBe(0);
    }

    [TestMethod]
    public async Task SafeCopyFailure_LeavesOriginalTargetAndSourceIntact()
    {
        var source = Path.Combine(Dir("Source"), "file.bin");
        var target = Path.Combine(Dir("Target"), "file.bin");
        await File.WriteAllBytesAsync(source, new byte[2 * 1024 * 1024]);
        await File.WriteAllTextAsync(target, "original target");
        var record = new ResourceMoveExecutionState { Id = 41, SourcePath = source, DestPath = target };
        using var cancellation = new CancellationTokenSource();
        var run = () => ResourceMoveSafeFileSystem.Move(record, () => Task.CompletedTask,
            _ => { cancellation.Cancel(); return Task.CompletedTask; }, new PauseToken(), cancellation.Token, (_, _) => true);
        await run.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(target).Should().Be("original target");
        new FileInfo(source).Length.Should().Be(2 * 1024 * 1024);
        record.MoveJournalJson.Should().NotBeNull();
        await ResourceMoveSafeFileSystem.Move(record, () => Task.CompletedTask, _ => Task.CompletedTask,
            new PauseToken(), CancellationToken.None, (_, _) => true);
        File.Exists(source).Should().BeFalse(); new FileInfo(target).Length.Should().Be(2 * 1024 * 1024);
        ResourceMoveSafeFileSystem.Cleanup(record);
    }

    [TestMethod]
    public async Task RetryCancelled_AfterAnotherMoveRejectsStaleResourceLocation()
    {
        var source = Dir("A"); await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "original");
        var a = await SeedResource(source);
        var oldBatch = (await Service.CreateBatch([a.Id], Dir("Y"))).Data!;
        await Service.CancelBatch(oldBatch.BatchId);
        var newerBatch = (await Service.CreateBatch([a.Id], Dir("Z"))).Data!;
        await Service.ExecuteBatch(newerBatch.BatchId, FakeArgs(_sp));
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "new occupant");
        (await Service.RetryBatch(oldBatch.BatchId)).Code.Should().NotBe(0);
        File.ReadAllText(Path.Combine(source, "file.txt")).Should().Be("new occupant");
        File.ReadAllText(Path.Combine(_testRoot, "Z", "A", "file.txt")).Should().Be("original");
        (await ResourceService.Get(a.Id))!.Path.Should().EndWith("Z/A");
    }

    [TestMethod]
    public async Task SameVolumeDirectory_UsesRenameWithoutCopyOrContentManifest()
    {
        var source = Dir("Large"); var destination = Path.Combine(Dir("Target"), "Large");
        using (var sparse = File.Create(Path.Combine(source, "sparse.bin"))) sparse.SetLength(128L * 1024 * 1024);
        var record = new ResourceMoveExecutionState { Id = 44, SourcePath = source, DestPath = destination };
        await ResourceMoveSafeFileSystem.Move(record, () => Task.CompletedTask, _ => Task.CompletedTask,
            new PauseToken(), CancellationToken.None, (_, _) => false);
        var journal = Newtonsoft.Json.JsonConvert.DeserializeObject<ResourceMoveSafeFileSystem.Journal>(record.MoveJournalJson!)!;
        journal.NativeRename.Should().BeTrue(); journal.Files.Should().BeEmpty();
        new FileInfo(Path.Combine(destination, "sparse.bin")).Length.Should().Be(128L * 1024 * 1024);
        Directory.Exists(source).Should().BeFalse();
        ResourceMoveSafeFileSystem.Cleanup(record);
        Directory.EnumerateFiles(destination).Should().ContainSingle();
    }

    [TestMethod]
    public async Task NativeRename_RecoversAfterPublishingBeforeDatabaseCheckpoint()
    {
        var source = Dir("Crash"); var destination = Path.Combine(Dir("Target"), "Crash");
        await File.WriteAllTextAsync(Path.Combine(source, "file.txt"), "preserved");
        var record = new ResourceMoveExecutionState { Id = 45, SourcePath = source, DestPath = destination };
        string? durableJournal = null;
        Task Save()
        {
            var journal = Newtonsoft.Json.JsonConvert.DeserializeObject<ResourceMoveSafeFileSystem.Journal>(record.MoveJournalJson!)!;
            if (journal.Published) throw new IOException("simulated database outage after rename");
            durableJournal = record.MoveJournalJson;
            return Task.CompletedTask;
        }
        await FluentActions.Awaiting(() => ResourceMoveSafeFileSystem.Move(record, Save, _ => Task.CompletedTask,
            new PauseToken(), CancellationToken.None, (_, _) => false)).Should().ThrowAsync<IOException>();
        record.MoveJournalJson = durableJournal;
        await ResourceMoveSafeFileSystem.Move(record, () => Task.CompletedTask, _ => Task.CompletedTask,
            new PauseToken(), CancellationToken.None, (_, _) => false);
        ResourceMoveSafeFileSystem.Cleanup(record);
        File.ReadAllText(Path.Combine(destination, "file.txt")).Should().Be("preserved");
        Directory.EnumerateFiles(destination).Should().ContainSingle();
    }

    [TestMethod]
    public async Task FixupRecovery_UsesPersistedDescendantPathsAfterDatabaseAlreadyMoved()
    {
        var source = Dir("Parent"); var childPath = Dir("Parent", "Child"); var dest = Dir("Dest");
        await File.WriteAllTextAsync(Path.Combine(childPath, "file.txt"), "child data");
        var parent = await SeedResource(source); var child = await SeedResource(childPath);
        var links = _sp.GetRequiredService<IResourceSourceLinkService>();
        await links.Add(new ResourceSourceLink { ResourceId = child.Id, Source = ResourceSource.PathMark, SourceKey = child.Path! });
        var batch = (await Service.CreateBatch([parent.Id], dest)).Data!;
        var row = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        var state = new ResourceMoveExecutionState { Id = row.Id, SourcePath = row.SourcePath, DestPath = row.DestPath };
        await ResourceMoveSafeFileSystem.Move(state, async () =>
        {
            row.MoveJournalJson = state.MoveJournalJson; row.PhysicalMoveStarted = state.PhysicalMoveStarted;
            await Db.SaveChangesAsync();
        }, _ => Task.CompletedTask, new PauseToken(), CancellationToken.None, (_, _) => false);
        // Simulate the exact durable state after ChangePath committed but cache/link repair failed.
        await ResourceService.ChangePath([parent.Id, child.Id], new System.Collections.Generic.Dictionary<int,string>
        {
            [parent.Id] = row.DestPath, [child.Id] = row.DestPath + "/Child"
        });
        row.Status = ResourceMoveRecordStatus.NeedsRecovery;
        await Db.SaveChangesAsync();
        (await Service.RetryBatch(batch.BatchId)).Code.Should().Be(0);
        await Service.ExecuteBatch(batch.BatchId, FakeArgs(_sp));
        (await Service.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        (await links.GetByResourceId(child.Id)).Single().SourceKey.Should().Be(row.DestPath + "/Child");
        (await ResourceService.Get(child.Id))!.Path.Should().Be(row.DestPath + "/Child");
    }

    [TestMethod]
    public async Task QueuedDestinationDisappears_DoesNotRecreateItOrMoveSource()
    {
        var source = Dir("A"); var dest = Dir("Dest"); var a = await SeedResource(source);
        var batch = (await Service.CreateBatch([a.Id], dest)).Data!;
        Directory.Delete(dest);
        await FluentActions.Awaiting(() => Service.ExecuteBatch(batch.BatchId, FakeArgs(_sp))).Should().ThrowAsync<BTaskException>();
        Directory.Exists(dest).Should().BeFalse(); Directory.Exists(source).Should().BeTrue();
        (await Service.GetRecords()).Single().Status.Should().Be(ResourceMoveRecordStatus.Failed);
    }

    [TestMethod]
    public async Task PreviewFingerprint_ExcludedSelectionsCanSubmitFilteredEffectiveIds()
    {
        var a = await SeedResource(Dir("Eligible")); var child = await SeedResource(Dir("Eligible", "Child"));
        var locked = await SeedResource(Dir("Locked")); var dest = Dir("Dest");
        await ResourceService.AddOrPutRange([new Resource { Path = null, SourceLinks = [
            new ResourceSourceLink { Source = ResourceSource.ExHentai, SourceKey = "preview-virtual" }] }]);
        var remote = (await ResourceService.GetAll()).Single(r => !r.HasLocalPath);
        await Service.CreateBatch([locked.Id], Dir("OtherDestination"));
        var preview = (await Service.Preview([a.Id, child.Id, locked.Id, remote.Id], dest)).Data!;
        preview.SkippedResourceIds.Should().Contain(remote.Id);
        preview.Items.Single(i => i.ResourceId == locked.Id).UnavailableReason.Should().Be("resourceLocked");
        var created = await Service.CreateBatch([a.Id, child.Id], dest,
            new() { Origin = "move-panel", ExpectedPreviewFingerprint = preview.PreviewFingerprint });
        created.Code.Should().Be(0);
        (await Service.GetBatch(created.Data!.BatchId))!.ResourceIds.Should().Equal(a.Id);
    }

    [TestMethod]
    public async Task PreviewFingerprint_NewCoveredResourceRequiresNewConfirmation()
    {
        var a = await SeedResource(Dir("Eligible")); var dest = Dir("Dest");
        var preview = (await Service.Preview([a.Id], dest)).Data!;
        await SeedResource(Dir("Eligible", "NewChild"));
        (await Service.CreateBatch([a.Id], dest, new() { ExpectedPreviewFingerprint = preview.PreviewFingerprint })).Code.Should().NotBe(0);
        (await Service.GetRecords()).Should().BeEmpty();
    }

    [TestMethod]
    public async Task IdempotencyReceipt_RemainsAvailableAfterDestinationGoesOffline()
    {
        var a = await SeedResource(Dir("A")); var dest = Dir("Dest");
        var options = new ResourceMoveRequestOptions { IdempotencyKey = "offline-receipt" };
        var first = (await Service.CreateBatch([a.Id], dest, options)).Data!;
        Directory.Delete(dest);
        var retry = await Service.CreateBatch([a.Id], dest, options);
        retry.Code.Should().Be(0); retry.Data!.BatchId.Should().Be(first.BatchId);
        (await Service.GetRecords()).Should().ContainSingle();
    }

}
