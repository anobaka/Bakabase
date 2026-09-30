using System.Text.Json.Nodes;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.TestKit.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.DataSync;

/// <summary>
/// One TestKit provider (real SQLite, AddDataSync registered) with helpers to build data sync state directly
/// through the stores. Every provider has its own database and data sync folders.
/// </summary>
internal sealed class DataSyncStoreFixture
{
    public const string Kind = DataSyncKindIds.CustomProperty;
    public const string ActorA = "aaaaaaaaaaaaaaaa";
    public const string ActorB = "bbbbbbbbbbbbbbbb";
    public const string ActorC = "cccccccccccccccc";

    private DataSyncStoreFixture(IServiceProvider services) => Services = services;

    public IServiceProvider Services { get; }
    public DataSyncStore Store => Services.GetRequiredService<DataSyncStore>();
    public DataSyncIdentityStore Identity => Services.GetRequiredService<DataSyncIdentityStore>();
    public BakabaseDbContext Db => Services.GetRequiredService<BakabaseDbContext>();
    public DataSyncLocalStateDbModel State { get; private set; } = null!;
    public string SelfNodeId => State.NodeId;

    public static async Task<DataSyncStoreFixture> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var fixture = new DataSyncStoreFixture(await TestServiceBuilder.BuildServiceProvider(configure));
        var device = await fixture.Services.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(default);
        var state = DataSyncLocalStateRows.New(device, DateTime.UtcNow);
        await fixture.Store.SaveLocalStateAsync(state, default);
        fixture.State = state;
        return fixture;
    }

    public static DataSyncVersionVector Vv(params (string Actor, long Counter)[] counters) =>
        counters.Aggregate(DataSyncVersionVector.Empty, (vv, c) => vv.With(new DataSyncActorId(c.Actor), c.Counter));

    public static SyncKey Key(string value) => new(value);

    public static EntityKeys Keys(params string[] keys) => new(keys.Select(k => new SyncKey(k)).ToList());

    public static string NewKey() => SyncKey.New().Value;

    /// <summary>A new row template: the caller's content columns, never keys.</summary>
    public DataSyncEntityDbModel Row(string localKey, DataSyncVersionVector? vv = null,
        DataSyncEntitySyncState state = DataSyncEntitySyncState.Synced, string? fingerprint = null,
        string kind = Kind) =>
        new()
        {
            Kind = kind,
            LocalKey = localKey,
            OriginNodeId = SelfNodeId,
            Fingerprint = fingerprint,
            LocalHash = ContentHash.Of(JsonValue.Create(localKey)),
            RawHash = "raw:" + localKey,
            SharedHash = ContentHash.Of(JsonValue.Create("shared:" + localKey)),
            VvJson = (vv ?? Vv((ActorA, 1))).ToCanonicalString(),
            State = state,
        };

    public Task<DataSyncEntityDbModel> LiveAsync(string localKey, DataSyncVersionVector? vv = null,
        DataSyncEntitySyncState state = DataSyncEntitySyncState.Synced, string kind = Kind) =>
        Identity.InsertFreshAsync(Row(localKey, vv, state, kind: kind), default);

    public async Task<DataSyncEntityDbModel> LiveWithAliasesAsync(string localKey, params string[] aliases)
    {
        var row = await LiveAsync(localKey);
        await Identity.AddAliasesAsync(row, aliases.Select(Key), null, default);
        return row;
    }

    public async Task<DataSyncEntityDbModel> TombstoneAsync(DataSyncEntityDbModel row, DataSyncVersionVector? vv = null,
        bool served = true)
    {
        var tombstoneVv = vv ?? DataSyncVersionVector.ParseStored(row.VvJson).With(new DataSyncActorId(ActorC),
            DataSyncVersionVector.ParseStored(row.VvJson).Counters.GetValueOrDefault(ActorC) + 1);
        await Identity.TombstoneAsync(row,
            new DataSyncTombstoneWrite(tombstoneVv, DataSyncTombstoneKind.Deleted, served, null), default);
        return row;
    }

    public async Task<DataSyncEntityDbModel> ReloadAsync(DataSyncEntityDbModel row)
    {
        await Db.Entry(row).ReloadAsync();
        return row;
    }

    public Task<DataSyncEntityDbModel?> ByPrimaryAsync(string key, string kind = Kind) =>
        Db.DataSyncEntities.AsNoTracking().SingleOrDefaultAsync(e => e.Kind == kind && e.SyncKey == key);

    public Task<List<DataSyncKeyAliasDbModel>> AliasesAsync(string kind = Kind) =>
        Db.DataSyncKeyAliases.AsNoTracking().Where(a => a.Kind == kind).OrderBy(a => a.AliasKey).ToListAsync();

    public async Task<IReadOnlyList<string>> KeysOfAsync(DataSyncEntityDbModel row)
    {
        var index = await Identity.GetKeyIndexAsync(row.Kind, null, default);
        return index.Entities.Single(e => e.Id == row.Id).Keys.All.Select(k => k.Value).ToList();
    }

    public async Task<DataSyncLinkDbModel> LinkAsync(string peer, DataSyncLinkMode mode = DataSyncLinkMode.TwoWay,
        DataSyncLinkState state = DataSyncLinkState.Active) =>
        await Store.AddLinkAsync(new DataSyncLinkDbModel
        {
            PeerNodeId = peer, PeerName = peer.ToUpperInvariant(), Mode = mode, State = state,
            Initiator = DataSyncLinkInitiator.ThisDevice,
        }, default);

    public static DataSyncWireRecord Record(IEnumerable<string> keys, DataSyncVersionVector vv, long seq = 1,
        JsonObject? content = null, bool deleted = false, DataSyncEditorRef? editedBy = null) =>
        new(keys.ToList(), "origin-node", seq, vv, editedBy, deleted, 1, null,
            deleted ? null : content ?? new JsonObject {["name"] = "Genre"},
            deleted ? null : ContentHash.Of(content ?? new JsonObject {["name"] = "Genre"}), null);

    public static DataSyncPendingRecord Pending(DataSyncWireRecord record, DataSyncPendingReason reason,
        DataSyncMergeFlags? flags = null) =>
        new(record, "rec:" + record.Keys[0] + ":" + record.Seq, reason, flags ?? DataSyncMergeFlags.None);

    public static DataSyncInboxPayload Payload(string name = "Genre", string? peerName = "PC-1",
        IReadOnlyList<DataSyncInboxCandidate>? candidates = null,
        IReadOnlyList<DataSyncInboxRecordRef>? records = null) =>
        new(name, null, peerName, null, null, [], null, null, null, 0, null, null, candidates, records, null);

    public static DataSyncInboxDraft Draft(string kind, string key, DataSyncInboxItemType type,
        DataSyncInboxItemOrigin origin, string subjectPath = "", DataSyncVersionVector? recordVv = null,
        string token = "t1", string? localKey = null, DataSyncInboxPayload? payload = null,
        string? recordHash = "rec") =>
        new(kind, new SyncKey(key), localKey, type, origin, subjectPath, payload ?? Payload(), recordHash, recordVv,
            null, DataSyncMergeFlags.None, token);

    public Task<List<DataSyncInboxItemDbModel>> ItemsAsync() =>
        Db.DataSyncInboxItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync();

    /// <summary>
    /// v3.1 §5.3: within one kind, every key is exactly one of a live primary, a tombstoned primary or an alias,
    /// and every alias points at the primary of an existing row.
    /// </summary>
    public async Task AssertKeyInvariantAsync(string kind = Kind)
    {
        var rows = await Db.DataSyncEntities.AsNoTracking().Where(e => e.Kind == kind).ToListAsync();
        var aliases = await AliasesAsync(kind);
        var primaries = rows.Select(r => r.SyncKey).ToList();
        Assert.AreEqual(primaries.Count, primaries.Distinct().Count(), "a primary key is used twice");
        Assert.AreEqual(aliases.Count, aliases.Select(a => a.AliasKey).Distinct().Count(), "an alias is used twice");
        foreach (var alias in aliases)
        {
            Assert.IsFalse(primaries.Contains(alias.AliasKey), $"alias {alias.AliasKey} is also a primary");
            Assert.IsTrue(primaries.Contains(alias.SyncKey), $"alias {alias.AliasKey} points at no row");
        }
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
