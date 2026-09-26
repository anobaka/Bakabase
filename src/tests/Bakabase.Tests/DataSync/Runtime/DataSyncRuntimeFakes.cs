using System.Collections.Concurrent;
using System.Text;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.DataSync.Apply;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Tests.DataSync.Runtime;

// Fakes of what the runtime takes from packages C (store, runner, actor guard, review store) and D (peer client,
// grant service), so the runtime is tested on its own. They keep only what the runtime reads and writes.

internal sealed class ManualDataSyncClock : IDataSyncClock
{
    public DateTime UtcNow { get; set; } = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>
/// Link rows, the local state row, open-item counts, and — for the facade — entities, inbox items, bases, readers and
/// history, stored as copies like a database would. Every write counts in <see cref="Writes"/>.
/// </summary>
internal sealed class FakeDataSyncStore : IDataSyncStore
{
    private readonly object _lock = new();
    private readonly Dictionary<int, DataSyncLinkDbModel> _links = new();
    private int _nextId = 1;
    private int _writes;

    /// <summary>Every call that would write the database.</summary>
    public int Writes => Volatile.Read(ref _writes);

    /// <summary>What the store stamps closures with.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public List<DataSyncEntityDbModel> Entities { get; } = [];
    public List<DataSyncInboxItemDbModel> Items { get; } = [];
    public Dictionary<(int LinkId, string Kind), List<DataSyncPeerBase>> Bases { get; } = new();
    public List<DataSyncReaderDbModel> Readers { get; } = [];
    public List<DataSyncApplyLogDbModel> History { get; } = [];
    public List<(IReadOnlyCollection<(string Kind, SyncKey Key)> Touched, int? LinkId)> StaleChecks { get; } = [];

    private void Wrote() => Interlocked.Increment(ref _writes);

    public DataSyncInboxItemDbModel AddItem(DataSyncInboxItemDbModel item)
    {
        lock (_lock)
        {
            var copy = item with { Id = Items.Count == 0 ? 1 : Items.Max(i => i.Id) + 1 };
            Items.Add(copy);
            return copy with { };
        }
    }

    public DataSyncInboxItemDbModel Item(long id)
    {
        lock (_lock) return Items.Single(i => i.Id == id) with { };
    }

    public DataSyncLocalStateDbModel? LocalState { get; set; } = new()
    {
        Id = 1, NodeId = "node-self", LibraryEpoch = "epoch-self", ActorGeneration = 1,
        ActorSalt = "0011223344556677", ActorId = "a1a1a1a1a1a1a1a1", ActorCounter = 7,
        RetiredActorsJson = "{\"b2b2b2b2b2b2b2b2\":3}", DbInstanceId = new string('d', 32),
    };

    public ConcurrentDictionary<int, int> OpenItems { get; } = new();
    public List<int> Deleted { get; } = [];
    public List<int> Stopped { get; } = [];

    public DataSyncLinkDbModel Add(DataSyncLinkDbModel link)
    {
        lock (_lock)
        {
            var copy = link with { Id = _nextId++ };
            _links[copy.Id] = copy;
            return copy with { };
        }
    }

    public DataSyncLinkDbModel? Get(int id)
    {
        lock (_lock) return _links.TryGetValue(id, out var link) ? link with { } : null;
    }

    public IReadOnlyList<DataSyncLinkDbModel> All()
    {
        lock (_lock) return _links.Values.Select(l => l with { }).OrderBy(l => l.Id).ToList();
    }

    public void Edit(int id, Action<DataSyncLinkDbModel> edit)
    {
        lock (_lock) edit(_links[id]);
    }

    public Task<DataSyncLocalStateDbModel?> GetLocalStateAsync(CancellationToken ct) =>
        Task.FromResult(LocalState is null ? null : LocalState with { });

    public Task SaveLocalStateAsync(DataSyncLocalStateDbModel state, CancellationToken ct)
    {
        Wrote();
        LocalState = state with { };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataSyncLinkDbModel>> GetLinksAsync(CancellationToken ct) => Task.FromResult(All());
    public Task<DataSyncLinkDbModel?> GetLinkAsync(int id, CancellationToken ct) => Task.FromResult(Get(id));

    public Task<DataSyncLinkDbModel?> GetLinkByPeerAsync(string peerNodeId, CancellationToken ct) =>
        Task.FromResult(All().FirstOrDefault(l => l.PeerNodeId == peerNodeId));

    public Task<DataSyncLinkDbModel> AddLinkAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        if (All().Any(l => l.PeerNodeId == link.PeerNodeId))
            throw new InvalidOperationException("PeerNodeId is unique (§4.2).");
        Wrote();
        return Task.FromResult(Add(link));
    }

    public Task UpdateLinkAsync(DataSyncLinkDbModel link, CancellationToken ct)
    {
        Wrote();
        lock (_lock)
        {
            if (!_links.ContainsKey(link.Id)) throw new InvalidOperationException($"No link {link.Id}.");
            _links[link.Id] = link with { };
        }

        return Task.CompletedTask;
    }

    public Task DeleteLinkAsync(int id, CancellationToken ct)
    {
        Wrote();
        lock (_lock)
        {
            _links.Remove(id);
            Deleted.Add(id);
            CloseWhere(i => i.LinkId == id, DataSyncInboxClosure.LinkRemoved);
        }

        return Task.CompletedTask;
    }

    public Task StopLinkAsync(int id, CancellationToken ct)
    {
        Wrote();
        lock (_lock)
        {
            Stopped.Add(id);
            CloseWhere(i => i.LinkId == id, DataSyncInboxClosure.LinkStopped);
        }

        return Task.CompletedTask;
    }

    /// <summary>The counted items of E1's tests, then the stored open items.</summary>
    public Task<IReadOnlyList<DataSyncOpenInboxItem>> GetOpenItemsAsync(int? linkId, CancellationToken ct)
    {
        var count = linkId is { } id ? OpenItems.GetValueOrDefault(id) : OpenItems.Values.Sum();
        var items = Enumerable.Range(1, count)
            .Select(i => new DataSyncOpenInboxItem(-i, linkId, "customProperty", SyncKey.New(),
                DataSyncInboxItemType.FieldConflict, DataSyncInboxItemOrigin.Merger, "name", "t", null))
            .ToList();
        lock (_lock)
        {
            items.AddRange(Items.Where(i => i.ClosedAtUtc is null && (linkId is null || i.LinkId == linkId))
                .Select(i => new DataSyncOpenInboxItem(i.Id, i.LinkId, i.Kind, new SyncKey(i.SyncKey), i.Type,
                    i.Origin, i.SubjectPath, i.Token, null)));
        }

        return Task.FromResult<IReadOnlyList<DataSyncOpenInboxItem>>(items);
    }

    public Task<IReadOnlyList<DataSyncEntityDbModel>> GetEntitiesAsync(string kind, bool includeTombstones,
        CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DataSyncEntityDbModel>>(Entities
                .Where(e => e.Kind == kind && (includeTombstones || e.DeletedAtUtc is null))
                .Select(e => e with { }).ToList());
        }
    }

    public Task<(int Live, int Tombstones)> CountPublishedAsync(string kind, CancellationToken ct)
    {
        lock (_lock)
        {
            var live = Entities.Count(e => e.Kind == kind && e.DeletedAtUtc is null &&
                                           e.State == DataSyncEntitySyncState.Synced);
            var tombstones = Entities.Count(e => e.Kind == kind && e.DeletedAtUtc is not null && e.TombstoneServed);
            return Task.FromResult((live, tombstones));
        }
    }

    public Task SetEntityStateAsync(string kind, string localKey, DataSyncEntitySyncState state, CancellationToken ct)
    {
        EditEntity(kind, localKey, e => e.State = state);
        return Task.CompletedTask;
    }

    public Task SetOverlayAsync(string kind, string localKey, Bakabase.Modules.DataSync.Abstractions.DataSyncOverlay overlay,
        CancellationToken ct)
    {
        EditEntity(kind, localKey, e => e.OverlayJson = overlay.LocalOnlyChildren.Count == 0 && overlay.HeldChildren.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(overlay, Bakabase.Modules.DataSync.Canonical.DataSyncJson.Options));
        return Task.CompletedTask;
    }

    public Task SetChildrenLocalAsync(string kind, string localKey, bool childrenLocal, CancellationToken ct)
    {
        EditEntity(kind, localKey, e => e.ChildrenLocal = childrenLocal);
        return Task.CompletedTask;
    }

    private void EditEntity(string kind, string localKey, Action<DataSyncEntityDbModel> edit)
    {
        Wrote();
        lock (_lock) edit(Entities.Single(e => e.Kind == kind && e.LocalKey == localKey && e.DeletedAtUtc is null));
    }

    public Task<IReadOnlyList<DataSyncPeerBase>> GetBasesAsync(int linkId, string kind, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<DataSyncPeerBase>>(
                Bases.TryGetValue((linkId, kind), out var bases) ? bases.ToList() : []);
        }
    }

    public Task CloseItemsAsync(IReadOnlyCollection<long> ids, DataSyncInboxClosure closure, DataSyncInboxAction? action,
        DataSyncEditorRef? by, int? applyLogId, CancellationToken ct)
    {
        Wrote();
        lock (_lock) CloseWhere(i => ids.Contains(i.Id), closure);
        return Task.CompletedTask;
    }

    public Task<int> CloseStaleStateItemsAsync(IReadOnlyCollection<(string Kind, SyncKey Key)> touched, int? linkId,
        DateTime nowUtc, CancellationToken ct)
    {
        Wrote();
        lock (_lock) StaleChecks.Add((touched, linkId));
        return Task.FromResult(0);
    }

    /// <summary>Closes the open items that match, as a store closure would.</summary>
    public void CloseWhere(Func<DataSyncInboxItemDbModel, bool> match, DataSyncInboxClosure closure)
    {
        lock (_lock)
        {
            foreach (var item in Items.Where(i => i.ClosedAtUtc is null && match(i)))
            {
                item.ClosedAtUtc = Now();
                item.Closure = closure;
            }
        }
    }

    public Task<DataSyncInboxItemDbModel?> GetItemAsync(long id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(Items.FirstOrDefault(i => i.Id == id) is { } item ? item with { } : null);
    }

    public Task<DataSyncInboxPage> QueryInboxAsync(DataSyncInboxQuery query, CancellationToken ct)
    {
        lock (_lock)
        {
            var filtered = Items.Where(i => (query.PeerNodeId is null || i.PeerNodeId == query.PeerNodeId) &&
                                            (query.Kind is null || i.Kind == query.Kind) &&
                                            (query.LocalKey is null || i.LocalKey == query.LocalKey)).ToList();
            var open = filtered.Count(i => i.ClosedAtUtc is null);
            if (query.OpenOnly) filtered = filtered.Where(i => i.ClosedAtUtc is null).ToList();
            // A store's view: what it knows of the rules is not the facade's to trust, so none are given here, and a
            // default is pre-chosen that the facade must drop (§9.1). The order is the contract's: open items first,
            // newest first within each group.
            var page = filtered.OrderBy(i => i.ClosedAtUtc is null ? 0 : 1).ThenByDescending(i => i.Id)
                .Skip(query.Skip).Take(query.Take)
                .Select(i => DataSyncInboxService.ToView(i, i.LinkId is { } l ? Get(l) : null) with
                {
                    AllowedActions = [],
                    DefaultAction = DataSyncInboxAction.Skip,
                })
                .ToList();
            return Task.FromResult(new DataSyncInboxPage(page, filtered.Count, open));
        }
    }

    public Task<IReadOnlyList<long>> GetUnannouncedItemIdsAsync(int linkId, CancellationToken ct)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<long>>(Items
                .Where(i => i.LinkId == linkId && i.ClosedAtUtc is null && i.NotifiedAtUtc is null)
                .Select(i => i.Id).ToList());
        }
    }

    public Task SetItemsNotifiedAsync(IReadOnlyCollection<long> ids, int notificationId, DateTime nowUtc,
        CancellationToken ct)
    {
        Wrote();
        lock (_lock)
        {
            foreach (var item in Items.Where(i => ids.Contains(i.Id)))
            {
                item.NotificationId = notificationId;
                item.NotifiedAtUtc = nowUtc;
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<int>> GetSettledNotificationsAsync(DateTime closedSinceUtc, CancellationToken ct)
    {
        lock (_lock)
        {
            var openIds = Items.Where(i => i.ClosedAtUtc is null && i.NotificationId is not null)
                .Select(i => i.NotificationId!.Value).ToHashSet();
            return Task.FromResult<IReadOnlyList<int>>(Items
                .Where(i => i.ClosedAtUtc >= closedSinceUtc && i.NotificationId is { } n && !openIds.Contains(n))
                .Select(i => i.NotificationId!.Value).Distinct().ToList());
        }
    }

    public Task<IReadOnlyList<DataSyncReaderDbModel>> GetReadersAsync(CancellationToken ct)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<DataSyncReaderDbModel>>(Readers.Select(r => r with { }).ToList());
    }

    public Task SetReaderNotifiedAsync(string nodeId, DateTime nowUtc, CancellationToken ct)
    {
        Wrote();
        lock (_lock)
        {
            foreach (var reader in Readers.Where(r => r.NodeId == nodeId)) reader.NotifiedAtUtc = nowUtc;
        }

        return Task.CompletedTask;
    }

    public Task<int> AddHistoryAsync(DataSyncApplyLogDbModel log, CancellationToken ct)
    {
        Wrote();
        lock (_lock)
        {
            var copy = log with { Id = History.Count == 0 ? 1 : History.Max(h => h.Id) + 1 };
            History.Add(copy);
            return Task.FromResult(copy.Id);
        }
    }

    public Task<IReadOnlyList<DataSyncApplyLogDbModel>> GetHistoryAsync(CancellationToken ct)
    {
        lock (_lock) return Task.FromResult<IReadOnlyList<DataSyncApplyLogDbModel>>(History.Select(h => h with { }).ToList());
    }

    public Task<DataSyncApplyLogDbModel?> GetHistoryEntryAsync(int id, CancellationToken ct)
    {
        lock (_lock) return Task.FromResult(History.FirstOrDefault(h => h.Id == id) is { } log ? log with { } : null);
    }

    // Not used by the runtime or the facade.
    public Task<long> NextSeqAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<DataSyncEntityDbModel>> GetPublishedChangedSinceAsync(string kind, long sinceSeq,
        CancellationToken ct) => throw new NotSupportedException();

    /// <summary>Pending records to re-merge per link, as <see cref="GetPendingToMergeAsync"/> answers without a pull.</summary>
    public ConcurrentDictionary<int, List<(string Kind, SyncKey Key)>> PendingToMerge { get; } = new();

    public Task<IReadOnlyList<(string Kind, SyncKey Key)>> GetPendingToMergeAsync(int linkId, bool fullReconciliation,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<(string Kind, SyncKey Key)>>(
            PendingToMerge.TryGetValue(linkId, out var pending) ? pending.ToList() : []);

    public Task UpsertBasesAsync(int linkId, IEnumerable<DataSyncBaseUpdate> updates, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task RepointBasesAsync(string kind, string fromKey, string toKey, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<DataSyncInboxReconcileResult> ReconcileInboxAsync(int linkId, string peerNodeId,
        IReadOnlyList<DataSyncInboxDraft> drafts, IReadOnlyCollection<(string Kind, SyncKey Key)> evaluated,
        IReadOnlyList<DataSyncClosureHint> hints, DateTime nowUtc, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<int> CloseDominatedItemsAsync(IReadOnlyCollection<(string Kind, SyncKey Key)> touched, DateTime nowUtc,
        CancellationToken ct) => throw new NotSupportedException();

    public Task<DataSyncSourceAttention> GetAttentionAsync(CancellationToken ct) => throw new NotSupportedException();

    public Task TouchReaderAsync(DataSyncReader reader, DataSyncFeedQuery query, long seqServed, DateTime nowUtc,
        CancellationToken ct) => throw new NotSupportedException();

    public Task PruneAsync(DateTime nowUtc, CancellationToken ct) => throw new NotSupportedException();
}

/// <summary>One peer as the feed serves it: its head, a snapshot per manifest, and pages the fake reader decodes.</summary>
internal sealed class FakePeer(string nodeId)
{
    public string NodeId { get; } = nodeId;
    public string ServedNodeId { get; set; } = nodeId;
    public string Epoch { get; set; } = "epoch-1";
    public string ActorId { get; set; } = "0123456789abcdef";
    public int Contract { get; set; } = DataSyncContract.Version;
    public int MinimumPeerContract { get; set; } = DataSyncContract.MinimumPeerVersion;
    public Dictionary<string, long> MaxSeq { get; } = new() { ["extensionGroup"] = 3, ["customProperty"] = 5 };
    public HashSet<string> Superseded { get; } = [];
    public DataSyncFeedCounterpart? Counterpart { get; set; }
    public DataSyncSourceAttention Attention { get; set; } = new(false, 0, 0, false, 0);
    public long? SeenCounter { get; set; }
    public int PagesPerKind { get; set; } = 1;
    public string? BadPageOf { get; set; }

    public Queue<DataSyncPeerException> HeadErrors { get; } = new();
    public Queue<DataSyncPeerException> ManifestErrors { get; } = new();
    public Queue<DataSyncPeerException> PageErrors { get; } = new();

    /// <summary>Awaited at the start of every head: lets a test hold a fetch cycle.</summary>
    public Func<CancellationToken, Task>? BeforeHead { get; set; }

    public ConcurrentQueue<DataSyncFeedQuery> HeadQueries { get; } = new();
    public ConcurrentQueue<DataSyncFeedQuery> ManifestQueries { get; } = new();
    public int Pages;
    public int Manifests;

    public DataSyncFeedHead Head() => new(ServedNodeId, Epoch, ActorId, Contract, MinimumPeerContract, "2.4.0-beta.1",
        MaxSeq.Values.DefaultIfEmpty().Max(),
        MaxSeq.Select(k => new DataSyncFeedKindHead(k.Key, 1, k.Value, Superseded.Contains(k.Key), 1)).ToList(),
        Attention, SeenCounter, Counterpart);

    public DataSyncFeedManifest Manifest(DataSyncFeedQuery query)
    {
        var id = Interlocked.Increment(ref Manifests);
        var kinds = query.Since.Where(s => MaxSeq.ContainsKey(s.Key)).Select(s =>
        {
            var superseded = Superseded.Contains(s.Key);
            return new DataSyncFeedKind(s.Key, 1, MaxSeq[s.Key], 0, 10, 0, "sha256:x", superseded ? 0 : s.Value, 1,
                superseded);
        }).ToList();
        return new DataSyncFeedManifest($"snap-{id}", 120_000, ServedNodeId, Epoch, ActorId, Contract,
            MinimumPeerContract, "2.4.0-beta.1", kinds, Counterpart, Attention);
    }

    /// <summary>Called after each page is counted, before it is served.</summary>
    public Action? OnPage { get; set; }

    /// <summary>
    /// Serves the page bytes instead of the fake reader's (snapshot id, kind, since, cursor): a scripted source for
    /// the real page reader.
    /// </summary>
    public Func<string, string, long, string?, byte[]>? Serve { get; set; }

    public byte[] Page(string snapshotId, string kind, long sinceSeq, string? cursor)
    {
        Interlocked.Increment(ref Pages);
        OnPage?.Invoke();
        if (Serve is { } serve) return serve(snapshotId, kind, sinceSeq, cursor);
        if (kind == BadPageOf) return "bad"u8.ToArray();
        var index = cursor is null ? 1 : int.Parse(cursor[1..]);
        return Encoding.UTF8.GetBytes($"page:{index}:{PagesPerKind}");
    }
}

internal sealed class FakeDataSyncPeerClient : IDataSyncPeerClient
{
    public ConcurrentDictionary<string, FakePeer> Peers { get; } = new();

    public FakePeer Add(string nodeId) => Peers[nodeId] = new FakePeer(nodeId);

    private FakePeer Peer(string nodeId) =>
        Peers.TryGetValue(nodeId, out var peer)
            ? peer
            : throw new DataSyncPeerException(DataSyncPeerErrorCode.Unreachable, "unknown peer");

    public Task<DataSyncPeerProbe> ProbeAsync(string peerNodeId, CancellationToken ct) =>
        throw new NotSupportedException();

    public async Task<DataSyncFeedHead> GetHeadAsync(string peerNodeId, DataSyncFeedQuery query, CancellationToken ct)
    {
        var peer = Peer(peerNodeId);
        if (peer.BeforeHead is { } before) await before(ct);
        peer.HeadQueries.Enqueue(query);
        if (peer.HeadErrors.TryDequeue(out var error)) throw error;
        return peer.Head();
    }

    public Task<DataSyncFeedManifest> GetManifestAsync(string peerNodeId, DataSyncFeedQuery query,
        CancellationToken ct)
    {
        var peer = Peer(peerNodeId);
        peer.ManifestQueries.Enqueue(query);
        if (peer.ManifestErrors.TryDequeue(out var error)) throw error;
        return Task.FromResult(peer.Manifest(query));
    }

    public Task<ReadOnlyMemory<byte>> GetPageAsync(string peerNodeId, string snapshotId, string kind, long sinceSeq,
        string? cursor, CancellationToken ct)
    {
        var peer = Peer(peerNodeId);
        if (peer.PageErrors.TryDequeue(out var error)) throw error;
        return Task.FromResult<ReadOnlyMemory<byte>>(peer.Page(snapshotId, kind, sinceSeq, cursor));
    }
}

/// <summary>Decodes the fake pages (<c>page:{index}:{count}</c>); a staged kind carries no entities.</summary>
internal sealed class FakeKindPageReader : IDataSyncKindPageReader
{
    public HashSet<string> Kinds { get; } = ["extensionGroup", "customProperty"];

    public bool Supports(string kind) => Kinds.Contains(kind);

    public IDataSyncKindPageAssembly Begin(string kind, string snapshotId, DataSyncFeedKind manifestKind,
        bool fullReconciliation) => new Assembly(kind, manifestKind, fullReconciliation);

    private sealed class Assembly(string kind, DataSyncFeedKind manifestKind, bool full) : IDataSyncKindPageAssembly
    {
        private bool _complete;

        public string? Problem { get; private set; }

        public DataSyncPageStep Add(ReadOnlyMemory<byte> page)
        {
            var text = Encoding.UTF8.GetString(page.Span);
            if (!text.StartsWith("page:", StringComparison.Ordinal))
            {
                Problem = "corrupted";
                return new DataSyncPageStep(false, null, false);
            }

            var parts = text.Split(':');
            var index = int.Parse(parts[1]);
            var count = int.Parse(parts[2]);
            _complete = index >= count;
            return new DataSyncPageStep(true, _complete ? null : $"p{index + 1}", _complete);
        }

        public DataSyncStagedKind? Complete()
        {
            if (!_complete)
            {
                Problem = "corrupted";
                return null;
            }

            return new DataSyncStagedKind(kind, 1, true, null, [], manifestKind.MaxSeq, full);
        }
    }
}

internal sealed class FakeDataSyncGrantService : IDataSyncGrantService
{
    public HashSet<string> Outbound { get; } = [];
    public List<DataSyncGrantView> Readers { get; } = [];
    public List<DataSyncPeerCandidate> Peers { get; } = [];
    public List<DataSyncAccessRequestView> Requests { get; } = [];
    public List<DataSyncAccessRequestInput> Sent { get; } = [];
    public bool SharingEnabled { get; set; } = true;
    public RemoteAccessMode RemoteAccessMode { get; set; } = RemoteAccessMode.Enabled;

    /// <summary>What a request answers; by default it waits for approval.</summary>
    public Func<DataSyncAccessRequestInput, DataSyncAccessRequestOutcome> Answer { get; set; } = input =>
        new DataSyncAccessRequestOutcome("awaitingApproval", "req-" + (input.PeerNodeId ?? input.Address),
            input.PeerNodeId ?? "node-by-address", "Peer by address", null);

    /// <summary>Every call that changed access, in order (<c>approve:{id}:{readBack}</c>, <c>revoke:{node}</c>, …).</summary>
    public ConcurrentQueue<string> Changes { get; } = new();

    /// <summary>What an approval answers; by default the request's peer, read back when asked for two-way.</summary>
    public Func<DataSyncAccessRequestView, bool, DataSyncApprovalOutcome>? Approval { get; set; }

    /// <summary>Thrown by the next access-changing call, as the federation side refuses on this device.</summary>
    public DataSyncProblem? Refuse { get; set; }

    public Task<bool> IsSharingEnabledAsync(CancellationToken ct) => Task.FromResult(SharingEnabled);

    public Task SetSharingEnabledAsync(bool enabled, bool enablePairedRemoteAccess, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"sharing:{enabled}:{enablePairedRemoteAccess}");
        SharingEnabled = enabled;
        // Only from Disabled, never touching Enabled or Unrestricted (§7.1.3).
        if (enabled && enablePairedRemoteAccess && RemoteAccessMode == RemoteAccessMode.Disabled)
            RemoteAccessMode = RemoteAccessMode.Enabled;
        return Task.CompletedTask;
    }

    private void ThrowIfRefused()
    {
        if (Refuse is { } problem)
        {
            Refuse = null;
            throw new DataSyncProblemException(problem);
        }
    }

    public Task<RemoteAccessMode> GetRemoteAccessModeAsync(CancellationToken ct) => Task.FromResult(RemoteAccessMode);

    public Task<IReadOnlyList<DataSyncPeerCandidate>> GetPeersAsync(bool discover, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DataSyncPeerCandidate>>(Peers.ToList());

    public Task<DataSyncAccessRequestOutcome> RequestAccessAsync(DataSyncAccessRequestInput input, CancellationToken ct)
    {
        lock (Sent) Sent.Add(input);
        return Task.FromResult(Answer(input));
    }

    /// <summary>
    /// The clock the listing is read at. Set, the listing leaves out every request whose time ran out, as
    /// <c>FederationPeerService.GetDataSyncStatusAsync</c> does: an expired request is never listed as expired, it is
    /// simply gone.
    /// </summary>
    public Func<DateTime>? Now { get; set; }

    public Task<IReadOnlyList<DataSyncAccessRequestView>> GetRequestsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DataSyncAccessRequestView>>(Requests
            .Where(r => Now is not { } now || r.ExpiresAt > now()).ToList());

    public Task<DataSyncApprovalOutcome> ApproveAsync(string requestId, bool readBack, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"approve:{requestId}:{readBack}");
        var request = Requests.Single(r => r.RequestId == requestId);
        var outcome = Approval?.Invoke(request, readBack) ?? new DataSyncApprovalOutcome(request.NodeId,
            request.NodeName, request.Intent, readBack && request.Intent == DataSyncRequestIntent.TwoWay, null);
        lock (Readers) Readers.Add(new DataSyncGrantView(request.NodeId, request.NodeName, DateTime.UtcNow));
        if (outcome.ReadBackGranted) Outbound.Add(request.NodeId);
        return Task.FromResult(outcome);
    }

    public Task RejectAsync(string requestId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"reject:{requestId}");
        return Task.CompletedTask;
    }

    public Task CancelOutgoingAsync(string requestId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"cancel:{requestId}");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<DataSyncGrantView>> GetGrantsAsync(CancellationToken ct)
    {
        lock (Readers) return Task.FromResult<IReadOnlyList<DataSyncGrantView>>(Readers.ToList());
    }

    public Task RevokeAsync(string peerNodeId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"revoke:{peerNodeId}");
        lock (Readers) Readers.RemoveAll(r => r.NodeId == peerNodeId);
        return Task.CompletedTask;
    }

    /// <summary>A code whose expiry comes back without a kind, as a stored time would.</summary>
    public Task<DataSyncInvitationView> CreateInvitationAsync(DataSyncInvitationInput input, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"invite:{input.AllowTwoWay}");
        return Task.FromResult(new DataSyncInvitationView("48213705",
            DateTime.SpecifyKind(new DateTime(2026, 9, 1, 8, 10, 0), DateTimeKind.Unspecified),
            ["http://192.168.1.20:5000"], input.AllowTwoWay));
    }

    public Task<bool> HasOutboundGrantAsync(string peerNodeId, CancellationToken ct) =>
        Task.FromResult(Outbound.Contains(peerNodeId));

    public Task ForgetOutboundAsync(string peerNodeId, CancellationToken ct)
    {
        ThrowIfRefused();
        Changes.Enqueue($"forget:{peerNodeId}");
        Outbound.Remove(peerNodeId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeReviewStore : IDataSyncReviewStore
{
    private readonly ConcurrentDictionary<string, DataSyncReviewEntry> _entries = new();
    private int _next;

    public List<DataSyncReviewEntry> Staged { get; } = [];
    public List<string> Discarded { get; } = [];

    public DataSyncReviewEntry? GetForLink(int linkId) => _entries.Values.FirstOrDefault(e => e.LinkId == linkId);

    public DataSyncReviewEntry? PeekForLink(int linkId) => GetForLink(linkId);

    public DataSyncReviewEntry Stage(int? linkId, bool copyOnce, DataSyncStagedPull pull)
    {
        var entry = new DataSyncReviewEntry($"review-{Interlocked.Increment(ref _next)}", linkId, copyOnce, pull, null,
            null, null, DateTime.UtcNow, false);
        _entries[entry.ReviewId] = entry;
        lock (Staged) Staged.Add(entry);
        return entry;
    }

    /// <summary>A review expires after 60 min idle, or is lost on restart.</summary>
    public void Expire(int linkId)
    {
        foreach (var entry in _entries.Values.Where(e => e.LinkId == linkId).ToList()) _entries.TryRemove(entry.ReviewId, out _);
    }

    public DataSyncReviewEntry? Get(string reviewId) => _entries.GetValueOrDefault(reviewId);

    public void SetLastPlan(string reviewId, DataSyncPlan plan) => Edit(reviewId, e => e with { LastPlan = plan });

    public void MarkApplying(string reviewId, string taskId) => Edit(reviewId, e => e with { TaskId = taskId });

    public void MarkApplied(string reviewId, int applyLogId) => Edit(reviewId, e => e with { ApplyLogId = applyLogId });

    public void MarkApplyEnded(string reviewId) =>
        Edit(reviewId, e => e.ApplyLogId is null ? e with { TaskId = null } : e);

    private void Edit(string reviewId, Func<DataSyncReviewEntry, DataSyncReviewEntry> edit)
    {
        if (_entries.TryGetValue(reviewId, out var entry)) _entries[reviewId] = edit(entry);
    }

    public void Discard(string reviewId)
    {
        _entries.TryRemove(reviewId, out _);
        Discarded.Add(reviewId);
    }
}

internal sealed record AutoSyncCall(DataSyncLinkContext Context, DataSyncStagedPull? Pull, string TaskId,
    DataSyncTaskAttempt? Attempt);

internal sealed class FakeApplyRunner(IDataSyncTaskRegistry registry) : IDataSyncApplyRunner
{
    public ConcurrentQueue<AutoSyncCall> AutoSyncs { get; } = new();
    public ConcurrentQueue<(DataSyncRestoreChoice Choice, int? LinkId)> Restores { get; } = new();
    public ConcurrentQueue<int> Undos { get; } = new();

    /// <summary>Awaited by every call: lets a test hold an apply inside its body.</summary>
    public Func<CancellationToken, Task>? Hold { get; set; }

    public Func<AutoSyncCall, DataSyncAutoSyncOutcome> AutoSyncOutcome { get; set; } =
        _ => new DataSyncAutoSyncOutcome(1, null, 0, 0, 1, [], [], DataSyncAutoSyncEnd.Committed);

    public Queue<Exception> UndoErrors { get; } = new();
    public Queue<Exception> AutoSyncErrors { get; } = new();

    /// <summary>Whether the attempt was still current when the runner would have entered the gate.</summary>
    public ConcurrentQueue<bool> AttemptCurrentAtGate { get; } = new();

    public async Task<DataSyncAutoSyncOutcome> RunAutoSyncAsync(DataSyncLinkContext link, DataSyncStagedPull? pull,
        BTaskArgs args)
    {
        AttemptCurrentAtGate.Enqueue(registry.ShouldRunCurrent());
        if (Hold is { } hold) await hold(args.CancellationToken);
        args.CancellationToken.ThrowIfCancellationRequested();
        var call = new AutoSyncCall(link, pull, args.Task.Id, DataSyncTaskAttempts.Current);
        AutoSyncs.Enqueue(call);
        lock (AutoSyncErrors)
        {
            if (AutoSyncErrors.TryDequeue(out var error)) throw error;
        }

        return AutoSyncOutcome(call);
    }

    public Task<int?> RunReviewAsync(string reviewId, IReadOnlyList<DataSyncPlanDecision> decisions,
        DataSyncApplyOptions options, BTaskArgs args) => Task.FromResult<int?>(1);

    public Task<int?> RunResolutionsAsync(IReadOnlyList<DataSyncResolveInput> resolutions, DataSyncApplyOptions options,
        BTaskArgs args) => Task.FromResult<int?>(1);

    public async Task<int?> RunUndoAsync(int applyLogId, BTaskArgs args)
    {
        if (Hold is { } hold) await hold(args.CancellationToken);
        Undos.Enqueue(applyLogId);
        lock (UndoErrors)
        {
            if (UndoErrors.TryDequeue(out var error)) throw error;
        }

        return applyLogId + 100;
    }

    public async Task<int?> RunRestoreAsync(DataSyncRestoreChoice choice, int? linkId, BTaskArgs args)
    {
        if (Hold is { } hold) await hold(args.CancellationToken);
        Restores.Enqueue((choice, linkId));
        return 1;
    }
}

internal sealed class FakeActorGuard : IDataSyncActorGuard
{
    public bool IsVerified { get; set; } = true;
    public ConcurrentQueue<(string Peer, string Actor, long Counter)> Evidence { get; } = new();
    public int Checks;

    /// <summary>Awaited by every evidence report, before it is recorded: lets a test hold one as a rotation would.</summary>
    public Func<string, CancellationToken, Task>? BeforeEvidence { get; set; }

    public Task<DataSyncPauseReason?> CheckAsync(DataSyncGateLease lease, CancellationToken ct)
    {
        Interlocked.Increment(ref Checks);
        return Task.FromResult<DataSyncPauseReason?>(null);
    }

    public async Task ReportPeerEvidenceAsync(string peerNodeId, string actorId, long seenCounter, CancellationToken ct)
    {
        if (BeforeEvidence is { } before) await before(peerNodeId, ct);
        Evidence.Enqueue((peerNodeId, actorId, seenCounter));
    }

    public Task ReportReaderAheadAsync(string readerNodeId, CancellationToken ct) => Task.CompletedTask;
    public void MarkVerified() => IsVerified = true;
}

internal sealed class RecordingObserver : IDataSyncRuntimeObserver
{
    public ConcurrentQueue<string> Events { get; } = new();

    public Task LinkChangedAsync(DataSyncLinkDbModel link, CancellationToken ct) => Record($"changed:{link.Id}:{link.State}");
    public Task LinkRemovedAsync(DataSyncLinkDbModel removed, CancellationToken ct) => Record($"removed:{removed.Id}");
    public Task LinkPausedAsync(DataSyncLinkDbModel link, CancellationToken ct) => Record($"paused:{link.Id}:{link.PausedReason}");

    public Task ReviewReadyAsync(DataSyncLinkDbModel link, DataSyncReviewEntry review, CancellationToken ct) =>
        Record($"review:{link.Id}:{review.ReviewId}");

    public Task AutoSyncAppliedAsync(DataSyncLinkDbModel link, DataSyncAutoSyncOutcome outcome, bool firstSync,
        CancellationToken ct) => Record($"applied:{link.Id}:{(firstSync ? "first" : "next")}");

    public int Count(string prefix) => Events.Count(e => e.StartsWith(prefix, StringComparison.Ordinal));

    private Task Record(string e)
    {
        Events.Enqueue(e);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The database's single writer as the runtime's row transactions meet it: <c>BEGIN IMMEDIATE</c> waits while another
/// transaction holds the write lock. A test holds it as the apply runner's open transaction would
/// (<see cref="Hold"/>) and sees who waits for it.
/// </summary>
internal sealed class FakeRowTransactions : IDataSyncRowTransactions
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private int _waiting;

    /// <summary>Transactions waiting for the write lock now.</summary>
    public int Waiting => Volatile.Read(ref _waiting);

    public async Task<IDataSyncRowTransaction> BeginAsync(IServiceProvider scope, CancellationToken ct)
    {
        Interlocked.Increment(ref _waiting);
        try
        {
            await _writeLock.WaitAsync(ct);
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }

        return new Transaction(_writeLock);
    }

    /// <summary>Holds the write lock, as another transaction would, until disposed.</summary>
    public IDisposable Hold()
    {
        if (!_writeLock.Wait(0)) throw new InvalidOperationException("The write lock is already held.");
        return new Held(_writeLock);
    }

    private sealed class Transaction(SemaphoreSlim writeLock) : IDataSyncRowTransaction
    {
        private int _released;

        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) writeLock.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Held(SemaphoreSlim writeLock) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) writeLock.Release();
        }
    }
}

/// <summary>Federation sessions as the scheduler reads them: the peers whose session is verified now.</summary>
internal sealed class FakePeerSessions : IDataSyncPeerSessions
{
    public ConcurrentDictionary<string, byte> Online { get; } = new(StringComparer.Ordinal);

    public bool IsOnline(string peerNodeId) => Online.ContainsKey(peerNodeId);
}

internal sealed class FakeHostLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;
    public void StopApplication() => _stopping.Cancel();
}
