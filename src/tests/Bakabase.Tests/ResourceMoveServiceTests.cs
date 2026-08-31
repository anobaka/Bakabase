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
    public async Task ExecuteBatch_DestinationOccupied_FailsRecordButContinuesOthers()
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
        await act.Should().ThrowAsync<BTaskException>();

        var records = await Db.Set<ResourceMoveRecordDbModel>().ToListAsync();
        records.Single(r => r.ResourceId == a.Id).Status.Should().Be(ResourceMoveRecordStatus.Failed);
        records.Single(r => r.ResourceId == b.Id).Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        File.Exists(Path.Combine(destDir, "B", "f.txt")).Should().BeTrue();
        (await ResourceService.Get(a.Id))!.Path.Should().Be(a.Path, "a failed record must not rewrite the path");
    }

    [TestMethod]
    public async Task ExecuteBatch_SourceGoneDestExists_TreatedAsAlreadyMoved()
    {
        // Simulates retrying an interrupted record whose files already landed: source is gone,
        // destination exists, but the DB still points at the old path.
        var destDir = Dir("Dest");
        Dir("Dest", "A");
        var sourcePath = $"{_testRoot.Replace('\\', '/')}/A"; // never created on disk
        var a = await SeedResource(sourcePath);

        Db.Set<ResourceMoveRecordDbModel>().Add(new ResourceMoveRecordDbModel
        {
            BatchId = "b3", ResourceId = a.Id, SourcePath = a.Path,
            DestPath = $"{destDir.Replace('\\', '/')}/A",
            Status = ResourceMoveRecordStatus.Pending, CreatedAt = DateTime.Now, Attempts = 1
        });
        await Db.SaveChangesAsync();

        await Service.ExecuteBatch("b3", FakeArgs(_sp));

        var record = await Db.Set<ResourceMoveRecordDbModel>().SingleAsync();
        record.Status.Should().Be(ResourceMoveRecordStatus.Succeeded);
        record.Attempts.Should().Be(2);
        (await ResourceService.Get(a.Id))!.Path.Should().EndWith("Dest/A");
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
}
