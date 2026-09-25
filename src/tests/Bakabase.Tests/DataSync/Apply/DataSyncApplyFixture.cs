using System.Text.Json.Nodes;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Kinds;
using Bakabase.InsideWorld.Business.Components.DataSync.Persistence;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Kinds.ExtensionGroups;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.TestKit.DataSync;
using Bakabase.TestKit.Utils;
using Bootstrap.Components.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.DataSync.Apply;

/// <summary>
/// One TestKit provider (real SQLite) with the <c>testItem</c> kind (in memory, with usage, values, a subtype and an
/// order) and the real extension group kind (the pure engine's codec over the real service), a clock the test moves, a
/// verified actor, and a simulated peer whose records a test hands the apply runner as staged pulls.
/// </summary>
internal sealed class DataSyncApplyFixture
{
    public const string Item = TestItemCodec.Kind;
    public const string Groups = DataSyncKindIds.ExtensionGroup;
    public static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private int _task;

    private DataSyncApplyFixture(IServiceProvider services, TestItemDataSyncKind kind, ManualTimeProvider clock,
        TestDataSyncDeviceIdentity identity)
    {
        Services = services;
        Kind = kind;
        Clock = clock;
        Identity = identity;
    }

    public IServiceProvider Services { get; }
    public TestItemDataSyncKind Kind { get; }
    public TestItemCodec Codec => Kind.TestCodec;
    public ManualTimeProvider Clock { get; }
    public TestDataSyncDeviceIdentity Identity { get; }
    public DataSyncApplyRunner Runner => Services.GetRequiredService<DataSyncApplyRunner>();
    public DataSyncGate Gate => Services.GetRequiredService<DataSyncGate>();
    public DataSyncActorGuard Guard => Services.GetRequiredService<DataSyncActorGuard>();
    public IDataSyncReviewStore Reviews => Services.GetRequiredService<IDataSyncReviewStore>();
    public DataSyncTaskRegistry Registry => Services.GetRequiredService<DataSyncTaskRegistry>();
    public IExtensionGroupService ExtensionGroups => Services.GetRequiredService<IExtensionGroupService>();
    public IDataSyncDataDirectory Directory => Services.GetRequiredService<IDataSyncDataDirectory>();
    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    /// <param name="extensionGroups">
    /// Registers the real extension group kind; a test that wraps it (<see cref="FailingDataSyncKind"/>) registers its
    /// own.
    /// </param>
    public static async Task<DataSyncApplyFixture> CreateAsync(Action<IServiceCollection>? configure = null,
        bool extensionGroups = true, IDataSyncDeviceIdentity? identityOverride = null)
    {
        var kind = new TestItemDataSyncKind();
        var clock = new ManualTimeProvider(Start);
        var identity = new TestDataSyncDeviceIdentity();
        var services = await TestServiceBuilder.BuildServiceProvider(s =>
        {
            s.AddSingleton<TimeProvider>(clock);
            s.AddSingleton<IDataSyncDeviceIdentity>(identityOverride ?? identity);
            s.AddScoped<IDataSyncKind>(_ => kind);
            if (extensionGroups) s.AddExtensionGroupDataSyncKind(ExtensionGroupCodec.Instance);
            configure?.Invoke(s);
        });
        var fixture = new DataSyncApplyFixture(services, kind, clock, identity);
        fixture.Guard.MarkVerified();
        await fixture.RefreshAsync();
        return fixture;
    }

    /// <summary>A fresh context of the root scope: never the stale one of an earlier read.</summary>
    public BakabaseDbContext NewDb() => Services.CreateScope().ServiceProvider.GetRequiredService<BakabaseDbContext>();

    #region Local state

    public async Task RefreshAsync()
    {
        using var lease = await Gate.EnterAsync(null, default);
        await Guard.CheckAsync(lease, default);
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DataSyncRefresher>()
            .RefreshAsync(lease, [Item, Groups], false, default);
    }

    public async Task<DataSyncLocalStateDbModel> StateAsync() =>
        await NewDb().DataSyncLocalStates.AsNoTracking().SingleAsync();

    public Task<DataSyncEntityDbModel> RowAsync(string localKey, string kind = Item) =>
        NewDb().DataSyncEntities.AsNoTracking().SingleAsync(e => e.Kind == kind && e.LocalKey == localKey &&
                                                                   e.DeletedAtUtc == null);

    public Task<DataSyncEntityDbModel?> ByKeyAsync(string key, string kind = Item) =>
        NewDb().DataSyncEntities.AsNoTracking().SingleOrDefaultAsync(e => e.Kind == kind && e.SyncKey == key);

    public Task<List<DataSyncEntityDbModel>> RowsAsync(string kind = Item) =>
        NewDb().DataSyncEntities.AsNoTracking().Where(e => e.Kind == kind).OrderBy(e => e.Id).ToListAsync();

    public Task<List<DataSyncPeerBaseDbModel>> BasesAsync(int linkId) =>
        NewDb().DataSyncPeerBases.AsNoTracking().Where(b => b.LinkId == linkId).OrderBy(b => b.Id).ToListAsync();

    public Task<List<DataSyncInboxItemDbModel>> ItemsAsync() =>
        NewDb().DataSyncInboxItems.AsNoTracking().OrderBy(i => i.Id).ToListAsync();

    public async Task<List<DataSyncInboxItemDbModel>> OpenItemsAsync() =>
        (await ItemsAsync()).Where(i => i.ClosedAtUtc == null).ToList();

    public Task<List<DataSyncApplyLogDbModel>> HistoryAsync() =>
        NewDb().DataSyncApplyLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync();

    public Task<DataSyncLinkDbModel> LinkRowAsync(int id) =>
        NewDb().DataSyncLinks.AsNoTracking().SingleAsync(l => l.Id == id);

    public async Task<IReadOnlyList<string>> KeysOfAsync(string localKey, string kind = Item)
    {
        var row = await RowAsync(localKey, kind);
        var aliases = await NewDb().DataSyncKeyAliases.AsNoTracking().Where(a => a.Kind == kind && a.SyncKey == row.SyncKey)
            .Select(a => a.AliasKey).ToListAsync();
        return [row.SyncKey, ..aliases.OrderBy(a => a, StringComparer.Ordinal)];
    }

    public static DataSyncVersionVector Vv(string json) => DataSyncVersionVector.ParseStored(json);

    public static TestItemContent Content(string name, params (string Id, string Label)[] children) =>
        new(name, null, children.Select(c => new TestChild(c.Id, c.Label)));

    #endregion

    #region Links and pulls

    public async Task<DataSyncLinkDbModel> LinkAsync(DataSyncPeer peer, DataSyncLinkMode mode = DataSyncLinkMode.TwoWay,
        bool firstContactDone = true, params string[] kinds)
    {
        kinds = kinds.Length == 0 ? [Item, Groups] : kinds;
        await using var scope = Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<DataSyncStore>();
        return await store.AddLinkAsync(new DataSyncLinkDbModel
        {
            PeerNodeId = peer.NodeId,
            PeerName = peer.Name,
            Mode = mode,
            LastMode = mode == DataSyncLinkMode.Off ? DataSyncLinkMode.TwoWay : mode,
            State = DataSyncLinkState.Active,
            Initiator = DataSyncLinkInitiator.ThisDevice,
            KindsJson = DataSyncStoredJson.Write(kinds.ToList()),
            FirstContactKindsJson = firstContactDone ? DataSyncStoredJson.Write(kinds.ToList()) : null,
            FirstContactCompletedAtUtc = firstContactDone ? Now : null,
            PeerActorId = peer.ActorId,
        }, default);
    }

    /// <summary>The context the fetch half hands the runner (the runner refreshes the actor, flags and mode).</summary>
    public static DataSyncLinkContext Context(DataSyncLinkDbModel link, DataSyncPeer peer)
    {
        var kinds = DataSyncStoredJson.ReadStrings(link.KindsJson, "KindsJson");
        var completed = DataSyncStoredJson.ReadStrings(link.FirstContactKindsJson, "FirstContactKindsJson");
        return new DataSyncLinkContext(link.Id, link.PeerNodeId, link.PeerName, link.Mode, link.Mode, kinds,
            kinds.Except(completed).ToList(), false, new DataSyncActorId(peer.ActorId), new Dictionary<string, long>(),
            peer.ActorId, new Dictionary<string, int> { [Item] = 1, [Groups] = 1 }, DataSyncMergeFlags.None);
    }

    public DataSyncStagedPull Pull(DataSyncPeer peer, params (string Kind, DataSyncWireRecord Record)[] records) =>
        Pull(peer, full: false, records);

    public DataSyncStagedPull Pull(DataSyncPeer peer, bool full, params (string Kind, DataSyncWireRecord Record)[] records)
    {
        var kinds = new List<DataSyncStagedKind>();
        var feedKinds = new List<DataSyncFeedKind>();
        foreach (var kind in new[] { Item, Groups })
        {
            var mine = records.Where(r => r.Kind == kind).Select(r => r.Record).ToList();
            if (mine.Count == 0 && !full) continue;
            var codec = kind == Item ? (IDataSyncKindCodec) Codec : ExtensionGroupCodec.Instance;
            var staged = mine.Select((r, i) => DataSyncRecordValidation.Stage(codec, r, i, DataSyncLimits.Default)).ToList();
            var maxSeq = mine.Count == 0 ? peer.Seq : mine.Max(r => r.Seq);
            kinds.Add(new DataSyncStagedKind(kind, 1, true, null, staged, maxSeq, full));
            feedKinds.Add(new DataSyncFeedKind(kind, 1, maxSeq, 0, mine.Count(r => !r.Deleted),
                mine.Count(r => r.Deleted), "", 0, mine.Count, false));
        }

        var manifest = new DataSyncFeedManifest(Guid.NewGuid().ToString("N")[..16], 60_000, peer.NodeId, "epoch-1",
            peer.ActorId, DataSyncContract.Version, DataSyncContract.MinimumPeerVersion, "2.0.0", feedKinds, null,
            new DataSyncSourceAttention(false, 0, 0, false, 0));
        return new DataSyncStagedPull(peer.NodeId, peer.Name, manifest, kinds, Now);
    }

    #endregion

    #region Running the runner

    public BTaskArgs Args(string? taskId = null, CancellationToken ct = default, PauseToken pause = default) =>
        new(pause, ct, new BTask(taskId ?? "DataSyncApply" + ++_task, () => "t"), _ => Task.CompletedTask, Services);

    /// <summary>Sets the link's state as the link actions would (a person pausing or resuming it).</summary>
    public async Task SetLinkStateAsync(int linkId, DataSyncLinkState state, DataSyncPauseReason? reason = null)
    {
        var db = NewDb();
        var link = await db.DataSyncLinks.SingleAsync(l => l.Id == linkId);
        link.State = state;
        link.PausedReason = reason;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Whether another connection takes SQLite's write lock (<c>BEGIN IMMEDIATE</c>) within <paramref name="wait"/>, as
    /// any other writer of the app would; it lets go at once.
    /// </summary>
    public async Task<bool> TryTakeWriteLockAsync(TimeSpan wait)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(NewDb().Database.GetConnectionString())
        {
            DefaultTimeout = Math.Max(1, (int) Math.Ceiling(wait.TotalSeconds)),
            Pooling = false,
        };
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(builder.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        try
        {
            command.CommandText = "BEGIN IMMEDIATE";
            await command.ExecuteNonQueryAsync();
        }
        catch (Microsoft.Data.Sqlite.SqliteException e) when (e.SqliteErrorCode == 5)
        {
            return false;
        }

        command.CommandText = "ROLLBACK";
        await command.ExecuteNonQueryAsync();
        return true;
    }

    public async Task<DataSyncAutoSyncOutcome> ApplyAsync(DataSyncLinkDbModel link, DataSyncPeer peer,
        DataSyncStagedPull? pull) =>
        await Runner.RunAutoSyncAsync(Context(await LinkRowAsync(link.Id), peer), pull, Args());

    public Task<DataSyncAutoSyncOutcome> ApplyAsync(DataSyncLinkDbModel link, DataSyncPeer peer,
        params (string Kind, DataSyncWireRecord Record)[] records) => ApplyAsync(link, peer, Pull(peer, records));

    public Task<int?> ResolveAsync(DataSyncInboxItemDbModel item, DataSyncInboxAction action, string? custom = null,
        string? target = null, string? targetRecord = null, string? newName = null, bool backup = false) =>
        ResolveAsync([new DataSyncResolveInput(item.Id, action, item.Token, custom, target, targetRecord, newName)], backup);

    public Task<int?> ResolveAsync(IReadOnlyList<DataSyncResolveInput> inputs, bool backup = false) =>
        Runner.RunResolutionsAsync(inputs, new DataSyncApplyOptions(backup), Args("DataSyncResolve:" + ++_task));

    public Task<int?> UndoAsync(int logId) => Runner.RunUndoAsync(logId, Args("DataSyncUndo:" + logId));

    /// <summary>The review's plan against the committed local state, as <c>GET /data-sync/reviews/{id}</c> re-plans it.</summary>
    public async Task<Bakabase.Modules.DataSync.Planning.DataSyncPlan> PlanAsync(DataSyncReviewEntry review,
        DataSyncLinkMode mode = DataSyncLinkMode.TwoWay)
    {
        await using var s = await DataSyncApplySession.OpenAsync(Services.GetRequiredService<IServiceScopeFactory>(), default);
        await s.LoadStateAsync(default);
        var kinds = review.Pull.Kinds.Select(k => k.Kind).Where(s.Kinds.ContainsKey).ToList();
        var local = await DataSyncMergeInputs.ReadLocalAsync(s, kinds, default);
        return Bakabase.Modules.DataSync.Planning.DataSyncPlanner.Plan(
            Bakabase.Modules.DataSync.Planning.DataSyncPlanInput.FromLocalState(review.Pull, local, s.Codecs,
                s.State.NodeId, review.CopyOnce ? DataSyncLinkMode.Off : mode));
    }

    #endregion

    #region Extension groups

    public async Task<string> AddGroupAsync(string name, params string[] extensions) =>
        (await ExtensionGroups.Add(new ExtensionGroupAddInputModel(name, extensions.ToHashSet()))).Id.ToString();

    public async Task<ExtensionGroup?> GroupAsync(string localKey)
    {
        var all = await ExtensionGroups.GetAll();
        return all.SingleOrDefault(g => g.Id.ToString() == localKey);
    }

    public static JsonObject GroupContent(string name, params string[] extensions) =>
        ExtensionGroupCodec.Instance.Write(new ExtensionGroupContentV1(name, extensions));

    #endregion
}

/// <summary>
/// A simulated peer: its actor issues counters and its feed Seq numbers, and it publishes records. With a node id
/// and an actor it stands for another provider of the test, whose records arrive through its real feed.
/// </summary>
internal sealed class DataSyncPeer(string name, string? nodeId = null, string? actorId = null)
{
    public string NodeId { get; } = nodeId ?? "node-" + name.ToLowerInvariant();
    public string Name { get; } = name;
    public string ActorId { get; } = actorId ?? Guid.NewGuid().ToString("N")[..16];
    public long Counter { get; private set; }
    public long Seq { get; private set; }
    public DataSyncEditorRef Editor => new(NodeId, Name, ActorId);

    /// <summary>The peer's next revision of a vector: its own counter added.</summary>
    public DataSyncVersionVector Next(DataSyncVersionVector? from = null) =>
        (from ?? DataSyncVersionVector.Empty).With(new DataSyncActorId(ActorId), ++Counter);

    public DataSyncWireRecord Record(IEnumerable<string> keys, DataSyncVersionVector vv, TestItemContent content,
        string? orderKey = null) =>
        Record(keys, vv, TestItemCodec.Instance.Write(content), orderKey);

    public DataSyncWireRecord Record(IEnumerable<string> keys, DataSyncVersionVector vv, JsonObject content,
        string? orderKey = null) =>
        new(keys.ToList(), NodeId, ++Seq, vv, Editor, false, 1, orderKey, content, ContentHash.Of(content), null, 0);

    public DataSyncWireRecord Tombstone(IEnumerable<string> keys, DataSyncVersionVector vv) =>
        new(keys.ToList(), NodeId, ++Seq, vv, Editor, true, 1, null, null, null, null, 0);
}
