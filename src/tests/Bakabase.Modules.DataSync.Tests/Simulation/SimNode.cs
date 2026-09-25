using System.Globalization;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>
/// One device of the simulator (§13.3): the service (definitions, values, local order), the side rows, links with
/// bases and pending records, an inbox with both origins, history, the actor with its salt, retired actors and
/// <c>actor.json</c>. It runs Refresh through <see cref="DataSyncRefreshRules"/>, serves its feed through the real
/// wire writer, reader and assembler, merges with the real <see cref="DataSyncMerger"/>, records applies through
/// <see cref="DataSyncRecordApply"/> and <see cref="DataSyncRevisionRules"/>, and places order with
/// <see cref="DataSyncOrderPlanner"/> — the helpers the Business implementation [C] calls too.
/// </summary>
/// <remarks>
/// Everything a database holds is in <see cref="Db"/>, so restoring a database is swapping it; <c>actor.json</c>
/// (<see cref="Watermark"/>), the library epoch and the verified flag live outside it.
/// </remarks>
internal sealed partial class SimNode
{
    /// <summary>The kind the scenario API defaults to.</summary>
    public const string Kind = TestItemCodec.Kind;

    public static readonly TimeSpan UnverifiedTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan TombstoneRetention = TimeSpan.FromDays(180);
    public static readonly TimeSpan FullReconciliationInterval = TimeSpan.FromHours(24);

    private static DataSyncLimits Limits => SimKinds.Limits;

    private readonly SimWorld _world;
    private readonly HashSet<int> _headsAnswered = [];
    private DateTime _startedAt;

    public SimNode(SimWorld world, string name, bool headless = false)
    {
        _world = world;
        Name = name;
        NodeId = name.ToLowerInvariant().Replace(' ', '-');
        Headless = headless;
        Db.Local.Salt = world.NewSalt();
        Db.Local.ActorId = DataSyncActorId.Derive(NodeId, Epoch, Db.Local.Salt).Value;
        Db.Local.DbInstanceId = "db-" + NodeId + "-" + Db.Local.Salt;
        Watermark = new SimWatermark(1, Db.Local.ActorId, 0, Db.Local.DbInstanceId);
    }

    public string Name { get; }
    public string NodeId { get; }
    public bool Headless { get; }
    public string Epoch { get; private set; } = "epoch-1";
    public SimDb Db { get; private set; } = new();
    public SimWatermark Watermark { get; private set; }

    /// <summary>§5.6: after every start the actor is unverified until each Active link's peer answered a head.</summary>
    public bool Verified { get; private set; } = true;

    public List<SimNotification> Notifications { get; } = [];
    public List<DataSyncMergeNote> Notes { get; } = [];
    public DataSyncMergeResult? LastResult { get; private set; }

    /// <summary>Invariant I4 breaches found while applying (the runner fails on any).</summary>
    public List<string> Violations { get; } = [];

    /// <summary>A write that lands between a merge and its apply (the runner injects it; §8.10.2 ChangedDuringApply).</summary>
    public Action<SimNode, DataSyncMergeResult>? BeforeApply { get; set; }

    public List<SimRow> Rows => Db.Rows;
    public Dictionary<string, SimLink> Links => Db.Links;
    public List<SimItem> Items => Db.Items;
    public DataSyncActorId Actor => new(Db.Local.ActorId);
    public long ActorCounter => Db.Local.ActorCounter;
    public long LastSeq => Db.Local.LastSeq;
    public DataSyncEditorRef Editor => new(NodeId, Name, Db.Local.ActorId);
    public int OpenDecisions => Items.Count(i => i.Open);
    public IEnumerable<SimItem> OpenItems => Items.Where(i => i.Open);
    public bool RestorePending => Db.Local.RestoreReason is not null;
    private DateTime Now => _world.Clock.Now;

    public override string ToString() => Name;

    // ---- links -------------------------------------------------------------------------------------

    /// <summary>This node pulls <paramref name="peer"/> (a new link, its first contact still to come).</summary>
    public SimLink Follow(SimNode peer, DataSyncLinkMode mode = DataSyncLinkMode.TwoWay)
    {
        var link = new SimLink { Id = _world.NextLinkId(), Peer = peer, Mode = mode, LastMode = mode };
        Links[peer.NodeId] = link;
        return link;
    }

    public SimLink LinkTo(SimNode peer) => Links[peer.NodeId];

    public SimLink? LinkById(int? id) => id is { } value ? Links.Values.FirstOrDefault(l => l.Id == value) : null;

    /// <summary>The link's effective mode (§8.1): mutual Follow works as TwoWay.</summary>
    public DataSyncLinkMode EffectiveMode(SimLink link) =>
        link.Mode == DataSyncLinkMode.Follow &&
        link.Peer.Links.GetValueOrDefault(NodeId) is { Mode: DataSyncLinkMode.Follow }
            ? DataSyncLinkMode.TwoWay
            : link.Mode;

    // ---- local edits (through "the services") ------------------------------------------------------

    public SimRow Create(TestItemContent content) => Create(Kind, content);

    public SimRow Create(string kind, object content)
    {
        var row = new SimRow { Kind = kind, LocalKey = NewLocalKey(), Content = content };
        row.LocalHash = ContentHash.Of(SimKinds.Of(kind).Codec.Write(content));
        Rows.Add(row);
        if (SimKinds.Of(kind).HasOrder) OrderOf(kind).Add(row.LocalKey);
        return row;
    }

    public SimRow Row(string name, string kind = Kind) => Rows.Single(r => r.Kind == kind && r.IsLive && r.Name == name);

    public SimRow? Find(string name, string kind = Kind) =>
        Rows.SingleOrDefault(r => r.Kind == kind && r.IsLive && r.Name == name);

    public SimRow RowOf(string kind, SyncKey key) => Rows.First(r => r.Kind == kind && r.Keys.Contains(key));

    public SimRow RowOf(SyncKey key) => RowOf(Kind, key);

    public SimRow? LiveRow(string kind, string localKey) =>
        Rows.FirstOrDefault(r => r.Kind == kind && !r.Deleted && r.LocalKey == localKey);

    public void Edit(string name, Func<TestItemContent, TestItemContent> change)
    {
        var row = Row(name);
        row.Content = change(row.Item!);
    }

    public void EditRow(SimRow row, object content)
    {
        if (!row.IsLive) throw new InvalidOperationException("Only a live definition can be edited.");
        row.Content = content;
    }

    public void Delete(string name) => DeleteRow(Row(name));

    /// <summary>The service deletes a definition and its values; Refresh tombstones the side row.</summary>
    public void DeleteRow(SimRow row)
    {
        row.DeletedLocally = true;
        row.Content = null;
        Db.Values.Remove((row.Kind, row.LocalKey));
        Db.Usage.Remove((row.Kind, row.LocalKey));
        if (Db.Order.TryGetValue(row.Kind, out var order)) order.Remove(row.LocalKey);
    }

    /// <summary>
    /// "Sync the definition only" turned on or off here (§3.6). The flag is shared content kept on the side row, so
    /// the next Refresh issues a revision like any local edit.
    /// </summary>
    public void SetChildrenLocal(SimRow row, bool on)
    {
        if (!row.IsLive) throw new InvalidOperationException("Only a live definition can be edited.");
        if (!SimKinds.Of(row.Kind).Codec.Descriptor.SupportsChildrenLocal)
            throw new InvalidOperationException($"{row.Kind} does not offer 'sync the definition only'.");
        row.ChildrenLocal = on;
    }

    public void Use(string name, string childId, int resources) => UseChild(Row(name), childId, resources);

    public void UseChild(SimRow row, string childId, int resources)
    {
        if (!Db.Usage.TryGetValue((row.Kind, row.LocalKey), out var usage))
            Db.Usage[(row.Kind, row.LocalKey)] = usage = new Dictionary<string, int>(StringComparer.Ordinal);
        usage[childId] = resources;
    }

    public void SetValues(string name, int values) => Db.Values[(Kind, Row(name).LocalKey)] = values;

    public void SetRowValues(SimRow row, int values) => Db.Values[(row.Kind, row.LocalKey)] = values;

    /// <summary>A person moves a definition in the list (the service's integer order).</summary>
    public void Move(SimRow row, int index)
    {
        var order = OrderOf(row.Kind);
        order.Remove(row.LocalKey);
        order.Insert(Math.Clamp(index, 0, order.Count), row.LocalKey);
    }

    public List<string> OrderOf(string kind)
    {
        if (!Db.Order.TryGetValue(kind, out var order)) Db.Order[kind] = order = [];
        return order;
    }

    private string NewLocalKey() => (++Db.NextLocalKey).ToString(CultureInfo.InvariantCulture);

    private long NextCounter()
    {
        var next = ++Db.Local.ActorCounter;
        return next;
    }

    private long NextSeq() => ++Db.Local.LastSeq;

    internal IEnumerable<SimRow> Live(string kind)
    {
        var rows = Rows.Where(r => r.Kind == kind && r.IsLive);
        if (!SimKinds.Of(kind).HasOrder) return rows.ToList();
        var order = OrderOf(kind);
        return rows.OrderBy(r => order.IndexOf(r.LocalKey)).ToList();
    }

    // ---- Refresh (§6.1) ---------------------------------------------------------------------------

    /// <summary>
    /// Compares state and issues revisions: local deletions become tombstones, new definitions get keys, moves get
    /// order keys (§3.7), comparison-form changes get <c>LocalEdit</c> revisions, and a revert of a recent apply is
    /// held (§6.5). Skipped entirely while the actor is unverified (§5.6).
    /// </summary>
    public void Refresh()
    {
        if (!Verified) return;
        foreach (var kind in SimKinds.All) RefreshKind(kind);
        WriteWatermark();
    }

    private void RefreshKind(SimKind kind)
    {
        var codec = kind.Codec;
        foreach (var row in Rows.Where(r => r.Kind == kind.Kind && !r.Deleted && r.DeletedLocally).ToList())
        {
            if (!row.HasSideRow)
            {
                Rows.Remove(row);
                continue;
            }

            row.Vv = DataSyncRefreshRules.LocalRevision(row.Vv, Actor, NextCounter, deletion: true);
            row.LastEditor = Editor;
            Tombstone(row, DataSyncTombstoneKind.Deleted);
        }

        foreach (var row in Live(kind.Kind).Where(r => !r.HasSideRow))
        {
            row.Keys.Add(_world.NewKey());
            row.Origin = NodeId;
            row.State = DataSyncEntitySyncState.Synced;
        }

        var moves = kind.HasOrder
            ? DataSyncOrderPlanner.DetectMoves(OrderEntries(kind.Kind), Limits.MaxOrderKeyLength)
            : new Dictionary<string, string>();
        foreach (var row in Live(kind.Kind))
        {
            var json = codec.Write(row.Content!);
            if (row.Seq == 0)
            {
                // A new local definition: its first revision.
                row.OrderKey = moves.GetValueOrDefault(row.LocalKey) ?? row.OrderKey;
                var publication = DataSyncPublication.Of(codec, row.Content!, row.Overlay, row.ChildrenLocal, row.OrderKey,
                    row.Unknown);
                row.LocalHash = ContentHash.Of(json);
                row.SharedHash = publication.SharedHash ?? DataSyncRefreshRules.HeldSharedHash(row.LocalHash);
                row.Vv = DataSyncRefreshRules.LocalRevision(DataSyncVersionVector.Empty, Actor, NextCounter);
                row.Seq = NextSeq();
                row.LastEditor = Editor;
                continue;
            }

            var decision = DataSyncRefreshRules.Evaluate(codec,
                new DataSyncRefreshRow(row.State, row.SharedHash, row.OrderKey, row.Overlay, row.ChildrenLocal, row.PublishHeld,
                    row.Unknown),
                json, false, moves.GetValueOrDefault(row.LocalKey),
                typed => row.LastApply is { } last && DataSyncLostUpdateGuard.InWindow(last.At, Now)
                    ? DataSyncLostUpdateGuard.UndoneChanges(codec, typed, last.Changes)
                    : null);
            row.LocalHash = decision.LocalHash;
            switch (decision.Action)
            {
                case DataSyncRefreshAction.LocalEdit:
                    row.SharedHash = decision.SharedHash;
                    row.OrderKey = decision.OrderKey;
                    row.Vv = DataSyncRefreshRules.LocalRevision(row.Vv, Actor, NextCounter);
                    row.Seq = NextSeq();
                    row.LastEditor = Editor;
                    break;
                case DataSyncRefreshAction.HoldForLostUpdate:
                    _world.Count("lostUpdateHeld");
                    row.PublishHeld = true;
                    row.Seq = NextSeq();
                    UpsertItem(null, DataSyncLostUpdateGuard.Draft(kind.Kind, new EntityKeys(row.Keys), row.LocalKey,
                        row.Name, codec.SubtypeOf(row.Content!), decision.Undone!, row.Vv));
                    break;
                case DataSyncRefreshAction.RefreshHeldItem when row.LastApply is { } applied:
                    var undone = DataSyncLostUpdateGuard.UndoneChanges(codec, row.Content!, applied.Changes);
                    UpsertItem(null, DataSyncLostUpdateGuard.Draft(kind.Kind, new EntityKeys(row.Keys), row.LocalKey,
                        row.Name, codec.SubtypeOf(row.Content!), undone, row.Vv));
                    break;
            }
        }
    }

    /// <summary>The synced, live, published entities of a kind in local order (§3.7 step 1).</summary>
    private List<DataSyncOrderEntry> OrderEntries(string kind) =>
        Live(kind).Where(r => r.HasSideRow && r.State == DataSyncEntitySyncState.Synced && !r.PublishHeld)
            .Select(r => new DataSyncOrderEntry(r.LocalKey, r.OrderKey, DataSyncOrderPlanner.TieKeyOf(new EntityKeys(r.Keys))))
            .ToList();

    /// <summary>
    /// A side row becomes a tombstone (the caller has set its vector and editor): served only when it was synced
    /// and the deletion is a real one (§6.3). Its open items close.
    /// </summary>
    private void Tombstone(SimRow row, DataSyncTombstoneKind kind)
    {
        row.StateAtDeletion = row.State;
        row.Served = kind == DataSyncTombstoneKind.Deleted && DataSyncRefreshRules.ServesTombstone(row.State);
        row.TombstoneKind = kind;
        row.Deleted = true;
        row.DeletedLocally = false;
        row.DeletedAt = Now;
        row.Content = null;
        row.PublishHeld = false;
        row.LastApply = null;
        row.Seq = NextSeq();
        Db.Values.Remove((row.Kind, row.LocalKey));
        Db.Usage.Remove((row.Kind, row.LocalKey));
        if (Db.Order.TryGetValue(row.Kind, out var order)) order.Remove(row.LocalKey);
        foreach (var item in Items.Where(i => i.Open && i.Kind == row.Kind && row.Keys.Contains(i.Key)).ToList())
            Close(item, DataSyncInboxClosure.Superseded, null);
    }

    /// <summary>Writes <c>actor.json</c> after a commit that issued counters or rotated (§4.7).</summary>
    private void WriteWatermark()
    {
        var local = Db.Local;
        Watermark = new SimWatermark(local.Generation, local.ActorId, local.ActorCounter, local.DbInstanceId);
    }

    // ---- the inbox (§9.3) -------------------------------------------------------------------------

    private static DataSyncOpenInboxItem ToOpen(SimItem i) =>
        new(i.Id, i.LinkId, i.Kind, i.Key, i.Type, i.Origin, i.Subject, i.Token, i.RecordVv);

    /// <param name="reconciled">
    /// The draft comes from <see cref="DataSyncInboxRules.Reconcile"/>, which names the open item of its subject:
    /// finding one it did not name means one merge drafted two items with one subject, which a store holding one open
    /// item per subject (§4.2) refuses. That is a violation, never merged silently.
    /// </param>
    /// <returns>The item, and whether it was inserted.</returns>
    private (SimItem Item, bool Inserted) UpsertItem(int? linkId, DataSyncInboxDraft draft, long? existingId = null,
        bool reconciled = false)
    {
        var bySubject = Items.FirstOrDefault(i => i.Open && i.LinkId == linkId && i.Kind == draft.Kind && i.Key == draft.Key &&
                                                  i.Type == draft.Type && i.Subject == draft.SubjectPath);
        if (reconciled && existingId is null && bySubject is not null)
        {
            Violations.Add($"inbox: {Name} got two drafts of {draft.Type} {draft.Kind}/{draft.Key.Value[..6]} " +
                           $"'{draft.SubjectPath}' from one merge");
        }

        var item = existingId is { } id ? Items.Single(i => i.Id == id) : bySubject;
        var inserted = item is null;
        if (item is null)
        {
            item = new SimItem
            {
                Id = ++Db.NextItemId, LinkId = linkId, Kind = draft.Kind, Key = draft.Key, Type = draft.Type,
                Origin = draft.Origin, Subject = draft.SubjectPath, Payload = draft.Payload, Token = draft.Token,
                CreatedAt = Now,
            };
            Items.Add(item);
            _world.Count("item:" + draft.Type + (Headless ? ":headless" : ""));
        }

        item.LocalKey = draft.LocalKey;
        item.Payload = draft.Payload;
        item.RecordHash = draft.RecordHash;
        item.RecordVv = draft.RecordVv;
        item.Token = draft.Token;
        item.Flags = draft.Flags;
        return (item, inserted);
    }

    private void Close(SimItem item, DataSyncInboxClosure closure, DataSyncEditorRef? by)
    {
        if (!item.Open) return;
        item.Closure = closure;
        item.ClosedBy = by;
        foreach (var notification in Notifications.Where(n => !n.Read && n.ItemIds.Contains(item.Id)))
        {
            if (notification.ItemIds.All(id => Items.FirstOrDefault(i => i.Id == id) is not { Open: true }))
                notification.Read = true;
        }
    }

    /// <summary>§9.3 state-derived closure: a state-derived item closes exactly when its state is gone.</summary>
    private void CloseStaleStateItems()
    {
        foreach (var item in Items.Where(i => i.Open && i.Origin == DataSyncInboxItemOrigin.State).ToList())
        {
            var link = LinkById(item.LinkId);
            var row = item.Kind.Length == 0 ? null : Rows.FirstOrDefault(r => r.Kind == item.Kind && r.IsLive && r.Keys.Contains(item.Key));
            var facts = item.Type switch
            {
                DataSyncInboxItemType.ChildDeletedInUse => new DataSyncStateItemFacts(HoldExists: HoldExists(item, row, link)),
                DataSyncInboxItemType.MassChildDeletion => new DataSyncStateItemFacts(BasePendingReason: row is null || link is null
                    ? null
                    : link.Bases.GetValueOrDefault((row.Kind, row.Primary))?.Pending?.Reason),
                DataSyncInboxItemType.SuspectedLostUpdate => new DataSyncStateItemFacts(PublishHeld: row?.PublishHeld ?? false),
                _ => new DataSyncStateItemFacts(LargeChangeRecordsWaiting:
                    link?.Bases.Values.Any(b => b.Pending?.Reason == DataSyncPendingReason.LargeChange) ?? false),
            };
            if (!DataSyncInboxRules.StateItemStands(item.Type, facts)) Close(item, DataSyncInboxClosure.Superseded, null);
        }
    }

    private static string PeerIdOfPath(string subject) => subject[(subject.IndexOf(':') + 1)..];

    /// <summary>The local child a child path names on a link (through the base's child map).</summary>
    private static string LocalChildOf(SimLink? link, SimRow row, string subject)
    {
        var peerId = PeerIdOfPath(subject);
        return link?.Bases.GetValueOrDefault((row.Kind, row.Primary))?.ChildMap.GetValueOrDefault(peerId) ?? peerId;
    }

    private static bool HoldExists(SimItem item, SimRow? row, SimLink? link)
    {
        if (row is null || link is null) return false;
        var local = LocalChildOf(link, row, item.Subject);
        return row.Overlay.HeldChildren.Any(h => h.LinkId == link.Id && h.ChildId == local) &&
               SimKinds.Of(row.Kind).Codec.ChildrenOf(row.Content!).Any(c => c.Id == local);
    }

    /// <summary>Dominance across links (§9.3): any link's merger-derived item a row's vector covers closes.</summary>
    public void CloseDominated()
    {
        foreach (var item in Items.Where(i => i.Open && i.Origin == DataSyncInboxItemOrigin.Merger).ToList())
        {
            var row = Rows.FirstOrDefault(r => r.Kind == item.Kind && r.Keys.Contains(item.Key));
            if (row is null) continue;
            var close = DataSyncInboxRules.DominanceClosure(ToOpen(item), row.Vv, row.LastEditor,
                row.LastEditor?.NodeId == NodeId);
            if (close is not null) Close(item, close.Closure, close.By);
        }
    }

    /// <summary>One notification per link per cycle (§9.4); headless nodes create none.</summary>
    private void Notify(string source, string @case, IEnumerable<long> itemIds)
    {
        if (Headless) return;
        var ids = itemIds.ToList();
        if (@case == "newItems" && Notifications.Any(n => n.Source == source && !n.Read && n.Case == "newItems" &&
                                                        Now - n.At <= TimeSpan.FromHours(1)))
            return;
        var notification = new SimNotification { Source = source, Case = @case, At = Now };
        notification.ItemIds.AddRange(ids);
        Notifications.Add(notification);
    }

    // ---- lookups -----------------------------------------------------------------------------------

    private SimRow? LiveOwner(string kind, SyncKey key) =>
        Rows.FirstOrDefault(r => r.Kind == kind && !r.Deleted && r.Keys.Contains(key));

    private SimRow? TombstoneOwner(string kind, SyncKey key) =>
        Rows.FirstOrDefault(r => r.Kind == kind && r.Deleted && r.Keys.Contains(key));

    private long? SeqOf(string kind, SyncKey key) => Rows.FirstOrDefault(r => r.Kind == kind && r.Keys.Contains(key))?.Seq;

    public DataSyncSourceAttention Attention =>
        new(Headless, OpenDecisions, Links.Values.Count(l => l.Paused is not null), RestorePending, 0);
}
