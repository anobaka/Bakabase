using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Components.DataSync;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Notification.Abstractions.Models.Domain;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Tests.DataSync.Runtime;
using Bakabase.TestKit.DataSync;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Bakabase.Tests.DataSync.Api;

/// <summary>
/// The real facade (<see cref="DataSyncService"/>) over the harness's real composition (<see cref="DataSyncHarness"/>),
/// with the real notifier and hub publisher, a gate the test can hold and a notification service that keeps what it is
/// given. Controllers are built per caller.
/// </summary>
internal sealed class DataSyncApiHarness : DataSyncHarness
{
    public TestGateEntry Gate { get; } = new();
    public TestDataSyncHostKind HostKind { get; } = new();
    public FakeNotificationService Notifications { get; }

    public DataSyncNotifier Notifier => Provider.GetRequiredService<DataSyncNotifier>();

    private DataSyncApiHarness() => Notifications = new FakeNotificationService(Clock);

    public static async Task<DataSyncApiHarness> CreateAsync(Action<IServiceCollection>? configure = null,
        bool registerFetchTask = true)
    {
        var h = new DataSyncApiHarness();
        return await BuildAsync(h, registerFetchTask, s =>
        {
            s.AddSingleton(KeyArgsLocalizer.Create());
            s.AddSingleton<IDataSyncGateEntry>(h.Gate);
            s.AddSingleton<IDataSyncHostKind>(h.HostKind);
            s.AddSingleton<INotificationService>(h.Notifications);
        }, configure);
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

    // ---- fixtures ------------------------------------------------------------------------------------------------

    /// <summary>An open item, stored the way the store stores it (times without a kind).</summary>
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
                or DataSyncInboxItemType.SuspectedLostUpdate
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

    /// <summary>A synced entity row of the kind; nothing checks that the definition exists.</summary>
    public DataSyncEntityDbModel AddEntity(string localKey, string syncKey, string kind = DataSyncKindIds.CustomProperty,
        Action<DataSyncEntityDbModel>? configure = null)
    {
        var entity = new DataSyncEntityDbModel
        {
            Kind = kind, LocalKey = localKey, SyncKey = syncKey, OriginNodeId = Self.NodeId, LocalHash = "sha256:l",
            SharedHash = "sha256:s", Seq = 1, CreatedAtUtc = Clock.UtcNow, UpdatedAtUtc = Clock.UtcNow,
        };
        configure?.Invoke(entity);
        return Store.AddEntity(entity);
    }

    /// <summary>A real custom property; its id is its local key.</summary>
    public async Task<string> AddPropertyAsync(string name, PropertyType type = PropertyType.SingleLineText)
    {
        await using var scope = Provider.CreateAsyncScope();
        var property = await scope.ServiceProvider.GetRequiredService<ICustomPropertyService>()
            .Add(new CustomPropertyAddOrPutDto { Name = name, Type = type });
        return property.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Key(int n) => n.ToString("x32");
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

/// <summary>Keeps every notification, stamped with the test's clock, and every id marked read.</summary>
internal sealed class FakeNotificationService(IDataSyncClock clock) : INotificationService
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
