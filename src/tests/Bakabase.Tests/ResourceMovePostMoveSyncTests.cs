using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.ResourceMove;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Property.Abstractions.Models.Db;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceMovePostMoveSyncTests
{
    private IServiceProvider _services = null!;
    private string _root = null!;
    private readonly RecordingSync _sync = new();
    private Func<string, object, Task>? _onIncremental;
    private IResourceService Resources => _services.GetRequiredService<IResourceService>();
    private IPathMarkService Marks => _services.GetRequiredService<IPathMarkService>();
    private IResourceMoveService Moves => _services.GetRequiredService<IResourceMoveService>();
    private BakabaseDbContext Db => _services.GetRequiredService<BakabaseDbContext>();
    private BTaskArgs Args => new(new PauseToken(), CancellationToken.None,
        new BTask("post-move-sync", () => "test"), _ => Task.CompletedTask, _services);

    private sealed class RecordingSync : IPathMarkSyncService
    {
        public bool Reject;
        public List<int> Requested = [];
        public Task EnqueueSync(params int[] markIds)
        {
            Requested.AddRange(markIds);
            return Reject ? Task.FromException(new IOException("fixture scheduling failure")) : Task.CompletedTask;
        }
        public Task EnqueueSync(ResourceSource source, params int[] markIds) => EnqueueSync(markIds);
    }

    public class CallbackProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    private sealed class CallbackHubContext : IHubContext<WebGuiHub, IWebGuiClient>
    {
        public IHubClients<IWebGuiClient> Clients { get; }
        public IGroupManager Groups { get; }
        public CallbackHubContext(Func<string, object, Task> incremental)
        {
            var client = DispatchProxy.Create<IWebGuiClient, CallbackProxy>();
            ((CallbackProxy)(object)client).Handler = (method, args) =>
                method.Name == nameof(IWebGuiClient.GetIncrementalData)
                    ? incremental((string)args![0]!, args[1]!) : Task.CompletedTask;
            Clients = DispatchProxy.Create<IHubClients<IWebGuiClient>, CallbackProxy>();
            ((CallbackProxy)(object)Clients).Handler = (_, _) => client;
            Groups = DispatchProxy.Create<IGroupManager, CallbackProxy>();
            ((CallbackProxy)(object)Groups).Handler = (_, _) => Task.CompletedTask;
        }
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "move-post-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _services = await TestServiceBuilder.BuildServiceProvider(Path.Combine(_root, "app"), services =>
        {
            services.AddSingleton<IPathMarkSyncService>(_sync);
            services.AddSingleton<IHubContext<WebGuiHub, IWebGuiClient>>(new CallbackHubContext((key, data) =>
                _onIncremental?.Invoke(key, data) ?? Task.CompletedTask));
        });
        _services.GetRequiredService<IBOptions<ResourceOptions>>().Value.SynchronizationOptions.SyncMarksImmediately = false;
        Db.CustomProperties.AddRange(
            new CustomPropertyDbModel {Id = 52, Name = "Status", Type = PropertyType.SingleLineText},
            new CustomPropertyDbModel {Id = 53, Name = "Child label", Type = PropertyType.SingleLineText});
        await Db.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        (_services as IDisposable)?.Dispose();
        Directory.Delete(_root, true);
    }

    private string Dir(string path)
    {
        var fullPath = Path.Combine(_root, path);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private async Task<Resource> Resource(string path, ResourceStatus status = ResourceStatus.Absent,
        bool createDirectory = true, bool mixedSource = false)
    {
        var fullPath = createDirectory ? Dir(path) : Path.Combine(_root, path);
        var links = new List<ResourceSourceLink> {new() {Source = ResourceSource.PathMark, SourceKey = fullPath}};
        if (mixedSource) links.Add(new() {Source = ResourceSource.Pixiv, SourceKey = "fixture-work"});
        await Resources.AddOrPutRange([new Resource {Path = fullPath, Status = status, SourceLinks = links}]);
        return (await Resources.GetAll()).Single(resource => resource.Path == fullPath);
    }

    private Task<PathMark> PropertyMark(string path, int propertyId, string value, bool subtree = false) => Marks.Add(new PathMark
    {
        Path = Path.Combine(_root, path), Type = PathMarkType.Property,
        ConfigJson = JsonConvert.SerializeObject(new PropertyMarkConfig
        {
            MatchMode = PathMatchMode.Layer, Layer = 0, Pool = PropertyPool.Custom, PropertyId = propertyId,
            ValueType = PropertyValueType.Fixed, FixedValue = value,
            ApplyScope = subtree ? PathMarkApplyScope.MatchedAndSubdirectories : PathMarkApplyScope.MatchedOnly
        })
    });

    private Task SyncProperties() => _services.GetRequiredService<PathMarkSyncService>()
        .SyncMarks(null, null, new PauseToken(), CancellationToken.None);

    private async Task<string?> Value(int resourceId, int propertyId) =>
        (await Db.CustomPropertyValues.AsNoTracking().SingleOrDefaultAsync(row => row.ResourceId == resourceId &&
            row.PropertyId == propertyId && row.Scope == (int)PropertyValueScope.Synchronization))?.Value;

    private async Task<(PathMark Old, PathMark New)> StatusMarks()
    {
        var oldMark = await PropertyMark("TBD/AV", 52, "TBD", true);
        var newMark = await PropertyMark("Use/AV", 52, "Use", true);
        return (oldMark, newMark);
    }

    [TestMethod]
    public async Task CompletedMoveRekeysTheEntireTreeRestoresAvailabilityAndReappliesAllRelatedMarks()
    {
        var resource = await Resource("TBD/AV/Work");
        var child = await Resource("TBD/AV/Work/Child");
        var missingChild = await Resource("TBD/AV/Work/Gone", createDirectory: false);
        var statusMarks = await StatusMarks();
        var oldChild = await PropertyMark("TBD/AV/Work/Child", 53, "old child");
        var newChild = await PropertyMark("Use/AV/Work/Child", 53, "new child");
        await SyncProperties();
        Assert.AreEqual("TBD", await Value(resource.Id, 52));
        Assert.AreEqual("old child", await Value(child.Id, 53));
        Db.CustomPropertyValues.Add(new CustomPropertyValueDbModel
            {ResourceId = resource.Id, PropertyId = 52, Scope = (int)PropertyValueScope.Manual, Value = "manual override"});
        await Db.SaveChangesAsync();
        var notificationSawPending = false;
        _services.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += args =>
        {
            if (!args.ResourceIds.Contains(resource.Id) || !Db.ResourceMoveRecords.AsNoTracking().Any(row =>
                    row.ResourceId == resource.Id && row.Status == ResourceMoveRecordStatus.Succeeded)) return;
            notificationSawPending = Db.PathMarks.AsNoTracking().Where(mark =>
                    mark.Id == statusMarks.Old.Id || mark.Id == statusMarks.New.Id ||
                    mark.Id == oldChild.Id || mark.Id == newChild.Id)
                .All(mark => mark.SyncStatus == PathMarkSyncStatus.Pending);
        };
        var destination = Dir("Use/AV");
        var batch = (await Moves.CreateBatch([resource.Id], destination)).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        Assert.IsTrue(notificationSawPending, "The move must persist pending mark work before publishing success.");
        var current = (await Resources.GetAll()).ToDictionary(row => row.Id);
        foreach (var original in new[] {resource, child, missingChild})
        {
            var expected = original.Path!.Replace("/TBD/AV/", "/Use/AV/");
            Assert.AreEqual(expected, current[original.Id].Path);
            Assert.AreEqual(expected, (await _services.GetRequiredService<IResourceSourceLinkService>()
                .GetByResourceId(original.Id)).Single().SourceKey);
        }
        Assert.AreEqual(ResourceStatus.Active, current[resource.Id].Status);
        Assert.AreEqual(ResourceStatus.Active, current[child.Id].Status);
        Assert.AreEqual(ResourceStatus.Absent, current[missingChild.Id].Status);
        CollectionAssert.AreEquivalent(new[] {statusMarks.Old.Id, statusMarks.New.Id, oldChild.Id, newChild.Id},
            _sync.Requested.Distinct().ToArray());
        await SyncProperties();
        Assert.AreEqual("Use", await Value(resource.Id, 52));
        Assert.AreEqual("Use", await Value(child.Id, 52));
        Assert.AreEqual("new child", await Value(child.Id, 53));
        Assert.AreEqual("manual override", (await Db.CustomPropertyValues.AsNoTracking().SingleAsync(row =>
            row.ResourceId == resource.Id && row.PropertyId == 52 && row.Scope == (int)PropertyValueScope.Manual)).Value);
        Assert.IsFalse(await Db.PropertyMarkEffects.AnyAsync(effect => effect.MarkId == statusMarks.Old.Id || effect.MarkId == oldChild.Id));
        Assert.AreEqual(3, (await Resources.GetAll()).Count);
    }

    [TestMethod]
    public async Task APhysicalMoveDoesNotReplaceAnExternalOriginsAvailabilityStatus()
    {
        var resource = await Resource("TBD/AV/Mixed", mixedSource: true);
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        var moved = (await Resources.GetAll()).Single();
        Assert.AreEqual(ResourceStatus.Absent, moved.Status);
        Assert.AreEqual(2, (await _services.GetRequiredService<IResourceSourceLinkService>().GetByResourceId(moved.Id)).Count);
    }

    [TestMethod]
    public async Task DurablePendingMarksResumeAfterSchedulingFailureAndStartupWithoutSyncingRows()
    {
        var resource = await Resource("TBD/AV/Work", ResourceStatus.Active);
        await StatusMarks();
        await SyncProperties();
        _sync.Reject = true;
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        Assert.AreEqual(ResourceMoveRecordStatus.Succeeded, (await Db.ResourceMoveRecords.AsNoTracking().SingleAsync()).Status);
        Assert.AreEqual(2, await Marks.GetPendingCount());
        Assert.AreEqual(0, (await Marks.GetBySyncStatus(PathMarkSyncStatus.Syncing)).Count);
        var manual = await PropertyMark("Use/AV/Work", 53, "leave pending");
        var tasks = _services.GetRequiredService<BTaskManager>();
        var manager = new PathSyncManager(NullLogger<PathSyncManager>.Instance, _services, tasks,
            _services.GetRequiredService<IBakabaseLocalizer>());
        await (Task)typeof(PathSyncManager).GetMethod("RecoverInterruptedSyncs", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, [CancellationToken.None])!;
        await (Task)typeof(PathSyncManager).GetMethod("ProcessQueuesIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(manager, [CancellationToken.None])!;
        Assert.IsNotNull(tasks.GetTaskViewModel("SyncPathMarks"));
        await tasks.Start("SyncPathMarks");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (tasks.GetTaskViewModel("SyncPathMarks")!.Status is BTaskStatus.NotStarted or BTaskStatus.Running)
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Recovered property synchronization did not finish.");
            await Task.Delay(10);
        }
        Assert.AreEqual(BTaskStatus.Completed, tasks.GetTaskViewModel("SyncPathMarks")!.Status);
        Assert.AreEqual("Use", await Value(resource.Id, 52));
        Assert.IsNull(await Value(resource.Id, 53), "Recovery must not apply unrelated manual pending configurations.");
        Assert.AreEqual(PathMarkSyncStatus.Pending, (await Marks.Get(manual.Id))!.SyncStatus);
        Assert.AreEqual(1, await Marks.GetPendingCount());
    }

    [TestMethod]
    public async Task RelationshipRebuildFailureDoesNotSuppressPersistedMarkScheduling()
    {
        var resource = await Resource("TBD/AV/Work", ResourceStatus.Active);
        await Resource("TBD/AV/Work/Child", ResourceStatus.Active);
        var marks = await StatusMarks();
        await SyncProperties();
        await Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_relationship_rebuild BEFORE UPDATE OF ParentId ON ResourcesV2
            WHEN NEW.ParentId IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'fixture relationship rebuild failure'); END;
            """);
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        Assert.AreEqual(ResourceMoveRecordStatus.Succeeded, (await Db.ResourceMoveRecords.AsNoTracking().SingleAsync()).Status);
        CollectionAssert.AreEquivalent(new[] {marks.Old.Id, marks.New.Id}, _sync.Requested.Distinct().ToArray());
        Assert.AreEqual(2, await Marks.GetPendingCount());
    }

    [TestMethod]
    public async Task FailureToPersistMarkWorkKeepsTheMoveRecoverableInsteadOfReportingSuccess()
    {
        var resource = await Resource("TBD/AV/Work");
        await StatusMarks();
        await SyncProperties();
        await Db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER reject_pending_mark BEFORE UPDATE OF SyncStatus ON PathMarks
            WHEN NEW.SyncStatus = 0
            BEGIN SELECT RAISE(ABORT, 'fixture mark persistence failure'); END;
            """);
        var recoveryPublished = false;
        _services.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged += args =>
        {
            if (args.ResourceIds.Contains(resource.Id))
                recoveryPublished = Db.ResourceMoveRecords.AsNoTracking().Any(row =>
                    row.ResourceId == resource.Id && row.Status == ResourceMoveRecordStatus.NeedsRecovery);
        };
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Assert.ThrowsAsync<BTaskSuspendedException>(() => Moves.ExecuteBatch(batch.BatchId, Args));
        var record = await Db.ResourceMoveRecords.AsNoTracking().SingleAsync();
        Assert.AreEqual(ResourceMoveRecordStatus.NeedsRecovery, record.Status);
        Assert.IsNotNull(record.MoveJournalJson);
        Assert.IsTrue(_services.GetRequiredService<ResourceMoveGuard>().IsResourceLocked(resource.Id));
        Assert.IsTrue(recoveryPublished);
        Assert.AreEqual(ResourceStatus.Active, (await Resources.GetAll()).Single().Status);
    }

    private PathSyncManager NewManager() => new(NullLogger<PathSyncManager>.Instance, _services,
        _services.GetRequiredService<BTaskManager>(), _services.GetRequiredService<IBakabaseLocalizer>());

    private static async Task RecoverAndPoll(PathSyncManager manager)
    {
        foreach (var method in new[] {"RecoverInterruptedSyncs", "ProcessQueuesIfNeeded"})
            await (Task)typeof(PathSyncManager).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(manager, [CancellationToken.None])!;
    }

    [TestMethod]
    public async Task StartupDoesNotAutomaticallyApplyManuallyPendingConfigurations()
    {
        await PropertyMark("TBD/AV", 52, "manual pending", true);
        await RecoverAndPoll(NewManager());
        Assert.AreEqual(1, await Marks.GetPendingCount());
        Assert.IsNull(_services.GetRequiredService<BTaskManager>().GetTaskViewModel("SyncPathMarks"));
    }

    [TestMethod]
    public async Task StartupDoesNotRecoverAManualEditWithANewerRevisionThanTheMoveReceipt()
    {
        var resource = await Resource("TBD/AV/Work", ResourceStatus.Active);
        var mark = await PropertyMark("TBD/AV", 52, "TBD", true);
        await SyncProperties();
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        mark = (await Marks.Get(mark.Id))!;
        mark.Path = Path.Combine(_root, "Use/AV");
        var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(mark.ConfigJson)!;
        config.FixedValue = "manual pending";
        mark.ConfigJson = JsonConvert.SerializeObject(config);
        await Marks.Update(mark);
        await RecoverAndPoll(NewManager());
        Assert.AreEqual(1, await Marks.GetPendingCount());
        Assert.IsNull(_services.GetRequiredService<BTaskManager>().GetTaskViewModel("SyncPathMarks"));
        Assert.AreEqual("TBD", await Value(resource.Id, 52));
    }

    [TestMethod]
    public async Task SchedulingAnAlreadyPendingMarkPreservesItsDurableRequestedRevision()
    {
        var mark = await PropertyMark("TBD/AV", 52, "TBD", true);
        var before = (await Marks.Get(mark.Id))!.UpdatedAt;
        await NewManager().EnqueueSync(mark.Id);
        Assert.AreEqual(before, (await Marks.Get(mark.Id))!.UpdatedAt);
    }

    [TestMethod]
    public async Task HistoryCleanupPreservesPendingReceiptsUntilTheRequestedMarksHaveSynced()
    {
        var resource = await Resource("TBD/AV/Work", ResourceStatus.Active);
        await StatusMarks();
        await SyncProperties();
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        var record = await Db.ResourceMoveRecords.AsNoTracking().SingleAsync();
        Assert.AreNotEqual(0, (await Moves.DeleteRecord(record.Id)).Code);
        Assert.AreEqual(0, (await Moves.DeleteInactiveRecords()).Code);
        Assert.AreEqual(1, await Db.ResourceMoveRecords.AsNoTracking().CountAsync());
        await SyncProperties();
        Assert.AreEqual(0, (await Moves.DeleteInactiveRecords()).Code);
        Assert.AreEqual(0, await Db.ResourceMoveRecords.AsNoTracking().CountAsync());
    }

    [TestMethod]
    public async Task CancellationAfterPhysicalMoveWithNoAvailabilityChangesPreservesRecoveryJournalAndLocks()
    {
        var resource = await Resource("TBD/AV/Work", ResourceStatus.Active);
        var destination = Dir("Use/AV");
        var batch = (await Moves.CreateBatch([resource.Id], destination)).Data!;
        using var cancellation = new CancellationTokenSource();
        var task = new BTask("cancel-physical-move", () => "test");
        var args = new BTaskArgs(new PauseToken(), cancellation.Token, task, update =>
        {
            update(task);
            if (task.Percentage == 100 && Db.ResourceMoveRecords.AsNoTracking().Single().PhysicalMoveStarted)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return Task.CompletedTask;
        }, _services);
        var error = await Assert.ThrowsAsync<BTaskSuspendedException>(() => Moves.ExecuteBatch(batch.BatchId, args));
        Assert.IsTrue(error.PreserveOnCancellation);
        var record = await Db.ResourceMoveRecords.AsNoTracking().SingleAsync();
        Assert.AreEqual(ResourceMoveRecordStatus.NeedsRecovery, record.Status);
        Assert.IsTrue(record.PhysicalMoveStarted);
        Assert.IsNotNull(record.MoveJournalJson);
        Assert.IsTrue(Directory.Exists(Path.Combine(destination, "Work")), "The real executor must have physically moved the directory.");
        Assert.IsTrue(_services.GetRequiredService<ResourceMoveGuard>().IsResourceLocked(resource.Id));
        Assert.AreEqual(ResourceStatus.Active, (await Resources.GetAll()).Single().Status);
        Assert.AreEqual(resource.Path, (await Resources.GetAll()).Single().Path);
    }

    [TestMethod]
    public async Task AManualEditDuringThePendingNotificationIsNeverClaimedAsMoveRequestedWork()
    {
        var resource = await Resource("TBD/AV/Work", ResourceStatus.Active);
        var mark = await PropertyMark("TBD/AV", 52, "TBD", true);
        await SyncProperties();
        var edited = false;
        _onIncremental = async (key, data) =>
        {
            if (edited || key != "PathMark" || data is not PathMark changed ||
                changed.Id != mark.Id || changed.SyncStatus != PathMarkSyncStatus.Pending) return;
            edited = true;
            var current = (await Marks.Get(mark.Id))!;
            var config = JsonConvert.DeserializeObject<PropertyMarkConfig>(current.ConfigJson)!;
            config.FixedValue = "manual pending";
            current.ConfigJson = JsonConvert.SerializeObject(config);
            await Marks.Update(current);
        };
        var batch = (await Moves.CreateBatch([resource.Id], Dir("Use/AV"))).Data!;
        await Moves.ExecuteBatch(batch.BatchId, Args);
        Assert.IsTrue(edited);
        var row = await Db.ResourceMoveRecords.AsNoTracking().SingleAsync();
        var receipt = JsonConvert.DeserializeObject<ResourceMoveExecutionPlan>(row.ExecutionPlanJson!)!.PendingPathMarkRevisions!;
        Assert.AreNotEqual(receipt[mark.Id], (await Marks.Get(mark.Id))!.UpdatedAt.Ticks);
        var manager = NewManager();
        await manager.EnqueueRequestedRevisions(receipt);
        await RecoverAndPoll(manager);
        Assert.IsNull(_services.GetRequiredService<BTaskManager>().GetTaskViewModel("SyncPathMarks"));
        Assert.AreEqual(PathMarkSyncStatus.Pending, (await Marks.Get(mark.Id))!.SyncStatus);
        Assert.AreEqual("TBD", await Value(resource.Id, 52));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactSyncPreservesUnselectedPendingPropertyEffectsAndMediaLibraryMappings(bool reference)
    {
        if (reference)
        {
            Db.CustomProperties.Local.Single(property => property.Id == 53).Type = PropertyType.SingleChoice;
            await Db.SaveChangesAsync();
        }
        var resource = await Resource("Content/Work", ResourceStatus.Active);
        var selected = await PropertyMark("Content/Work", 53, "selected");
        var held = await PropertyMark("Content/Work", 53, "keep prior");
        held.Priority = 100;
        await Marks.Update(held);
        var libraries = _services.GetRequiredService<IMediaLibraryV2Service>();
        var library = await libraries.Add(new MediaLibraryV2AddOrPutInputModel("keep library", []));
        var unrequestedLibrary = await libraries.Add(new MediaLibraryV2AddOrPutInputModel("manual library", []));
        async Task<PathMark> LibraryMark() => await Marks.Add(new PathMark
        {
            Path = resource.Path!, Type = PathMarkType.MediaLibrary,
            ConfigJson = JsonConvert.SerializeObject(new MediaLibraryMarkConfig
            {
                MatchMode = PathMatchMode.Layer, Layer = 0, ValueType = PropertyValueType.Fixed,
                MediaLibraryId = library.Id, ApplyScope = PathMarkApplyScope.MatchedOnly
            })
        });
        var selectedLibrary = await LibraryMark();
        var heldLibrary = await LibraryMark();
        await SyncProperties();
        var previous = await Value(resource.Id, 53);
        Assert.IsNotNull(previous);
        var oldEffect = JsonConvert.SerializeObject(await Db.PropertyMarkEffects.AsNoTracking().SingleAsync(effect => effect.MarkId == held.Id));
        held = (await Marks.Get(held.Id))!;
        var heldConfig = JsonConvert.DeserializeObject<PropertyMarkConfig>(held.ConfigJson)!;
        heldConfig.FixedValue = "manual-unrequested";
        held.ConfigJson = JsonConvert.SerializeObject(heldConfig);
        await Marks.Update(held);
        heldLibrary = (await Marks.Get(heldLibrary.Id))!;
        var libraryConfig = JsonConvert.DeserializeObject<MediaLibraryMarkConfig>(heldLibrary.ConfigJson)!;
        libraryConfig.MediaLibraryId = unrequestedLibrary.Id;
        heldLibrary.ConfigJson = JsonConvert.SerializeObject(libraryConfig);
        await Marks.Update(heldLibrary);
        selected = (await Marks.Get(selected.Id))!;
        var selectedConfig = JsonConvert.DeserializeObject<PropertyMarkConfig>(selected.ConfigJson)!;
        selectedConfig.FixedValue = "selected new";
        selected.ConfigJson = JsonConvert.SerializeObject(selectedConfig);
        await Marks.Update(selected);
        selectedLibrary = (await Marks.Get(selectedLibrary.Id))!;
        selectedLibrary.Path = Path.Combine(_root, "elsewhere");
        await Marks.Update(selectedLibrary);
        var requested = new Dictionary<int, long>
        {
            [selected.Id] = (await Marks.Get(selected.Id))!.UpdatedAt.Ticks,
            [selectedLibrary.Id] = (await Marks.Get(selectedLibrary.Id))!.UpdatedAt.Ticks
        };
        await _services.GetRequiredService<PathMarkSyncService>().SyncMarks(null, null, new PauseToken(), CancellationToken.None, requested);
        Assert.AreEqual(previous, await Value(resource.Id, 53));
        Assert.AreEqual(oldEffect, JsonConvert.SerializeObject(await Db.PropertyMarkEffects.AsNoTracking().SingleAsync(effect => effect.MarkId == held.Id)));
        Assert.AreEqual(PathMarkSyncStatus.Pending, (await Marks.Get(held.Id))!.SyncStatus);
        Assert.AreEqual(PathMarkSyncStatus.Pending, (await Marks.Get(heldLibrary.Id))!.SyncStatus);
        var currentLibraries = await _services.GetRequiredService<IMediaLibraryResourceMappingService>().GetMediaLibraryIdsByResourceId(resource.Id);
        CollectionAssert.AreEqual(new[] {library.Id}, currentLibraries.ToArray());
        if (reference)
            Assert.IsFalse((await Db.CustomProperties.AsNoTracking().SingleAsync(property => property.Id == 53)).Options!
                .Contains("manual-unrequested", StringComparison.Ordinal));
    }
}
