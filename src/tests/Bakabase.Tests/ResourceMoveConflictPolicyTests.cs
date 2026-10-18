using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.ResourceMove;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceMoveConflictPolicyTests
{
    private string _root = null!;
    private IServiceProvider _services = null!;
    private IResourceMoveService Moves => _services.GetRequiredService<IResourceMoveService>();
    private IResourceService Resources => _services.GetRequiredService<IResourceService>();
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private ResourceMoveGuard Guard => _services.GetRequiredService<ResourceMoveGuard>();
    private BTaskArgs Args => new(new PauseToken(), CancellationToken.None,
        new BTask("conflict-policy-test", () => "test"), _ => Task.CompletedTask, _services);

    [TestInitialize]
    public async Task Setup()
    {
        _root = TestServiceBuilder.NewTestDirectory();
        Directory.CreateDirectory(_root);
        _services = await TestServiceBuilder.BuildServiceProvider(_root);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_services is IAsyncDisposable lifetime) await lifetime.DisposeAsync();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string Dir(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task<Resource> Seed(string relative, bool file = false)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (file) await File.WriteAllTextAsync(path, "source");
        else
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "same.txt"), "source");
        }
        await Resources.AddOrPutRange([new Resource { Path = path, IsFile = file }]);
        return (await Resources.GetAll()).Single(resource => resource.Path == path.Replace('\\', '/'));
    }

    [TestMethod]
    public async Task OverwriteExistingDestination_IsAcceptedAndPreservesDestinationOnlyFiles()
    {
        var resource = await Seed("source/A");
        var destination = Dir("destination");
        var occupied = Dir("destination/A");
        await File.WriteAllTextAsync(Path.Combine(occupied, "same.txt"), "old");
        await File.WriteAllTextAsync(Path.Combine(occupied, "keep.txt"), "keep");
        var preview = (await Moves.Preview([resource.Id], destination)).Data!;

        Assert.IsTrue(preview.Items.Single().DestConflict);
        Assert.AreEqual("destinationExists", preview.Items.Single().ConflictKind);
        var response = await Moves.CreateBatch([resource.Id], destination, new()
        {
            Origin = "move-panel", ConflictPolicy = "overwrite", ExpectedPreviewFingerprint = preview.PreviewFingerprint
        });
        Assert.AreEqual(0, response.Code);
        await Moves.ExecuteBatch(response.Data!.BatchId, Args);

        Assert.AreEqual("source", await File.ReadAllTextAsync(Path.Combine(occupied, "same.txt")));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(occupied, "keep.txt")));
        Assert.IsFalse(Directory.Exists(resource.Path));
        Assert.AreEqual("completed", (await Moves.GetBatch(response.Data.BatchId))!.Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SkipExistingDestination_LeavesWholeResourceUntouchedAndContinuesOtherMoves(bool file)
    {
        var conflict = await Seed(file ? "source/A.txt" : "source/A", file);
        var other = await Seed("source/B");
        var destination = Dir("destination");
        var occupied = Path.Combine(destination, Path.GetFileName(conflict.Path));
        if (file) await File.WriteAllTextAsync(occupied, "old");
        else
        {
            Directory.CreateDirectory(occupied);
            await File.WriteAllTextAsync(Path.Combine(occupied, "same.txt"), "old");
            await File.WriteAllTextAsync(Path.Combine(conflict.Path!, "unique.txt"), "leave this too");
        }
        var response = await Moves.CreateBatch([conflict.Id, other.Id], destination,
            new() { ConflictPolicy = "skip" });
        Assert.AreEqual(0, response.Code);
        await Moves.ExecuteBatch(response.Data!.BatchId, Args);

        var batch = (await Moves.GetBatch(response.Data.BatchId))!;
        Assert.AreEqual(1, batch.Counts.Skipped);
        Assert.AreEqual(1, batch.Counts.Succeeded);
        Assert.AreEqual("completed", batch.Status);
        Assert.AreEqual(conflict.Path, (await Resources.Get(conflict.Id))!.Path);
        Assert.AreEqual("source", await File.ReadAllTextAsync(file ? conflict.Path! : Path.Combine(conflict.Path!, "same.txt")));
        Assert.AreEqual("old", await File.ReadAllTextAsync(file ? occupied : Path.Combine(occupied, "same.txt")));
        if (!file)
        {
            Assert.IsTrue(File.Exists(Path.Combine(conflict.Path!, "unique.txt")));
            Assert.IsFalse(File.Exists(Path.Combine(occupied, "unique.txt")));
        }
        Assert.IsTrue(File.Exists(Path.Combine(destination, "B", "same.txt")));
        Assert.IsFalse(Guard.IsResourceLocked(conflict.Id));
        Assert.IsFalse(Guard.IsResourceLocked(other.Id));
    }

    [TestMethod]
    public async Task SkipTypeMismatch_DoesNotDeleteEitherSide()
    {
        var resource = await Seed("source/A", file: true);
        var destination = Dir("destination");
        var occupied = Dir("destination/A");
        await File.WriteAllTextAsync(Path.Combine(occupied, "keep.txt"), "keep");
        var response = await Moves.CreateBatch([resource.Id], destination, new() { ConflictPolicy = "skip" });

        Assert.AreEqual(0, response.Code);
        await Moves.ExecuteBatch(response.Data!.BatchId, Args);

        var record = (await Moves.GetRecords()).Single();
        Assert.AreEqual(ResourceMoveRecordStatus.Skipped, record.Status);
        Assert.AreEqual("typeMismatch", record.ConflictKind);
        Assert.AreEqual("source", await File.ReadAllTextAsync(resource.Path!));
        Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(occupied, "keep.txt")));
    }

    [TestMethod]
    public async Task SkipResourceIdentityConflict_DoesNotMergeResourceRecords()
    {
        var resource = await Seed("source/A");
        var existing = await Seed("destination/A");
        var response = await Moves.CreateBatch([resource.Id], Dir("destination"), new() { ConflictPolicy = "skip" });

        Assert.AreEqual(0, response.Code);
        await Moves.ExecuteBatch(response.Data!.BatchId, Args);

        var record = (await Moves.GetRecords()).Single();
        Assert.AreEqual(ResourceMoveRecordStatus.Skipped, record.Status);
        Assert.AreEqual("resourceIdentity", record.ConflictKind);
        Assert.AreEqual(resource.Path, (await Resources.Get(resource.Id))!.Path);
        Assert.AreEqual(existing.Path, (await Resources.Get(existing.Id))!.Path);
        Assert.IsTrue(File.Exists(Path.Combine(resource.Path!, "same.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(existing.Path!, "same.txt")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SkipDuplicateDestinations_SkipsEveryCollidingSourceWithoutChoosingAWinner(bool includeUnique)
    {
        var first = await Seed("first/A");
        var second = await Seed(includeUnique ? "second/A" : "second/a");
        var unique = includeUnique ? await Seed("third/B") : null;
        var ids = unique == null ? new[] { first.Id, second.Id } : [first.Id, second.Id, unique.Id];
        var destination = Dir("destination");
        var preview = (await Moves.Preview(ids, destination)).Data!;

        Assert.AreEqual(1, preview.DuplicateDestinationPaths.Length);
        Assert.AreEqual(2, preview.Items.Count(item => item.ConflictKind == "batchDestination"));
        var response = await Moves.CreateBatch(ids, destination, new()
        {
            ConflictPolicy = "skip", ExpectedPreviewFingerprint = preview.PreviewFingerprint, IdempotencyKey = "skip-batch"
        });
        Assert.AreEqual(0, response.Code);
        Assert.IsFalse(Guard.IsResourceLocked(first.Id));
        Assert.IsFalse(Guard.IsResourceLocked(second.Id));
        await Moves.ExecuteBatch(response.Data!.BatchId, Args);

        var batch = (await Moves.GetBatch(response.Data.BatchId))!;
        Assert.AreEqual(2, batch.Counts.Skipped);
        Assert.AreEqual(includeUnique ? 1 : 0, batch.Counts.Succeeded);
        Assert.AreEqual("completed", batch.Status);
        Assert.IsFalse(Directory.Exists(Path.Combine(destination, "A")));
        Assert.IsTrue(File.Exists(Path.Combine(first.Path!, "same.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(second.Path!, "same.txt")));
        Assert.AreEqual(first.Path, (await Resources.Get(first.Id))!.Path);
        Assert.AreEqual(second.Path, (await Resources.Get(second.Id))!.Path);
        if (includeUnique) Assert.IsTrue(File.Exists(Path.Combine(destination, "B", "same.txt")));
        var repeat = await Moves.CreateBatch(ids, destination,
            new() { ConflictPolicy = "skip", IdempotencyKey = "skip-batch" });
        Assert.AreEqual(0, repeat.Code);
        Assert.AreEqual(response.Data.BatchId, repeat.Data!.BatchId);
        Assert.AreEqual(ids.Length, await Db.ResourceMoveRecords.CountAsync());
        Assert.AreNotEqual(0, (await Moves.CreateBatch(ids, destination,
            new() { ConflictPolicy = "overwrite", IdempotencyKey = "skip-batch" })).Code);
    }

    [TestMethod]
    [DataRow("inherit")]
    [DataRow("ask")]
    [DataRow("overwrite")]
    public async Task OtherPolicies_RejectDuplicateDestinations(string policy)
    {
        var first = await Seed("first/A");
        var second = await Seed("second/A");
        var response = await Moves.CreateBatch([first.Id, second.Id], Dir("destination"),
            new() { ConflictPolicy = policy });

        Assert.AreNotEqual(0, response.Code);
        Assert.IsTrue(response.Message!.Contains("batchDestination"));
        Assert.AreEqual(0, await Db.ResourceMoveRecords.CountAsync());
        Assert.IsFalse(Guard.IsResourceLocked(first.Id));
        Assert.IsFalse(Guard.IsResourceLocked(second.Id));
    }

    [TestMethod]
    public async Task SkipPolicy_DoesNotReleaseAnAlreadyStartedMoveAsSkipped()
    {
        if (_services is IAsyncDisposable lifetime) await lifetime.DisposeAsync();
        _services = await TestServiceBuilder.BuildServiceProvider(_root, services =>
        {
            services.RemoveAll<IResourceMoveExecutor>();
            services.AddSingleton<IResourceMoveExecutor, ConflictAfterCheckpointExecutor>();
        });
        var resource = await Seed("source/A");
        var response = await Moves.CreateBatch([resource.Id], Dir("destination"), new() { ConflictPolicy = "skip" });
        var row = await Db.ResourceMoveRecords.SingleAsync();

        await Assert.ThrowsExceptionAsync<BTaskSuspendedException>(() => Moves.ExecuteBatch(response.Data!.BatchId, Args));

        Assert.AreEqual(ResourceMoveRecordStatus.WaitingForConflict, row.Status);
        Assert.IsTrue(row.PhysicalMoveStarted);
        Assert.AreEqual("owned-checkpoint", row.MoveJournalJson);
        Assert.AreEqual("destinationChanged", row.ConflictKind);
        Assert.IsTrue(Guard.IsResourceLocked(resource.Id));
        Assert.AreEqual("waiting", (await Moves.GetBatch(response.Data!.BatchId))!.Status);
        Assert.AreNotEqual(0, (await Moves.ResolveConflict(row.Id, new()
        {
            Action = "skip", Scope = "once", ConflictVersion = row.ConflictVersion
        })).Code);
    }

    private sealed class ConflictAfterCheckpointExecutor : IResourceMoveExecutor
    {
        public string Id => "local-files";
        public int Version => 1;
        public async Task ExecuteOrResumeAsync(ResourceMoveExecutionState state, Func<Task> checkpoint,
            Func<int, Task> progress, PauseToken pause, CancellationToken cancellation,
            Func<string, string, bool> authorized)
        {
            state.PhysicalMoveStarted = true;
            state.MoveJournalJson = "owned-checkpoint";
            await checkpoint();
            throw new ResourceMoveConflictException("destinationChanged", state.DestPath, false, "changed");
        }
        public void Cleanup(ResourceMoveExecutionState state) { }
    }
}
