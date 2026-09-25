using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Tests.DataSync.Runtime;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The real facade (<see cref="DataSyncService"/>) and the runtime as <c>AddDataSyncRuntime()</c> composes them — with
/// the real notifier and hub publisher — over fakes of packages C and D, a gate the test can hold, a manual clock and
/// a notification service that keeps what it is given. Controllers are built per caller.
/// </summary>
internal sealed class DataSyncApiHarness : IAsyncDisposable
{
    public ManualDataSyncClock Clock { get; } = new();
    public FakeDataSyncStore Store { get; } = new();
    public FakeDataSyncPeerClient Peers { get; } = new();
    public FakeDataSyncGrantService Grants { get; } = new();
    public FakeReviewStore Reviews { get; } = new();
    public FakeActorGuard Guard { get; } = new();
    public FakeKindPageReader Reader { get; } = new();
    public TestGateEntry Gate { get; } = new();
    public FakeHostKind HostKind { get; } = new();
    public FakeNotificationService Notifications { get; }
    public FakeKind CustomProperties { get; } = new(DataSyncKindIds.CustomProperty, supportsChildrenLocal: true);
    public FakeKind ExtensionGroups { get; } = new(DataSyncKindIds.ExtensionGroup, supportsChildrenLocal: false);
    public FakeUndoPreviewer UndoPreviewer { get; } = new();
    public FakeRefresher Refresher { get; } = new();
    public FakeHostLifetime Lifetime { get; } = new();
    public ServiceProvider Provider { get; private set; } = null!;

    public FakeApplyRunner Runner => (FakeApplyRunner) Provider.GetRequiredService<IDataSyncApplyRunner>();
    public BTaskManager Btm => Provider.GetRequiredService<BTaskManager>();
    public DataSyncLinkService Links => Provider.GetRequiredService<DataSyncLinkService>();
    public DataSyncNotifier Notifier => Provider.GetRequiredService<DataSyncNotifier>();
    public DataSyncRuntimeState State => Provider.GetRequiredService<DataSyncRuntimeState>();

    private DataSyncApiHarness()
    {
        Notifications = new FakeNotificationService(Clock);
        Store.Now = () => Clock.UtcNow;
    }

    public static async Task<DataSyncApiHarness> CreateAsync(Action<IServiceCollection>? configure = null,
        bool registerFetchTask = true)
    {
        var h = new DataSyncApiHarness();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(KeyArgsLocalizer.Create());
        services.AddSingleton<IOptionsMonitor<TaskOptions>>(new TestOptionsMonitor<TaskOptions>(new TaskOptions()));
        services.AddSingleton(sp => new AspNetCoreOptionsManager<TaskOptions>("task", "task",
            sp.GetRequiredService<IOptionsMonitor<TaskOptions>>(),
            sp.GetRequiredService<ILogger<AspNetCoreOptionsManager<TaskOptions>>>()));
        services.AddSingleton<BTaskManager>();
        services.AddSingleton<IBTaskEventHandler, TestBTaskEventHandler>();
        services.AddSingleton<IHostApplicationLifetime>(h.Lifetime);

        services.AddSingleton<IDataSyncClock>(h.Clock);
        services.AddScoped<IDataSyncKindPageReader>(_ => h.Reader);

        // Package C, package D and the host.
        services.AddScoped<IDataSyncStore>(_ => h.Store);
        services.AddSingleton<IDataSyncPeerClient>(h.Peers);
        services.AddSingleton<IDataSyncGrantService>(h.Grants);
        services.AddSingleton<IDataSyncReviewStore>(h.Reviews);
        services.AddSingleton<IDataSyncActorGuard>(h.Guard);
        services.AddSingleton<IDataSyncApplyRunner, FakeApplyRunner>();
        services.AddSingleton<IDataSyncGateEntry>(h.Gate);
        services.AddSingleton<IDataSyncHostKind>(h.HostKind);
        services.AddSingleton<IDataSyncDeviceIdentity>(new FakeDeviceIdentity());
        services.AddSingleton<INotificationService>(h.Notifications);
        services.AddSingleton<IDataSyncUndoPreviewer>(h.UndoPreviewer);
        services.AddSingleton<IDataSyncRefresher>(h.Refresher);
        services.AddScoped<IDataSyncKind>(_ => h.ExtensionGroups);
        services.AddScoped<IDataSyncKind>(_ => h.CustomProperties);
        configure?.Invoke(services);

        services.AddDataSyncRuntime();
        h.Provider = services.BuildServiceProvider(new ServiceProviderOptions
            { ValidateScopes = true, ValidateOnBuild = true });
        if (registerFetchTask)
        {
            var task = ActivatorUtilities.CreateInstance<DataSyncFetchTask>(h.Provider);
            await h.Btm.Enqueue(BTaskBuilder.Create(task.Id, task.GetName).Persistent().Every(task.GetInterval())
                .ConflictsWith(task.ConflictKeys).Run(task.RunAsync));
        }

        return h;
    }

    /// <summary>A controller for one request, as the remote-access gate admitted <paramref name="caller"/>.</summary>
    public (DataSyncController Controller, AsyncServiceScope Scope) Controller(RemoteAccessContext? caller)
    {
        var scope = Provider.CreateAsyncScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (caller is not null) http.SetRemoteAccessContext(caller);
        var controller = new DataSyncController(scope.ServiceProvider.GetRequiredService<IDataSyncService>())
            { ControllerContext = new ControllerContext { HttpContext = http } };
        return (controller, scope);
    }

    /// <summary>Runs one call through a controller of its own scope.</summary>
    public async Task<T> CallAsync<T>(RemoteAccessContext? caller, Func<DataSyncController, Task<T>> call)
    {
        var (controller, scope) = Controller(caller);
        await using (scope)
        {
            return await call(controller);
        }
    }

    public IDataSyncService Service(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDataSyncService>();

    // ---- fixtures ------------------------------------------------------------------------------------------------

    public DataSyncLinkDbModel AddLink(string peerNodeId, Action<DataSyncLinkDbModel>? configure = null)
    {
        var link = new DataSyncLinkDbModel
        {
            PeerNodeId = peerNodeId,
            PeerName = "Peer " + peerNodeId,
            Mode = DataSyncLinkMode.TwoWay,
            State = DataSyncLinkState.Active,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            PeerLibraryEpoch = "epoch-1",
            PeerActorId = "0123456789abcdef",
            FirstContactKindsJson = "[\"extensionGroup\",\"customProperty\"]",
            FirstContactCompletedAtUtc = Clock.UtcNow,
            LastSyncedAtUtc = DateTime.SpecifyKind(Clock.UtcNow, DateTimeKind.Unspecified),
            NextAttemptAtUtc = DateTime.SpecifyKind(Clock.UtcNow.AddMinutes(1), DateTimeKind.Unspecified),
            CreatedAtUtc = Clock.UtcNow,
            UpdatedAtUtc = Clock.UtcNow,
        };
        configure?.Invoke(link);
        Grants.Outbound.Add(peerNodeId);
        if (!Peers.Peers.ContainsKey(peerNodeId)) Peers.Add(peerNodeId);
        return Store.Add(link);
    }

    /// <summary>An open item, stored the way package C stores it (times without a kind).</summary>
    public DataSyncInboxItemDbModel AddItem(DataSyncInboxItemType type, int? linkId, string syncKey,
        string subjectPath = "name", DataSyncInboxPayload? payload = null, string localKey = "12",
        string kind = DataSyncKindIds.CustomProperty)
    {
        var link = linkId is { } id ? Store.Get(id) : null;
        return Store.AddItem(new DataSyncInboxItemDbModel
        {
            LinkId = linkId,
            PeerNodeId = link?.PeerNodeId,
            Kind = kind,
            SyncKey = syncKey,
            LocalKey = localKey,
            Type = type,
            Origin = type is DataSyncInboxItemType.ChildDeletedInUse or DataSyncInboxItemType.MassChildDeletion
                or DataSyncInboxItemType.SuspectedLostUpdate or DataSyncInboxItemType.LargeChange
                ? DataSyncInboxItemOrigin.State
                : DataSyncInboxItemOrigin.Merger,
            SubjectPath = subjectPath,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload ?? Payload(), DataSyncJson.Options),
            Token = $"token-{Guid.NewGuid():N}"[..20],
            CreatedAtUtc = DateTime.SpecifyKind(Clock.UtcNow, DateTimeKind.Unspecified),
            UpdatedAtUtc = DateTime.SpecifyKind(Clock.UtcNow, DateTimeKind.Unspecified),
        });
    }

    public static DataSyncInboxPayload Payload(string name = "Genre", IReadOnlyList<DataSyncInboxCandidate>? candidates = null,
        IReadOnlyList<DataSyncInboxRecordRef>? records = null, string? remoteSubtype = null, string? localSubtype = null) =>
        new(name, localSubtype, "Peer", null, null,
            [new DataSyncFieldOutcome("name", DataSyncFieldResolution.Conflict, null, null, null, null)], null, null, null,
            0, remoteSubtype, localSubtype, candidates, records, null);

    public DataSyncEntityDbModel AddEntity(string localKey, string syncKey, string kind = DataSyncKindIds.CustomProperty,
        Action<DataSyncEntityDbModel>? configure = null)
    {
        var entity = new DataSyncEntityDbModel
        {
            Kind = kind, LocalKey = localKey, SyncKey = syncKey, OriginNodeId = "node-self", LocalHash = "sha256:l",
            SharedHash = "sha256:s", Seq = 1, CreatedAtUtc = Clock.UtcNow, UpdatedAtUtc = Clock.UtcNow,
        };
        configure?.Invoke(entity);
        Store.Entities.Add(entity);
        return entity;
    }

    public static string Key(int n) => n.ToString("x32");

    public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
}

// ---- callers -------------------------------------------------------------------------------------------------------

internal static class Callers
{
    public static RemoteAccessContext Loopback => new() { IsLoopback = true, Mode = RemoteAccessMode.Disabled };

    public static RemoteAccessContext Paired => new()
    {
        IsLoopback = false, Mode = RemoteAccessMode.Enabled,
        Device = new RemoteDevice { Id = "device-1", Name = "Desktop", Key = "key", CreatedAt = DateTime.UtcNow },
    };

    /// <summary>A LAN browser admitted only because the mode is Unrestricted.</summary>
    public static RemoteAccessContext UnpairedUnrestricted => new()
        { IsLoopback = false, Mode = RemoteAccessMode.Unrestricted };
}

// ---- fakes of the host ---------------------------------------------------------------------------------------------

/// <summary>
/// The DataSyncGate as a test holds it: a held gate answers a limited wait at once (a request answers Busy without
/// waiting its 30 s); an unlimited wait waits.
/// </summary>
internal sealed class TestGateEntry : IDataSyncGateEntry
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    public int Entered;

    public bool IsHeld => _semaphore.CurrentCount == 0;

    public async Task<DataSyncGateLease?> TryEnterAsync(TimeSpan? timeout, CancellationToken ct)
    {
        if (timeout is not null)
        {
            if (!await _semaphore.WaitAsync(TimeSpan.Zero, ct)) return null;
        }
        else
        {
            await _semaphore.WaitAsync(ct);
        }

        Interlocked.Increment(ref Entered);
        return new Lease(_semaphore);
    }

    /// <summary>Holds the gate as an apply would, until disposed.</summary>
    public IDisposable Hold()
    {
        if (!_semaphore.Wait(0)) throw new InvalidOperationException("The gate is already held.");
        return new Lease(_semaphore);
    }

    private sealed class Lease(SemaphoreSlim semaphore) : DataSyncGateLease
    {
        private int _released;
        public override bool IsHeld => Volatile.Read(ref _released) == 0;

        public override void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
        }
    }
}

internal sealed class FakeHostKind : IDataSyncHostKind
{
    public bool IsHeadless { get; set; }
}

internal sealed class FakeDeviceIdentity : IDataSyncDeviceIdentity
{
    public Task<DataSyncDevice> GetAsync(CancellationToken ct) =>
        Task.FromResult(new DataSyncDevice("node-self", "epoch-self", "This PC"));
}

/// <summary>Keeps every notification, stamped with the test's clock, and every id marked read.</summary>
internal sealed class FakeNotificationService(ManualDataSyncClock clock) : INotificationService
{
    private readonly List<NotificationRecord> _records = [];
    public ConcurrentQueue<int[]> MarkedRead { get; } = new();

    public IReadOnlyList<NotificationRecord> Records
    {
        get
        {
            lock (_records) return _records.ToList();
        }
    }

    public Task<NotificationRecord> CreateAsync(NotificationCreationInputModel input)
    {
        lock (_records)
        {
            var record = new NotificationRecord
            {
                Id = _records.Count + 1, Source = input.Source, Title = input.Title, Body = input.Body,
                PayloadJson = input.PayloadJson, Severity = input.Severity, CreatedAt = clock.UtcNow,
            };
            _records.Add(record);
            return Task.FromResult(record);
        }
    }

    public Task<SearchResponse<NotificationRecord>> SearchAsync(NotificationSearchInputModel input)
    {
        lock (_records)
        {
            var found = _records.Where(r => (input.Source is null || r.Source == input.Source) &&
                                            (!input.UnreadOnly || !r.IsRead))
                .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Take(input.PageSize).ToList();
            return Task.FromResult(new SearchResponse<NotificationRecord>(found, found.Count, 1, input.PageSize));
        }
    }

    public Task<int> GetUnreadCountAsync()
    {
        lock (_records) return Task.FromResult(_records.Count(r => !r.IsRead));
    }

    /// <summary>An empty list would mark every unread notification; data sync must never send one.</summary>
    public Task MarkAsReadAsync(int[]? ids)
    {
        if (ids is not { Length: > 0 }) throw new AssertFailedException("MarkAsReadAsync with no ids marks everything.");
        MarkedRead.Enqueue(ids);
        lock (_records)
        {
            foreach (var record in _records.Where(r => ids.Contains(r.Id))) record.ReadAt ??= clock.UtcNow;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(int[] ids) => Task.CompletedTask;
    public Task ClearReadAsync() => Task.CompletedTask;

    public static string? CaseOf(NotificationRecord record) =>
        JsonNode.Parse(record.PayloadJson!)?["case"]?.GetValue<string>();

    public static string? RouteOf(NotificationRecord record) =>
        JsonNode.Parse(record.PayloadJson!)?["route"]?.GetValue<string>();
}

/// <summary>
/// A localizer that shows what it was asked: <c>Key(arg1|arg2)</c>, so tests can see a notification's key and its
/// arguments.
/// </summary>
public class KeyArgsLocalizer : DispatchProxy
{
    public static IBakabaseLocalizer Create() => DispatchProxy.Create<IBakabaseLocalizer, KeyArgsLocalizer>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == "get_Item" && args is { Length: > 0 } && args[0] is string key)
        {
            var arguments = args.Length > 1 && args[1] is object?[] values ? values : [];
            var text = arguments.Length == 0 ? key : $"{key}({string.Join("|", arguments)})";
            return new LocalizedString(key, text);
        }

        return targetMethod?.ReturnType == typeof(string) ? targetMethod.Name : null;
    }
}

// ---- fakes of packages B and C that the facade reads ---------------------------------------------------------------

/// <summary>A kind whose content is a JSON object with a <c>name</c>; its comparison form is the content itself.</summary>
internal sealed class FakeKind(string kind, bool supportsChildrenLocal) : IDataSyncKind
{
    public IDataSyncKindCodec Codec { get; } = new FakeCodec(kind, supportsChildrenLocal);
    public Dictionary<string, JsonObject> Contents { get; } = new();
    public ConcurrentQueue<(string LocalKey, string Subtype)> Previewed { get; } = new();

    public Task<IReadOnlyList<LocalEntity>> ReadAsync(IReadOnlyCollection<string>? localKeys, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LocalEntity>>(Contents
            .Where(c => localKeys is null || localKeys.Contains(c.Key))
            .Select((c, i) => new LocalEntity(c.Key, null, i, c.Value)).ToList());

    public Task<DataSyncTypeChangePreview> PreviewSubtypeChangeAsync(string localKey, string subtype,
        CancellationToken ct)
    {
        Previewed.Enqueue((localKey, subtype));
        return Task.FromResult(new DataSyncTypeChangePreview("MultipleChoice", subtype, 1234, 1234, 210, []));
    }

    public Task<IReadOnlyDictionary<string, JsonObject>> CapturePreImageAsync(IReadOnlyCollection<string> localKeys,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<ApplyBatchOutcome> ApplyAsync(ApplyBatch batch, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, EntityUsage>> GetUsageAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> childIdsByLocalKey, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task RestoreAsync(string localKey, JsonObject preImage, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task DeleteAsync(string localKey, CancellationToken ct) => throw new NotSupportedException();

    public void ResetCaches()
    {
    }

    public Task<IReadOnlyList<string>> ReadOrderAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task ApplyOrderAsync(IReadOnlyList<string> syncedLocalKeysInSharedOrder, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ChangeSubtypeAsync(string localKey, string subtype, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<IReadOnlyDictionary<string, string>> ReadRawHashesAsync(CancellationToken ct) =>
        throw new NotSupportedException();

    private sealed class FakeCodec(string kind, bool supportsChildrenLocal) : IDataSyncKindCodec
    {
        public DataSyncKindDescriptor Descriptor { get; } = new(kind, 1, [], typeof(JsonObject), false,
            kind == DataSyncKindIds.CustomProperty, true, supportsChildrenLocal, "option");

        public int ComparisonFormVersion => 1;
        public JsonObject Upgrade(JsonObject content, int fromSchemaVersion) => content;

        public CodecReadResult Read(JsonObject content, DataSyncLimits limits) =>
            new(content.DeepClone().AsObject(), null, [], []);

        public object ReadLocal(JsonObject content) => content.DeepClone().AsObject();
        public JsonObject Write(object content) => ((JsonObject) content).DeepClone().AsObject();
        public string NameOf(object content) => ((JsonObject) content)["name"]!.GetValue<string>();
        public string? SubtypeOf(object content) => null;
        public int ChildCountOf(object content) => 0;
        public DataSyncNaturalMatch MatchNatural(object incoming, object local) => DataSyncNaturalMatch.None;
        public EntityDiff Diff(object local, object incoming) => throw new NotSupportedException();

        public MergeResult Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds) =>
            throw new NotSupportedException();

        public MergeResult PrepareCreate(object incoming, string? nameOverride) => throw new NotSupportedException();

        public DataSyncPublishable Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal) =>
            new(localContent, 0, []);

        public JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal) =>
            ((JsonObject) publishedContent).DeepClone().AsObject();

        public IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input) =>
            throw new NotSupportedException();

        public DataSyncMerge3Result Merge3(DataSyncMerge3Input input) => throw new NotSupportedException();
        public IReadOnlyList<DataSyncChildInfo> ChildrenOf(object content) => [];
    }
}

internal sealed class FakeUndoPreviewer : IDataSyncUndoPreviewer
{
    public DataSyncUndoPreview Preview { get; set; } = new(true, [], null);
    public ConcurrentQueue<int> Previewed { get; } = new();

    public Task<DataSyncUndoPreview> PreviewAsync(DataSyncApplyLogDbModel entry, CancellationToken ct)
    {
        Previewed.Enqueue(entry.Id);
        return Task.FromResult(Preview);
    }
}

/// <summary>Records every Refresh and the lease it was given; can throw once that the actor changed.</summary>
internal sealed class FakeRefresher : IDataSyncRefresher
{
    public ConcurrentQueue<(IReadOnlyCollection<string> Kinds, bool LeaseHeld)> Calls { get; } = new();
    public int ActorChangesLeft;

    public Task<DataSyncRefreshResult> RefreshAsync(DataSyncGateLease lease, IReadOnlyCollection<string> kinds,
        bool collectPublished, CancellationToken ct)
    {
        Calls.Enqueue((kinds, lease.IsHeld));
        if (Interlocked.Decrement(ref ActorChangesLeft) >= 0) throw new DataSyncActorChangedException();
        return Task.FromResult(new DataSyncRefreshResult(1, 0, new DataSyncActorId("a1a1a1a1a1a1a1a1"), false, null));
    }
}
