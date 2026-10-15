using System.Collections.Concurrent;
using System.Net.Http;
using System.Reflection;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed partial class MetadataReplicaCoordinatorTests
{
    private string _root = null!;
    private MetadataReplicaStore _store = null!;
    private MetadataReplicaRegistry _registry = null!;
    private MetadataReplicaCoordinator _coordinator = null!;
    private IManagedServerService _transport = null!;
    private Wire _wire = null!;
    private static readonly ReplicaIdentity Owner = new("source-device", "source-epoch");
    private static readonly ReplicaCheckpoint Start = new("generation-one", 1, "start", false);

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-metadata-coordinator-" + Guid.NewGuid().ToString("N"));
        _store = new(_root);
        _registry = new(new DataDirectory(_root));
        _transport = DispatchProxy.Create<IManagedServerService, Wire>();
        _wire = (Wire)(object)_transport;
        _coordinator = new(_transport, _store, _registry);
        _wire.Handle = call => Task.FromResult(call.Path.EndsWith("/info")
            ? Json(new ManagedLibraryInfo(Owner.ServerId, "Source", Owner.LibraryEpoch))
            : call.Path.EndsWith("/head") ? Json(new MetadataSourceManifest(Owner, Start, "Source"))
            : Page(call, 1));
    }

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private static ManagedServerResponse Json(object value) => new(200, "application/json", JsonConvert.SerializeObject(value));
    private static ReplicaResource Resource(long revision) => new(1, revision,
        JsonConvert.SerializeObject(new {id = 1, displayName = "revision-" + revision}), new(), []);
    private static ManagedServerResponse Page(Call call, long revision)
    {
        var request = JsonConvert.DeserializeObject<MetadataPageRequest>(call.Json!)!;
        var next = request.Checkpoint with {Complete = true, Cursor = null, Sequence = revision};
        return Json(new ReplicaBatch(request.Checkpoint, next,
            request.Checkpoint == next ? [] : [Resource(revision)], [], [], []));
    }
    private async Task SeedAsync() => await _coordinator.RunCycleAsync(null, null, default);
    private ManagedLibraryService Service() => new(_transport, new Identity(), _coordinator,
        new ManagedMetadataReader(_coordinator, _store, new(_store), null!));
    private static ManagedLibraryRequest Get() => new(Owner.LibraryEpoch, "resourceGet", Ids: [1]);
    private static ManagedLibraryRequest Write() => new(Owner.LibraryEpoch, "resourcePin",
        Id: 1, Query: new() {{"pin", ["true"]}});

    [TestMethod]
    public async Task Complete_replica_waits_for_all_catalog_pages_before_clearing_write_refresh_barrier()
    {
        await SeedAsync();
        await _store.UpdateSyncStateAsync(Owner, state => state with {RefreshPending = true});
        var pages = 0;
        _wire.Handle = call =>
        {
            if (call.Path.EndsWith("/head"))
                return Task.FromResult(Json(new MetadataSourceManifest(Owner, Start with {Sequence = 3, CatalogRevision = 2})));
            var request = JsonConvert.DeserializeObject<MetadataPageRequest>(call.Json!)!;
            pages++;
            var next = request.Checkpoint with {Sequence = 3, Complete = true,
                Cursor = pages < 3 ? "catalog-" + pages : null, CatalogRevision = pages < 3 ? 0 : 2};
            return Task.FromResult(Json(new ReplicaBatch(request.Checkpoint, next, [], [],
                [new ReplicaCatalog("testCatalog", pages.ToString(), "{}")], [])));
        };
        await _coordinator.RunCycleAsync(null, null, default);
        Assert.AreEqual(3, pages);
        Assert.AreEqual(3, (await _store.ReadCatalogAllAsync(Owner, "testCatalog", default)).Count);
        Assert.IsFalse((await _store.GetSyncStateAsync(Owner)).RefreshPending);
    }

    [TestMethod]
    public async Task Local_desktop_without_managed_devices_never_contacts_metadata_transport()
    {
        _wire.Connections = [];
        await SeedAsync();
        var sources = await Service().SourcesAsync(default);
        Assert.AreEqual(1, sources.Sources.Count);
        Assert.IsTrue(sources.Sources.Single().IsLocal);
        Assert.IsNull(sources.Sources.Single().Metadata);
        Assert.IsEmpty(_wire.Calls);
    }

    [TestMethod]
    public async Task Complete_metadata_remains_readable_offline_but_live_write_is_not_queued()
    {
        await SeedAsync();
        _wire.Calls.Clear();
        _wire.Connections = [_wire.Connections.Single() with {State = ManagedServerState.Offline}];
        _wire.Handle = _ => throw new HttpRequestException("offline");
        var resource = await Service().ExecuteAsync(Owner.ServerId, Get(), default);
        StringAssert.Contains(resource.Body, "revision-1");
        Assert.IsEmpty(_wire.Calls);
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Write(), default));
        Assert.AreEqual("ManagedSourceUnavailable", error.ErrorCode);
        Assert.AreEqual(1, _wire.Calls.Count);
        Assert.AreEqual("/management/library/info", _wire.Calls.Single().Path);
        Assert.IsFalse((await _store.GetSyncStateAsync(Owner)).RefreshPending);
    }

    [TestMethod]
    public async Task Revocation_wrong_epoch_and_forgotten_pairing_all_deny_cached_data()
    {
        await SeedAsync();
        var epoch = await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            _coordinator.RequireAsync(Owner.ServerId, "another-epoch", default));
        Assert.AreEqual("LibraryEpochChanged", epoch.ErrorCode);
        _wire.Connections = [_wire.Connections.Single() with {State = ManagedServerState.Revoked}];
        var denied = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Get(), default));
        Assert.AreEqual("ManagedSourceDenied", denied.ErrorCode);
        _wire.Connections = [];
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Get(), default));
        Assert.AreEqual(1, (await _store.GetRowsByIdsAsync(Owner, [1])).Count); // retained physically, inaccessible through the reader
    }

    [TestMethod]
    public async Task First_snapshot_page_access_denied_is_persisted_and_not_reported_as_empty_library()
    {
        var normal = _wire.Handle;
        _wire.Handle = call => call.Path.Contains("/page")
            ? Task.FromResult(new ManagedServerResponse(403, "application/json", "{}")) : normal(call);
        await SeedAsync();
        Assert.IsTrue((await _registry.ReadAsync(default)).Single().Denied);
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Get(), default));
        Assert.AreEqual("ManagedSourceDenied", error.ErrorCode);
        Assert.IsNull((await _store.GetStatusAsync(Owner)).ActiveGeneration);
    }

    [TestMethod]
    public async Task Legacy_source_error_after_catalog_preserves_checkpoint_and_resumes_same_resource_page()
    {
        var resourceCheckpoint = Start with {Cursor = "resources"};
        var requests = new List<ReplicaCheckpoint>();
        _wire.Handle = call =>
        {
            if (call.Path.EndsWith("/head")) return Task.FromResult(Json(new MetadataSourceManifest(Owner, Start)));
            var request = JsonConvert.DeserializeObject<MetadataPageRequest>(call.Json!)!;
            requests.Add(request.Checkpoint);
            return Task.FromResult(request.Checkpoint == Start
                ? Json(new ReplicaBatch(Start, resourceCheckpoint, [], [], [new("properties", "4:1", "{}")], []))
                : Json(new {code = 500, message = "Private source exception stack /private/source/database.sqlite"}));
        };
        await SeedAsync();
        CollectionAssert.AreEqual(new[] {Start, resourceCheckpoint}, requests);
        var failed = await _store.GetStatusAsync(Owner);
        Assert.AreEqual(resourceCheckpoint, failed.Checkpoint);
        Assert.IsNull(failed.ActiveGeneration);
        Assert.AreEqual(0L, failed.StagedResourceCount);
        var status = await _store.GetSyncStateAsync(Owner);
        Assert.AreEqual("MetadataSyncFailed", status.ErrorCode);
        Assert.AreEqual("The source did not complete the metadata request. The saved checkpoint is retained.", status.Error);

        requests.Clear();
        _wire.Handle = call =>
        {
            if (call.Path.EndsWith("/head")) return Task.FromResult(Json(new MetadataSourceManifest(Owner, Start)));
            requests.Add(JsonConvert.DeserializeObject<MetadataPageRequest>(call.Json!)!.Checkpoint);
            return Task.FromResult(Page(call, 1));
        };
        await SeedAsync();
        CollectionAssert.AreEqual(new[] {resourceCheckpoint}, requests);
        Assert.IsTrue((await _store.GetStatusAsync(Owner)).Checkpoint!.Complete);
        Assert.AreEqual(1L, (await _store.GetStatusAsync(Owner)).ResourceCount);
        Assert.AreEqual(1, (await _store.ReadCatalogAllAsync(Owner, "properties", default)).Count);
        Assert.IsNull((await _store.GetSyncStateAsync(Owner)).ErrorCode);
    }

    [TestMethod]
    public async Task Legacy_source_error_preserves_complete_replica_and_pending_write_refresh()
    {
        await SeedAsync();
        var checkpoint = (await _store.GetStatusAsync(Owner)).Checkpoint;
        await using (await _coordinator.BeginWriteAsync(Owner.ServerId, Owner.LibraryEpoch, default)) { }
        _wire.Handle = call => Task.FromResult(call.Path.EndsWith("/head")
            ? Json(new MetadataSourceManifest(Owner, Start with {Sequence = 2}))
            : new ManagedServerResponse(200, "application/json",
                "{\"message\":\"Private source exception stack\",\"code\":500}"));
        await SeedAsync();
        Assert.AreEqual(checkpoint, (await _store.GetStatusAsync(Owner)).Checkpoint);
        Assert.AreEqual(1L, (await _store.GetRowsByIdsAsync(Owner, [1])).Single().Revision);
        var status = await _store.GetSyncStateAsync(Owner);
        Assert.IsTrue(status.RefreshPending);
        Assert.AreEqual("MetadataSyncFailed", status.ErrorCode);
        Assert.IsFalse(status.Error!.Contains("Private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Legacy_manifest_error_is_source_failure_without_a_ready_empty_library_or_exception_details()
    {
        _wire.Handle = _ => Task.FromResult(Json(new {code = 500, message = "Private source exception stack"}));
        await SeedAsync();
        var status = (await _coordinator.StatusAsync(default)).Single();
        Assert.IsNull(status.Data.ActiveGeneration);
        Assert.IsEmpty(status.Identity.LibraryEpoch);
        Assert.AreEqual("MetadataSyncFailed", status.Sync.ErrorCode);
        Assert.IsFalse(status.Sync.Error!.Contains("Private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Malformed_or_oversized_pages_leave_committed_rows_and_checkpoint_then_retry_same_cursor()
    {
        await SeedAsync();
        var checkpoint = (await _store.GetStatusAsync(Owner)).Checkpoint!;
        var malformed = new[]
        {
            "null", "{}",
            JsonConvert.SerializeObject(new ReplicaBatch(checkpoint with {Cursor = "another-checkpoint"},
                checkpoint with {Sequence = 2}, [Resource(2)], [], [], [])),
            JsonConvert.SerializeObject(new ReplicaBatch(checkpoint, checkpoint with {Sequence = 2}, null!, [], [], [])),
            JsonConvert.SerializeObject(new ReplicaBatch(checkpoint, checkpoint with {Sequence = 2}, [Resource(2) with {Postings = null!}], [], [], [])),
            JsonConvert.SerializeObject(new ReplicaBatch(checkpoint, checkpoint with {Sequence = 2}, [Resource(2) with {ResourceJson = "{\"id\":99}"}], [], [], [])),
            new string(' ', MetadataReplicaStore.MaxBatchBytes + 1)
        };
        foreach (var body in malformed)
        {
            _wire.Handle = call => Task.FromResult(call.Path.EndsWith("/head")
                ? Json(new MetadataSourceManifest(Owner, Start with {Sequence = 2}))
                : new ManagedServerResponse(200, "application/json", body));
            await SeedAsync();
            Assert.AreEqual(checkpoint, (await _store.GetStatusAsync(Owner)).Checkpoint);
            Assert.AreEqual(1L, (await _store.GetRowsByIdsAsync(Owner, [1])).Single().Revision);
            Assert.IsNotNull((await _store.GetSyncStateAsync(Owner)).ErrorCode);
        }
        _wire.Handle = call => Task.FromResult(call.Path.EndsWith("/head")
            ? Json(new MetadataSourceManifest(Owner, Start with {Sequence = 2})) : Page(call, 2));
        await SeedAsync();
        Assert.AreEqual(2L, (await _store.GetStatusAsync(Owner)).ActiveSequence);
        Assert.IsNull((await _store.GetSyncStateAsync(Owner)).ErrorCode);
    }

    [TestMethod]
    public async Task Invalid_manifest_does_not_crash_discovery_or_publish_an_empty_ready_library()
    {
        foreach (var body in new[] {"null", "{}", "{\"contractVersion\":1,\"identity\":null}", new string(' ', 65537)})
        {
            _wire.Handle = _ => Task.FromResult(new ManagedServerResponse(200, "application/json", body));
            await SeedAsync();
            var status = (await _coordinator.StatusAsync(default)).Single();
            Assert.IsNull(status.Data.ActiveGeneration);
            Assert.IsNotNull(status.Sync.ErrorCode);
            Assert.IsEmpty(status.Identity.LibraryEpoch);
        }
    }

    [TestMethod]
    public async Task Changed_source_epoch_invalidates_previous_cached_selection_even_if_new_sync_is_incomplete()
    {
        await SeedAsync();
        var nextOwner = Owner with {LibraryEpoch = "new-epoch"};
        _wire.Handle = call => Task.FromResult(call.Path.EndsWith("/head")
            ? Json(new MetadataSourceManifest(nextOwner, Start))
            : new ManagedServerResponse(503, "application/json", "{}"));
        await SeedAsync();
        var error = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Get(), default));
        Assert.AreEqual("LibraryEpochChanged", error.ErrorCode);
        Assert.IsNull((await _store.GetStatusAsync(nextOwner)).ActiveGeneration);
        Assert.AreEqual(1, (await _store.GetRowsByIdsAsync(Owner, [1])).Count);
    }

    [TestMethod]
    public async Task Oversized_source_page_retries_smaller_batch_without_advancing_cursor()
    {
        var requested = new List<int>();
        _wire.Handle = call =>
        {
            if (call.Path.EndsWith("/head")) return Task.FromResult(Json(new MetadataSourceManifest(Owner, Start)));
            var page = JsonConvert.DeserializeObject<MetadataPageRequest>(call.Json!)!;
            requested.Add(page.PageSize);
            Assert.AreEqual(Start, page.Checkpoint);
            return Task.FromResult(page.PageSize > 12 ? new ManagedServerResponse(413, "application/json", "{}") : Page(call, 1));
        };
        await SeedAsync();
        CollectionAssert.AreEqual(new[] {50, 25, 12}, requested);
        Assert.AreEqual(1L, (await _store.GetStatusAsync(Owner)).ResourceCount);
    }

    [TestMethod]
    public async Task Mutation_barrier_is_durable_before_dispatch_and_background_cannot_clear_it_while_write_runs()
    {
        await SeedAsync();
        _wire.Calls.Clear();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revision = 1L;
        _wire.Handle = async call =>
        {
            if (call.Path.EndsWith("/info")) return Json(new ManagedLibraryInfo(Owner.ServerId, "Source", Owner.LibraryEpoch));
            if (call.Method == HttpMethod.Put)
            {
                var reopened = new MetadataReplicaStore(_root);
                Assert.IsTrue((await reopened.GetSyncStateAsync(Owner)).RefreshPending);
                entered.SetResult();
                await release.Task;
                revision = 2;
                return Json(new {code = 0});
            }
            return call.Path.EndsWith("/head") ? Json(new MetadataSourceManifest(Owner, Start with {Sequence = revision})) : Page(call, revision);
        };
        var write = Service().ExecuteAsync(Owner.ServerId, Write(), default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await SeedAsync();
        Assert.IsFalse(_wire.Calls.Any(c => c.Path.EndsWith("/head")));
        Assert.IsTrue((await _store.GetSyncStateAsync(Owner)).RefreshPending);
        release.SetResult();
        var response = await write;
        Assert.IsTrue((bool)JObject.Parse(response.Body)["metadataPending"]!);
        var refreshed = await Service().ExecuteAsync(Owner.ServerId, Get(), default);
        StringAssert.Contains(refreshed.Body, "revision-2");
        Assert.IsFalse((await _store.GetSyncStateAsync(Owner)).RefreshPending);
        Assert.AreEqual(1, _wire.Calls.Count(c => c.Method == HttpMethod.Put));
    }

    [TestMethod]
    public async Task Lost_write_response_survives_restart_and_failed_refresh_never_returns_old_metadata_or_retries_write()
    {
        await SeedAsync();
        var normal = _wire.Handle;
        _wire.Handle = call => call.Method == HttpMethod.Put ? throw new HttpRequestException("response lost") : normal(call);
        await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Write(), default));
        Assert.IsTrue((await _store.GetSyncStateAsync(Owner)).RefreshPending);
        _store = new(_root);
        _registry = new(new DataDirectory(_root));
        _coordinator = new(_transport, _store, _registry);
        _wire.Handle = _ => throw new HttpRequestException("still offline");
        var pending = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Get(), default));
        Assert.AreEqual("MetadataRefreshPending", pending.ErrorCode);
        Assert.AreEqual(1, _wire.Calls.Count(c => c.Method == HttpMethod.Put));
        Assert.AreEqual(1L, (await _store.GetRowsByIdsAsync(Owner, [1])).Single().Revision);
        _wire.Handle = call => Task.FromResult(call.Path.EndsWith("/head")
            ? Json(new MetadataSourceManifest(Owner, Start with {Sequence = 2})) : Page(call, 2));
        StringAssert.Contains((await Service().ExecuteAsync(Owner.ServerId, Get(), default)).Body, "revision-2");
        Assert.IsFalse((await _store.GetSyncStateAsync(Owner)).RefreshPending);
        Assert.AreEqual(1, _wire.Calls.Count(c => c.Method == HttpMethod.Put));
    }

    [TestMethod]
    public async Task Paused_refresh_keeps_pending_until_user_resumes_without_discarding_previous_complete_copy()
    {
        await SeedAsync();
        await using (await _coordinator.BeginWriteAsync(Owner.ServerId, Owner.LibraryEpoch, default)) { }
        await _coordinator.ControlAsync(Owner.ServerId, Owner.LibraryEpoch, true, default);
        var pending = await Assert.ThrowsExactlyAsync<FederationAccessException>(() => Service().ExecuteAsync(Owner.ServerId, Get(), default));
        Assert.AreEqual("MetadataRefreshPending", pending.ErrorCode);
        Assert.IsTrue((await _store.GetStatusAsync(Owner)).Checkpoint!.Complete);
        await _coordinator.ControlAsync(Owner.ServerId, Owner.LibraryEpoch, false, default);
        StringAssert.Contains((await Service().ExecuteAsync(Owner.ServerId, Get(), default)).Body, "revision-1");
        Assert.IsFalse((await _store.GetSyncStateAsync(Owner)).RefreshPending);
    }

    private sealed class DataDirectory(string path) : IFederationDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }
    private sealed class Identity : INodeIdentityProvider
    {
        public Task<NodeIdentity> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NodeIdentity("manager-device", "manager-epoch", "Manager"));
    }
    public sealed record Call(HttpMethod Method, string Path, string? Json);
    public class Wire : DispatchProxy
    {
        public ConcurrentQueue<Call> Calls = new();
        public ManagedServerView[] Connections = [FakeManagedServerService.Server with {ServerId = Owner.ServerId}];
        public Func<Call, Task<ManagedServerResponse>> Handle = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IManagedServerService.GetAsync))
                return Task.FromResult(new ManagedServersView(true, Connections, []));
            if (method.Name != nameof(IManagedServerService.SendAsync)) throw new NotSupportedException();
            var call = new Call((HttpMethod)args![1]!, (string)args[2]!, (string?)args[3]);
            Calls.Enqueue(call);
            return Handle(call);
        }
    }
}
