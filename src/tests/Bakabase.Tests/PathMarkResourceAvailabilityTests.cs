using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class PathMarkResourceAvailabilityTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private string _media = null!;
    private readonly StoragePolicy _storage = new();
    private readonly BarrierIndex _index = new();
    private IPathMarkService Marks => _services.GetRequiredService<IPathMarkService>();
    private IResourceService Resources => _services.GetRequiredService<IResourceService>();
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();

    private sealed class StoragePolicy : IUserStoragePolicy
    {
        public Func<string, bool> Allowed = _ => true;
        public Action<string>? OnCheck;
        public bool IsRestricted => true;
        public IReadOnlyList<UserStorageRoot> GetRoots(UserStoragePurpose purpose = UserStoragePurpose.UserFiles) => [];
        public bool IsPathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles)
        {
            OnCheck?.Invoke(path);
            return Allowed(path);
        }
        public void EnsurePathAllowed(string path, UserStoragePurpose purpose = UserStoragePurpose.UserFiles) { }
        public void EnsureTreeMutationAllowed(string path) { }
    }

    private sealed class BarrierIndex : IResourceSearchIndexService
    {
        public Func<Task>? Barrier;
        public int Waits;
        public bool IsReady => true;
        public long Version => 0;
        public DateTime LastUpdatedAt => DateTime.MinValue;
        public Task<HashSet<int>?> SearchResourceIdsAsync(ResourceSearchFilterGroup? group) => Task.FromResult<HashSet<int>?>(null);
        public Task<Dictionary<string, int>?> GetPropertyValueResourceCountsAsync(PropertyPool pool, int propertyId,
            IEnumerable<string> ids, IReadOnlySet<int>? resources = null) => Task.FromResult<Dictionary<string, int>?>(null);
        public void InvalidateResource(int id) { }
        public void InvalidateResources(IEnumerable<int> ids) { }
        public void RemoveResource(int id) { }
        public void RemoveResources(IEnumerable<int> ids) { }
        public async Task WaitForPendingUpdatesAsync(CancellationToken ct = default)
        {
            Waits++;
            ct.ThrowIfCancellationRequested();
            if (Barrier != null) await Barrier();
        }
        public Task RebuildAllAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task WaitForReadyAsync(TimeSpan? timeout = null) => Task.CompletedTask;
        public ResourceSearchIndexStatus GetStatus() => new() {IsReady = true};
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "path-mark-availability-" + Guid.NewGuid().ToString("N"));
        _media = Path.Combine(_root, "media");
        Directory.CreateDirectory(_media);
        _services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "app"), services =>
        {
            services.AddSingleton<IUserStoragePolicy>(_storage);
            services.AddSingleton<IResourceSearchIndexService>(_index);
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    private Task<PathMark> AddMark(string? root = null, int layer = 1,
        PathFilterFsType? fsType = PathFilterFsType.Directory, List<string>? extensions = null) => Marks.Add(new PathMark
    {
        Path = root ?? _media, Type = PathMarkType.Resource,
        ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = layer, ApplyScope = PathMarkApplyScope.MatchedOnly,
            FsTypeFilter = fsType, Extensions = extensions
        })
    });

    private async Task<Resource> AddResource(string? path, ResourceStatus status = ResourceStatus.Active,
        bool isFile = false, params ResourceSource[] sources)
    {
        await Resources.AddOrPutRange([new Resource
        {
            Path = path, IsFile = isFile, Status = status,
            SourceLinks = sources.Select(source => new ResourceSourceLink
                {Source = source, SourceKey = path ?? "unmaterialized"}).ToList()
        }]);
        return (await Resources.GetAll()).Single(resource => resource.Path == path);
    }

    private Task Sync(Func<int, Task>? progress = null, CancellationToken ct = default) =>
        _services.GetRequiredService<ResourceSyncService>().SyncResources(ResourceSource.PathMark,
            progress, null, new PauseToken(), ct);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeletedResourceBecomesAbsentAndReappearingResourceBecomesActiveWithoutLosingMetadata(bool isFile)
    {
        var path = Path.Combine(_media, isFile ? "work.mp4" : "work");
        if (isFile) await File.WriteAllTextAsync(path, "fixture");
        else Directory.CreateDirectory(path);
        var mark = await AddMark(fsType: isFile ? PathFilterFsType.File : PathFilterFsType.Directory);
        await Sync();
        var resource = (await Resources.GetAll()).Single();
        Db.CustomProperties.Add(new CustomPropertyDbModel {Id = 711, Name = "status", Type = PropertyType.SingleLineText});
        Db.CustomPropertyValues.Add(new CustomPropertyValueDbModel
            {ResourceId = resource.Id, PropertyId = 711, Scope = (int)PropertyValueScope.Manual, Value = "TBD"});
        Db.ReservedPropertyValues.Add(new Bakabase.Abstractions.Models.Db.ReservedPropertyValue
            {ResourceId = resource.Id, Scope = (int)PropertyValueScope.Manual, Introduction = "keep me", CoverPaths = "[\"cover.jpg\"]"});
        Db.Enhancements.Add(new EnhancementDbModel
            {ResourceId = resource.Id, EnhancerId = 71, Target = 1, ValueType = StandardValueType.String, Value = "metadata", Key = "fixture"});
        await Db.SaveChangesAsync();
        var metadata = await MetadataSnapshot(resource.Id);
        var events = new List<int>();
        _services.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += args => events.AddRange(args.ResourceIds);
        if (isFile) File.Delete(path);
        else Directory.Delete(path);
        await Marks.MarkAsPending(mark.Id);
        _index.Barrier = async () =>
        {
            Assert.AreEqual(ResourceStatus.Absent, (await Resources.GetAll()).Single().Status,
                "The ORM cache must be refreshed before the search-index completion barrier.");
            Assert.IsTrue(events.Contains(resource.Id), "Status changes must publish a resource update.");
        };
        var progress = new List<int>();
        await Sync(value => {progress.Add(value); return Task.CompletedTask;});
        var absent = (await Resources.GetAll()).Single();
        Assert.AreEqual(resource.Id, absent.Id);
        Assert.AreEqual(path, absent.Path);
        Assert.AreEqual(ResourceStatus.Absent, absent.Status);
        Assert.IsTrue(absent.HasLocalPath, "A missing file keeps its local binding.");
        Assert.AreEqual(metadata, await MetadataSnapshot(resource.Id));
        Assert.AreEqual(100, progress.Last());
        Assert.AreEqual(2, _index.Waits);
        _index.Barrier = null;
        if (isFile) await File.WriteAllTextAsync(path, "restored");
        else Directory.CreateDirectory(path);
        await Marks.MarkAsPending(mark.Id);
        await Sync();
        var active = (await Resources.GetAll()).Single();
        Assert.AreEqual(resource.Id, active.Id);
        Assert.AreEqual(ResourceStatus.Active, active.Status);
        Assert.AreEqual(metadata, await MetadataSnapshot(resource.Id));
    }

    private async Task<string> MetadataSnapshot(int resourceId) => JsonConvert.SerializeObject(new
    {
        Custom = await Db.CustomPropertyValues.AsNoTracking().Where(row => row.ResourceId == resourceId).ToListAsync(),
        Reserved = await Db.ReservedPropertyValues.AsNoTracking().Where(row => row.ResourceId == resourceId).ToListAsync(),
        Enhancements = await Db.Enhancements.AsNoTracking().Where(row => row.ResourceId == resourceId).ToListAsync(),
        Sources = await Db.ResourceSourceLinks.AsNoTracking().Where(row => row.ResourceId == resourceId).ToListAsync()
    });

    [TestMethod]
    public async Task OtherOriginsUnknownOwnershipUnboundPathsAndUnmatchedPathsKeepTheirStatuses()
    {
        await AddMark();
        var untouched = new[]
        {
            await AddResource(Path.Combine(_media, "mixed"), sources: [ResourceSource.PathMark, ResourceSource.Steam]),
            await AddResource(Path.Combine(_media, "steam"), sources: [ResourceSource.Steam]),
            await AddResource(Path.Combine(_media, "unknown")),
            await AddResource(null, sources: [ResourceSource.PathMark]),
            await AddResource(Path.Combine(_root, "elsewhere", "moved"), sources: [ResourceSource.PathMark]),
            await AddResource(Path.Combine(_media, "nested", "out-of-layer"), sources: [ResourceSource.PathMark])
        };
        var absent = await AddResource(Path.Combine(_media, "missing"), sources: [ResourceSource.PathMark]);
        await Sync();
        var current = (await Resources.GetAll()).ToDictionary(resource => resource.Id);
        foreach (var resource in untouched) Assert.AreEqual(resource.Status, current[resource.Id].Status);
        Assert.AreEqual(ResourceStatus.Absent, current[absent.Id].Status);
        Assert.AreEqual(untouched.Length + 1, current.Count);
    }

    [TestMethod]
    [DataRow("offline-root")]
    [DataRow("denied-root")]
    [DataRow("denied-parent")]
    [DataRow("missing-parent")]
    [DataRow("locked")]
    public async Task MissingOrUnauthorizedStorageAndReservedMovesCannotProveAbsence(string reason)
    {
        var nested = reason is "denied-parent" or "missing-parent";
        var mark = await AddMark(layer: nested ? 2 : 1);
        var parent = nested ? Path.Combine(_media, "parent") : _media;
        if (reason == "denied-parent") Directory.CreateDirectory(parent);
        var resource = await AddResource(Path.Combine(parent, "missing"), sources: [ResourceSource.PathMark]);
        if (reason == "offline-root") Directory.Delete(_media);
        if (reason == "denied-root") _storage.Allowed = path => path != _media;
        if (reason == "denied-parent") _storage.Allowed = path => path != parent;
        if (reason == "locked")
            Assert.IsTrue(_services.GetRequiredService<ResourceMoveGuard>().TryReserve("fixture", [resource.Id], [resource.Path], out _));
        await Sync();
        Assert.AreEqual(ResourceStatus.Active, (await Resources.GetAll()).Single().Status);
        Assert.AreEqual(PathMarkSyncStatus.Synced, (await Marks.Get(mark.Id))!.SyncStatus);
    }

    [TestMethod]
    public async Task OnlyMarksCompletedInThisSyncAndTheirExtensionFiltersCanChangeStatus()
    {
        var active = await AddMark(fsType: PathFilterFsType.File, extensions: [".mp4"]);
        var unsyncedRoot = Path.Combine(_root, "unselected");
        Directory.CreateDirectory(unsyncedRoot);
        var unselected = await AddMark(unsyncedRoot);
        await Marks.MarkAsSyncedBatch([unselected.Id]);
        var missing = await AddResource(Path.Combine(_media, "missing.mp4"), isFile: true, sources: [ResourceSource.PathMark]);
        var ignored = await AddResource(Path.Combine(_media, "ignored.txt"), isFile: true, sources: [ResourceSource.PathMark]);
        var otherMark = await AddResource(Path.Combine(unsyncedRoot, "missing"), sources: [ResourceSource.PathMark]);
        await Sync();
        var current = (await Resources.GetAll()).ToDictionary(resource => resource.Id);
        Assert.AreEqual(ResourceStatus.Absent, current[missing.Id].Status);
        Assert.AreEqual(ResourceStatus.Active, current[ignored.Id].Status);
        Assert.AreEqual(ResourceStatus.Active, current[otherMark.Id].Status);
        Assert.AreEqual(PathMarkSyncStatus.Synced, (await Marks.Get(active.Id))!.SyncStatus);
    }

    [TestMethod]
    public async Task NestedStorageBecomingUnavailableAfterItsEnumerationDoesNotWriteCachedAbsence()
    {
        var parent = Path.Combine(_media, "nested");
        Directory.CreateDirectory(parent);
        await AddMark(layer: 2);
        var resource = await AddResource(Path.Combine(parent, "missing"), sources: [ResourceSource.PathMark]);
        var checks = 0;
        _storage.Allowed = path => path != parent || ++checks < 3;
        await Sync();
        Assert.AreEqual(3, checks, "Storage must be rechecked after enumeration immediately before the status write.");
        Assert.AreEqual(ResourceStatus.Active, (await Resources.GetAll()).Single(row => row.Id == resource.Id).Status);
    }

    [TestMethod]
    [DataRow("edit-group")]
    [DataRow("delete-group")]
    [DataRow("new-boundary")]
    [DataRow("edit-boundary")]
    public async Task ChangedExtensionGroupsAndResourceBoundariesInvalidateTheMatchingProof(string mutation)
    {
        var parent = Path.Combine(_media, "nested");
        Directory.CreateDirectory(parent);
        var groups = _services.GetRequiredService<IExtensionGroupService>();
        var group = await groups.Add(new ExtensionGroupAddInputModel("video", [".mp4"]));
        var mark = await AddMark(layer: 2, fsType: PathFilterFsType.File);
        var config = JsonConvert.DeserializeObject<ResourceMarkConfig>(mark.ConfigJson)!;
        config.ExtensionGroupIds = [group.Id];
        mark.ConfigJson = JsonConvert.SerializeObject(config);
        await Marks.Update(mark);
        PathMark? boundary = null;
        if (mutation == "edit-boundary") boundary = await AddMark(Path.Combine(_root, "other"));
        var resource = await AddResource(Path.Combine(parent, "missing.mp4"), isFile: true, sources: [ResourceSource.PathMark]);
        var changed = false;
        _storage.OnCheck = path =>
        {
            if (path != parent || changed) return;
            changed = true;
            if (mutation == "edit-group")
                groups.Put(group.Id, new ExtensionGroupPutInputModel("video", [".txt"])).GetAwaiter().GetResult();
            else if (mutation == "delete-group") groups.Delete(group.Id).GetAwaiter().GetResult();
            else
            {
                var current = boundary ?? new PathMark {Type = PathMarkType.Resource};
                current.Path = parent;
                current.ConfigJson = JsonConvert.SerializeObject(new ResourceMarkConfig
                {
                    MatchMode = PathMatchMode.Layer, Layer = 0, ApplyScope = PathMarkApplyScope.MatchedOnly,
                    FsTypeFilter = PathFilterFsType.Directory, IsResourceBoundary = true
                });
                if (boundary == null) Marks.Add(current).GetAwaiter().GetResult();
                else Marks.Update(current).GetAwaiter().GetResult();
            }
        };
        await Sync();
        Assert.IsTrue(changed);
        Assert.AreEqual(ResourceStatus.Active, (await Db.ResourcesV2.AsNoTracking().SingleAsync(row => row.Id == resource.Id)).Status);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ANameReusedByTheWrongFilesystemTypeIsAbsentUntilTheOriginalTypeReturns(bool wasFile)
    {
        await AddMark(fsType: wasFile ? PathFilterFsType.File : PathFilterFsType.Directory);
        var path = Path.Combine(_media, "replaced");
        if (wasFile) Directory.CreateDirectory(path);
        else await File.WriteAllTextAsync(path, "replacement");
        var resource = await AddResource(path, isFile: wasFile, sources: [ResourceSource.PathMark]);
        await Sync();
        Assert.AreEqual(ResourceStatus.Absent, (await Resources.GetAll()).Single(row => row.Id == resource.Id).Status);
        if (wasFile) {Directory.Delete(path); await File.WriteAllTextAsync(path, "restored");}
        else {File.Delete(path); Directory.CreateDirectory(path);}
        var mark = (await Marks.GetAll()).Single();
        await Marks.MarkAsPending(mark.Id);
        await Sync();
        Assert.AreEqual(ResourceStatus.Active, (await Resources.GetAll()).Single(row => row.Id == resource.Id).Status);
    }

    [TestMethod]
    [DataRow("path")]
    [DataRow("source")]
    [DataRow("mark")]
    [DataRow("lock")]
    public async Task LatePathOwnershipMarkAndMoveChangesInvalidateTheStatusWrite(string mutation)
    {
        var mark = await AddMark();
        var resource = await AddResource(Path.Combine(_media, "missing"), sources: [ResourceSource.PathMark]);
        var checks = 0;
        _storage.OnCheck = path =>
        {
            // The first check prepares the rule. This second check runs after current
            // resource/source snapshots were read, before the conditional write.
            if (path != _media || ++checks != 2) return;
            if (mutation == "path")
                Db.ResourcesV2.Where(row => row.Id == resource.Id)
                    .ExecuteUpdate(update => update.SetProperty(row => row.Path, Path.Combine(_root, "moved")));
            else if (mutation == "source")
                Db.ResourceSourceLinks.Add(new ResourceSourceLinkDbModel
                    {ResourceId = resource.Id, Source = ResourceSource.Steam, SourceKey = "late-origin"});
            else if (mutation == "mark")
                Db.PathMarks.Where(row => row.Id == mark.Id)
                    .ExecuteUpdate(update => update.SetProperty(row => row.Priority, row => row.Priority + 1));
            else
                Assert.IsTrue(_services.GetRequiredService<ResourceMoveGuard>().TryReserve("late-move", [resource.Id], [resource.Path], out _));
            Db.SaveChanges();
        };
        await Sync();
        Assert.IsTrue(checks >= 2, "The race must run after the candidate snapshot was loaded.");
        Assert.AreEqual(ResourceStatus.Active, (await Db.ResourcesV2.AsNoTracking().SingleAsync(row => row.Id == resource.Id)).Status);
    }

    [TestMethod]
    public async Task CancellationAfterACommittedStatusStillRefreshesCachesAndPublishesItsChange()
    {
        await AddMark();
        await AddResource(Path.Combine(_media, "first"), sources: [ResourceSource.PathMark]);
        await AddResource(Path.Combine(_media, "second"), sources: [ResourceSource.PathMark]);
        using var cancellation = new CancellationTokenSource();
        var events = new List<int>();
        _services.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += args => events.AddRange(args.ResourceIds);
        var checks = 0;
        _storage.OnCheck = path =>
        {
            if (path == _media && ++checks == 6) cancellation.Cancel();
        };
        await Assert.ThrowsAsync<OperationCanceledException>(() => Sync(ct: cancellation.Token));
        var current = await Resources.GetAll();
        var committed = current.Single(resource => resource.Status == ResourceStatus.Absent);
        Assert.AreEqual(1, current.Count(resource => resource.Status == ResourceStatus.Active));
        CollectionAssert.AreEqual(new[] {committed.Id}, events.ToArray());
        Assert.AreEqual(0, _index.Waits, "A cancelled pass must not report index-complete success.");
    }
}
