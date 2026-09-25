using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// One TestKit provider (real SQLite) with a memory kind, a clock the test moves, a device identity the test can
/// change and a move detector the test answers — everything Refresh and the actor guard read.
/// </summary>
internal sealed class DataSyncRefreshFixture
{
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private DataSyncRefreshFixture(IServiceProvider services, MemoryDataSyncKind kind, FakeOrderMoveDetector detector,
        ManualTimeProvider clock, TestDataSyncDeviceIdentity identity)
    {
        Services = services;
        Kind = kind;
        Detector = detector;
        Clock = clock;
        Identity = identity;
    }

    public IServiceProvider Services { get; }
    public MemoryDataSyncKind Kind { get; }
    public string KindId => Kind.Codec.Descriptor.Kind;
    public FakeOrderMoveDetector Detector { get; }
    public ManualTimeProvider Clock { get; }
    public TestDataSyncDeviceIdentity Identity { get; }

    public DataSyncGate Gate => Services.GetRequiredService<DataSyncGate>();
    public DataSyncActorGuard Guard => Services.GetRequiredService<DataSyncActorGuard>();
    public DataSyncStore Store => Services.GetRequiredService<DataSyncStore>();
    public DataSyncRefresher Refresher => Services.GetRequiredService<DataSyncRefresher>();
    public DataSyncActorWatermarkFile Watermark => Services.GetRequiredService<DataSyncActorWatermarkFile>();
    public DataSyncRefreshCoordinator Coordinator => Services.GetRequiredService<DataSyncRefreshCoordinator>();
    public BakabaseDbContext Db => Services.GetRequiredService<BakabaseDbContext>();

    public static async Task<DataSyncRefreshFixture> CreateAsync(bool hasOrder = false, bool verified = true,
        Action<IServiceCollection>? configure = null, string kind = DataSyncKindIds.CustomProperty)
    {
        var memoryKind = new MemoryDataSyncKind(kind, hasOrder);
        var detector = new FakeOrderMoveDetector();
        var clock = new ManualTimeProvider(Start);
        var identity = new TestDataSyncDeviceIdentity();
        var services = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton<IDataSyncDeviceIdentity>(identity);
            s.AddScoped<IDataSyncKind>(_ => memoryKind);
            s.AddSingleton<IDataSyncOrderMoveDetector>(detector);
            configure?.Invoke(s);
        });
        var fixture = new DataSyncRefreshFixture(services, memoryKind, detector, clock, identity);
        if (verified) fixture.Guard.MarkVerified();
        return fixture;
    }

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public async Task<DataSyncRefreshResult> RefreshAsync(bool collectPublished = false,
        DataSyncRefreshOptions? options = null)
    {
        using var lease = await Gate.EnterAsync(null, default);
        return await Refresher.RefreshAsync(lease, [KindId], collectPublished, options ?? DataSyncRefreshOptions.None,
            default);
    }

    public async Task<DataSyncPauseReason?> CheckAsync()
    {
        using var lease = await Gate.EnterAsync(null, default);
        return await Guard.CheckAsync(lease, default);
    }

    public Task<DataSyncEntityDbModel> RowAsync(string localKey) =>
        Db.DataSyncEntities.AsNoTracking()
            .SingleAsync(e => e.Kind == KindId && e.LocalKey == localKey && e.DeletedAtUtc == null);

    public Task<List<DataSyncEntityDbModel>> RowsAsync(bool includeTombstones = true) =>
        Db.DataSyncEntities.AsNoTracking().Where(e => e.Kind == KindId && (includeTombstones || e.DeletedAtUtc == null))
            .OrderBy(e => e.Id).ToListAsync();

    public Task<DataSyncLocalStateDbModel> StateAsync() => Db.DataSyncLocalStates.AsNoTracking().SingleAsync();

    public Task<List<DataSyncInboxItemDbModel>> ItemsAsync() =>
        Db.DataSyncInboxItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync();

    public async Task<DataSyncLinkDbModel> LinkAsync(string peer, DataSyncLinkState state = DataSyncLinkState.Active,
        DataSyncPauseReason? pausedReason = null) =>
        await Store.AddLinkAsync(new DataSyncLinkDbModel
        {
            PeerNodeId = peer, PeerName = peer.ToUpperInvariant(), Mode = DataSyncLinkMode.TwoWay, State = state,
            PausedReason = pausedReason, Initiator = DataSyncLinkInitiator.ThisDevice,
        }, default);

    public Task<DataSyncLinkDbModel> LinkRowAsync(string peer) =>
        Db.DataSyncLinks.AsNoTracking().SingleAsync(l => l.PeerNodeId == peer);

    public static DataSyncVersionVector Vv(string json) => DataSyncVersionVector.ParseStored(json);

    /// <summary>Stores an apply log with change lists, as the apply runner writes it.</summary>
    public async Task<int> LogApplyAsync(DataSyncHistoryKind kind, params DataSyncEntityChanges[] entities)
    {
        var log = new DataSyncApplyLogDbModel
        {
            Kind = kind,
            PeerNodeId = "peer-1",
            PeerName = "PC-1",
            AppliedAtUtc = Now,
            SummaryJson = "{}",
            ResultJson = new DataSyncApplyResultDocument([], entities).ToJson(),
            PreImageJson = "{}",
        };
        return await Store.AddHistoryAsync(log, default);
    }

    public static DataSyncChildInfo Child(string id, string label, string? parent = null) =>
        new(id, parent, new Bakabase.Modules.DataSync.Planning.DataSyncDisplayValue(label));

    public static JsonNode? Json(object? value) => value is null ? null : JsonSerializer.SerializeToNode(value);
}
