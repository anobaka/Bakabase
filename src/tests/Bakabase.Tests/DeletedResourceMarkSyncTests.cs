using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Tasks;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class DeletedResourceMarkSyncTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private BarrierIndex _index = null!;
    private IPathMarkService Marks => _services.GetRequiredService<IPathMarkService>();
    private IPathMarkEffectService Effects => _services.GetRequiredService<IPathMarkEffectService>();
    private IResourceService Resources => _services.GetRequiredService<IResourceService>();

    private sealed class BarrierIndex : IResourceSearchIndexService
    {
        public Func<CancellationToken, Task>? Barrier;
        public int Waits;
        public bool IsReady => true;
        public long Version => 0;
        public DateTime LastUpdatedAt => DateTime.MinValue;
        public Task<HashSet<int>?> SearchResourceIdsAsync(ResourceSearchFilterGroup? group) => Task.FromResult<HashSet<int>?>(null);
        public Task<Dictionary<string,int>?> GetPropertyValueResourceCountsAsync(PropertyPool pool, int propertyId,
            IEnumerable<string> ids, IReadOnlySet<int>? resources = null) => Task.FromResult<Dictionary<string,int>?>(null);
        public void InvalidateResource(int id) { }
        public void InvalidateResources(IEnumerable<int> ids) { }
        public void RemoveResource(int id) { }
        public void RemoveResources(IEnumerable<int> ids) { }
        public async Task WaitForPendingUpdatesAsync(CancellationToken ct = default)
        {
            Waits++;
            ct.ThrowIfCancellationRequested();
            if (Barrier != null) await Barrier(ct);
        }
        public Task RebuildAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task WaitForReadyAsync(TimeSpan? timeout = null) => Task.CompletedTask;
        public ResourceSearchIndexStatus GetStatus() => new() {IsReady = true};
    }

    private sealed class FailedRemovalOrm(IServiceProvider services)
        : FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>(services)
    {
        public bool RejectRemoval = true;
        public override async Task<BaseResponse> RemoveByKeys(IEnumerable<int> keys)
        {
            if (!RejectRemoval) return await base.RemoveByKeys(keys);
            var cache = await GetCacheVault();
            foreach (var key in keys) cache.TryRemove(key, out _);
            return BaseResponseBuilder.NotModified;
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "deleted-mark-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _index = new BarrierIndex();
        _services = await TestServiceBuilder.BuildServiceProvider(s => s.AddSingleton<IResourceSearchIndexService>(_index));
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    private Task<PathMark> AddMark(string root, int layer = 1, bool boundary = false) => Marks.Add(new PathMark
    {
        Path = root, Type = PathMarkType.Resource,
        ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = layer, ApplyScope = PathMarkApplyScope.MatchedOnly,
            FsTypeFilter = PathFilterFsType.Directory, IsResourceBoundary = boundary
        })
    });

    private async Task<Resource> AddResource(string path, bool otherSource = false, string? oldKey = null, bool noSource = false)
    {
        var links = noSource ? new List<ResourceSourceLink>() :
            new List<ResourceSourceLink> {new() {Source = ResourceSource.PathMark, SourceKey = oldKey ?? path}};
        if (otherSource) links.Add(new() {Source = ResourceSource.Steam, SourceKey = "fixture-game"});
        await Resources.AddOrPutRange([new Resource {Path = path, IsFile = false, SourceLinks = links}]);
        return (await Resources.GetAll()).Single(r => r.Path == path.Replace('\\', '/'));
    }

    private Task Sync(Func<int, Task>? progress = null, CancellationToken ct = default) =>
        _services.GetRequiredService<ResourceSyncService>().SyncResources(ResourceSource.PathMark,
            progress, null, new PauseToken(), ct);

    [TestMethod]
    [DataRow("nas")]
    [DataRow("windows")]
    public async Task MissingStorageRetractsPersistedResourcesAndFinalizesMarksAndLedger(string kind)
    {
        var path = kind == "windows" ? "E:/迅雷下载" : Path.Combine(_root, "missing-nas", "Anime");
        var mark = await AddMark(path);
        var resource = await AddResource(path + "/Episode");
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = resource.Path!}]);
        await Marks.SoftDelete(mark.Id);
        Assert.AreEqual(1, await Marks.GetPendingCount());
        await Sync();
        Assert.AreEqual(0, (await Resources.GetAll()).Count);
        Assert.IsNull(await Marks.Get(mark.Id));
        Assert.AreEqual(0, await Marks.GetPendingCount());
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
        Assert.AreEqual(1, _index.Waits);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SyncAllRoutesDeletionToRealResourceTaskAndReportsItsOutcome(bool malformed)
    {
        var mark = await AddMark(Path.Combine(_root, "missing-nas"));
        if (malformed)
        {
            mark.ConfigJson = "{\"matchMode\":2,\"regex\":\"[\",\"applyScope\":1}";
            await Marks.Update(mark);
        }
        await Marks.SoftDelete(mark.Id);
        var tasks = _services.GetRequiredService<BTaskManager>();
        var manager = new PathSyncManager(NullLogger<PathSyncManager>.Instance, _services, tasks,
            _services.GetRequiredService<IBakabaseLocalizer>());
        await manager.EnqueueSync();
        await (Task)typeof(PathSyncManager).GetMethod("ProcessQueuesIfNeeded", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(manager, [CancellationToken.None])!;
        Assert.IsNotNull(tasks.GetTaskViewModel("SyncResources"));
        Assert.IsNull(tasks.GetTaskViewModel("SyncPathMarks"), "A property-only no-op must not stand in for resource cleanup.");
        await tasks.Start("SyncResources");
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (tasks.GetTaskViewModel("SyncResources")!.Status is BTaskStatus.Running or BTaskStatus.NotStarted)
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Resource sync did not settle.");
            await Task.Delay(10);
        }
        var task = tasks.GetTaskViewModel("SyncResources")!;
        if (malformed)
        {
            Assert.AreEqual(BTaskStatus.Error, task.Status);
            Assert.AreNotEqual(100, task.Percentage);
            Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        }
        else
        {
            Assert.AreEqual(BTaskStatus.Completed, task.Status);
            Assert.IsNull(await Marks.Get(mark.Id));
            Assert.AreEqual(0, await Marks.GetPendingCount());
        }
    }

    [TestMethod]
    public async Task CleanupProtectsSharedCoverageOtherSourcesBoundariesAndSiblingPrefixesAndMovedPaths()
    {
        var anime = Path.Combine(_root, "Anime");
        var deleting = await AddMark(anime);
        await AddMark(anime + "/Shared", layer: 0);
        await AddMark(anime + "/Boundary", layer: 2, boundary: true);
        var kept = new[]
        {
            await AddResource(anime + "/Shared"),
            await AddResource(anime + "/Mixed", otherSource: true),
            await AddResource(anime + "/Boundary"),
            await AddResource(Path.Combine(_root, "AnimeMovie", "Film")),
            await AddResource(Path.Combine(_root, "Moved", "Episode"), oldKey: anime + "/OldEpisode")
        };
        var removed = await AddResource(anime + "/Unshared", oldKey: "Y:/legacy-downloads/Unshared");
        await Effects.AddResourceEffects([
            new() {MarkId = deleting.Id, Path = removed.Path!},
            new() {MarkId = deleting.Id, Path = anime + "/OldEpisode"}
        ]);
        await Marks.SoftDelete(deleting.Id);
        await Sync();
        CollectionAssert.AreEquivalent(kept.Select(r => r.Id).ToArray(), (await Resources.GetAll()).Select(r => r.Id).ToArray());
        Assert.IsNull(await Marks.Get(deleting.Id));
    }

    [TestMethod]
    public async Task LegacyLedgerRetractsResourcesWithoutSourceLinksButUnknownOwnershipIsPreserved()
    {
        var root = Path.Combine(_root, "Legacy");
        var deleting = await AddMark(root);
        var owned = await AddResource(root + "/Owned", noSource: true);
        var unknown = await AddResource(root + "/Unknown", noSource: true);
        await Effects.AddResourceEffects([new() {MarkId = deleting.Id, Path = owned.Path!}]);
        await Marks.SoftDelete(deleting.Id);
        await Sync();
        Assert.AreEqual(unknown.Id, (await Resources.GetAll()).Single().Id);
        Assert.IsNull(await Marks.Get(deleting.Id));
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(deleting.Id)).Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BoundaryAboveOrEqualToDeletedRuleDoesNotVetoItsExplicitOwnership(bool sameRoot)
    {
        var root = Path.Combine(_root, "Anime");
        var deletedRoot = sameRoot ? root : root + "/Series";
        await AddMark(root, layer: 0, boundary: true);
        var deleting = await AddMark(deletedRoot);
        var resource = await AddResource(deletedRoot + "/Episode");
        await Effects.AddResourceEffects([new() {MarkId = deleting.Id, Path = resource.Path!}]);
        await Marks.SoftDelete(deleting.Id);
        await Sync();
        Assert.AreEqual(0, (await Resources.GetAll()).Count);
        Assert.IsNull(await Marks.Get(deleting.Id));
    }

    [TestMethod]
    public async Task ResourceMovedDuringDiscoveryIsNotDeletedUsingItsOldSnapshotOrSourceKey()
    {
        var root = Path.Combine(_root, "Anime");
        var mark = await AddMark(root);
        var resource = await AddResource(root + "/Episode");
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = resource.Path!}]);
        await Marks.SoftDelete(mark.Id);
        var movedPath = Path.Combine(_root, "Moved", "Episode");
        await Sync(async progress =>
        {
            if (progress != 80) return;
            resource.Path = movedPath;
            await Resources.AddOrPutRange([resource]);
        });
        Assert.AreEqual(movedPath, (await Resources.GetAll()).Single().Path);
        Assert.IsNull(await Marks.Get(mark.Id));
    }

    [TestMethod]
    public async Task EditedDeletionSnapshotKeepsItsResourcesMarkAndLedgerForTheNextRun()
    {
        var root = Path.Combine(_root, "Anime");
        var mark = await AddMark(root);
        var resource = await AddResource(root + "/Episode");
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = resource.Path!}]);
        await Marks.SoftDelete(mark.Id);
        await Sync(async progress =>
        {
            if (progress != 80) return;
            var edited = (await Marks.Get(mark.Id))!;
            edited.Priority++;
            await Marks.Update(edited);
        });
        Assert.AreEqual(resource.Id, (await Resources.GetAll()).Single().Id);
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(1, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }

    [TestMethod]
    public async Task RetryAfterIndexFailureFinalizesRetainedLedgerWithoutNeedingResourcesToStillExist()
    {
        var root = Path.Combine(_root, "Legacy");
        var mark = await AddMark(root);
        var resource = await AddResource(root + "/Episode", noSource: true);
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = resource.Path!}]);
        await Marks.SoftDelete(mark.Id);
        _index.Barrier = _ => throw new InvalidOperationException("fixture failure after metadata cleanup");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Sync());
        Assert.AreEqual(0, (await Resources.GetAll()).Count);
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(1, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
        _index.Barrier = null;
        await Sync();
        Assert.IsNull(await Marks.Get(mark.Id));
        Assert.AreEqual(0, await Marks.GetPendingCount());
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }

    [TestMethod]
    public async Task FailedRetirementProofWriteKeepsResourceSourcesAndDeletionPending()
    {
        var mark = await AddMark(Path.Combine(_root, "missing"));
        var resource = await AddResource(mark.Path + "/Episode");
        await Marks.SoftDelete(mark.Id);
        var db = _services.GetRequiredService<BakabaseDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_retirement_proof BEFORE INSERT ON ResourceMarkEffects
            BEGIN SELECT RAISE(ABORT, 'fixture proof persistence failure'); END;
            """);
        var progress = new List<int>();
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => Sync(p =>
        {
            progress.Add(p);
            return Task.CompletedTask;
        }));
        Assert.IsFalse(progress.Contains(100));
        Assert.AreEqual(resource.Id, (await Resources.GetAll()).Single().Id);
        Assert.AreEqual(1, (await _services.GetRequiredService<IResourceSourceLinkService>().GetByResourceId(resource.Id)).Count);
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
    }

    [TestMethod]
    public async Task FailedResourceDeletionRetainsDurableOwnershipAfterSourcesRemovedAndCanRetry()
    {
        var mark = await AddMark(Path.Combine(_root, "missing"));
        var resource = await AddResource(mark.Path + "/Episode");
        await Marks.SoftDelete(mark.Id);
        var db = _services.GetRequiredService<BakabaseDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_resource_deletion BEFORE DELETE ON ResourcesV2
            BEGIN SELECT RAISE(ABORT, 'fixture resource deletion failure'); END;
            """);
        var progress = new List<int>();
        await Assert.ThrowsExactlyAsync<DbUpdateException>(() => Sync(p =>
        {
            progress.Add(p);
            return Task.CompletedTask;
        }));
        Assert.IsFalse(progress.Contains(100));
        Assert.AreEqual(resource.Id, (await Resources.GetAll()).Single().Id, "Failed deletion must restore the ORM cache from persisted rows.");
        Assert.AreEqual(0, (await _services.GetRequiredService<IResourceSourceLinkService>().GetByResourceId(resource.Id)).Count);
        Assert.AreEqual(resource.Path, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Single().Path);
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(0, _index.Waits);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_resource_deletion;");
        await Sync();
        Assert.AreEqual(0, (await Resources.GetAll()).Count);
        Assert.IsNull(await Marks.Get(mark.Id));
        Assert.AreEqual(0, await Marks.GetPendingCount());
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }

    [TestMethod]
    public async Task FailedRemovalResponseReloadsEvictedResourceAndRetainsDeletionForRetry()
    {
        (_services as IDisposable)?.Dispose();
        _services = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.AddSingleton<IResourceSearchIndexService>(_index);
            s.AddSingleton<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>(
                services => new FailedRemovalOrm(services));
        });
        var mark = await AddMark(Path.Combine(_root, "missing"));
        var resource = await AddResource(mark.Path + "/Episode");
        await Marks.SoftDelete(mark.Id);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Sync());
        Assert.AreEqual(resource.Id, (await Resources.GetAll()).Single().Id, "The cached absence must not conceal a failed database deletion.");
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(resource.Path, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Single().Path);
        Assert.AreEqual(0, _index.Waits);
        ((FailedRemovalOrm)_services.GetRequiredService<FullMemoryCacheResourceService<BakabaseDbContext, ResourceDbModel, int>>()).RejectRemoval = false;
        await Sync();
        Assert.AreEqual(0, (await Resources.GetAll()).Count);
        Assert.IsNull(await Marks.Get(mark.Id));
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }

    [TestMethod]
    public async Task CurrentPathLedgerOwnershipRetractsHistoricalRuleButActiveLedgerProtectsResource()
    {
        var deleting = await AddMark(Path.Combine(_root, "new-rule"));
        var active = await AddMark(Path.Combine(_root, "other-rule"));
        var removed = await AddResource(Path.Combine(_root, "historical", "owned"));
        var kept = await AddResource(Path.Combine(_root, "historical", "shared"));
        await Effects.AddResourceEffects([
            new() {MarkId = deleting.Id, Path = removed.Path!},
            new() {MarkId = deleting.Id, Path = kept.Path!},
            new() {MarkId = active.Id, Path = kept.Path!}
        ]);
        await Marks.SoftDelete(deleting.Id);
        await Sync();
        Assert.AreEqual(kept.Id, (await Resources.GetAll()).Single().Id);
        Assert.AreEqual(1, (await Effects.GetResourceEffectsByMarkId(active.Id)).Count);
        Assert.AreEqual(0, (await Effects.GetResourceEffectsByMarkId(deleting.Id)).Count);
    }

    [TestMethod]
    public async Task MetadataCleanupNeverRemovesMediaFiles()
    {
        var path = Path.Combine(_root, "Media", "Episode");
        Directory.CreateDirectory(path);
        var file = Path.Combine(path, "keep.txt");
        await File.WriteAllTextAsync(file, "media must survive");
        var mark = await AddMark(Path.Combine(_root, "Media"));
        await AddResource(path);
        await Marks.SoftDelete(mark.Id);
        await Sync();
        Assert.AreEqual("media must survive", await File.ReadAllTextAsync(file));
        Assert.AreEqual(0, (await Resources.GetAll()).Count);
        Assert.IsNull(await Marks.Get(mark.Id));
    }

    [TestMethod]
    public async Task MalformedDeletionDoesNotCompleteOrLoseMarkAndEffects()
    {
        var mark = await AddMark(Path.Combine(_root, "missing"));
        mark.ConfigJson = "{\"matchMode\":2,\"regex\":\"[\",\"applyScope\":1}";
        await Marks.Update(mark);
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = mark.Path + "/Episode"}]);
        await Marks.SoftDelete(mark.Id);
        var progress = new List<int>();
        await Assert.ThrowsExactlyAsync<RegexParseException>(() => Sync(p => {progress.Add(p); return Task.CompletedTask;}));
        Assert.IsFalse(progress.Contains(100));
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(1, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelOrIndexFailureRetainsDeletionSnapshotAndLedger(bool cancel)
    {
        var mark = await AddMark(Path.Combine(_root, "missing"));
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = mark.Path + "/Episode"}]);
        await Marks.SoftDelete(mark.Id);
        var progress = new List<int>();
        using var cts = new CancellationTokenSource();
        _index.Barrier = ct =>
        {
            if (cancel) {cts.Cancel(); ct.ThrowIfCancellationRequested();}
            throw new InvalidOperationException("fixture index failure");
        };
        try
        {
            await Sync(p => {progress.Add(p); return Task.CompletedTask;}, cts.Token);
            Assert.Fail("An interrupted or failed cleanup must not complete.");
        }
        catch (Exception e) when (cancel ? e is OperationCanceledException : e is InvalidOperationException) { }
        Assert.IsFalse(progress.Contains(100));
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(1, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }

    [TestMethod]
    public async Task ConcurrentRestoreKeepsMarkAndLedgerAndLateStatusTransitionsCannotEraseDeletion()
    {
        var mark = await AddMark(Path.Combine(_root, "missing"));
        await Effects.AddResourceEffects([new() {MarkId = mark.Id, Path = mark.Path + "/Episode"}]);
        await Marks.SoftDelete(mark.Id);
        await Marks.MarkAsSyncing(mark.Id);
        await Marks.MarkAsSynced(mark.Id);
        await Marks.MarkAsSyncedBatch([mark.Id]);
        await Marks.MarkAsFailed(mark.Id, "late error");
        await Marks.MarkAsPending(mark.Id);
        Assert.AreEqual(PathMarkSyncStatus.PendingDelete, (await Marks.Get(mark.Id))!.SyncStatus);
        _index.Barrier = async _ =>
        {
            var restored = (await Marks.Get(mark.Id))!;
            restored.IsDeleted = false;
            restored.SyncStatus = PathMarkSyncStatus.Pending;
            restored.Priority++;
            await Marks.Update(restored);
        };
        await Sync();
        Assert.IsFalse((await Marks.Get(mark.Id))!.IsDeleted);
        Assert.AreEqual(PathMarkSyncStatus.Pending, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual(1, (await Effects.GetResourceEffectsByMarkId(mark.Id)).Count);
    }
}
