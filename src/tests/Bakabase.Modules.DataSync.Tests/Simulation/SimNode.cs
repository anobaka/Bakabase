using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>A clock the scenarios move by hand (the lost-update window, §6.5).</summary>
internal sealed class SimClock
{
    public DateTime Now { get; private set; } = new(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>One side row (live or tombstone) with the definition it describes, as the store [C] keeps it.</summary>
internal sealed class SimRow
{
    public required string LocalKey { get; set; }
    public List<SyncKey> Keys { get; } = [];
    public TestItemContent? Content { get; set; }
    public string LocalHash { get; set; } = "";
    public string? SharedHash { get; set; }
    public DataSyncVersionVector Vv { get; set; } = DataSyncVersionVector.Empty;
    public DataSyncEditorRef? LastEditor { get; set; }
    public string? OrderKey { get; set; }
    public DataSyncEntitySyncState State { get; set; } = DataSyncEntitySyncState.Synced;
    public DataSyncOverlay Overlay { get; set; } = DataSyncOverlay.None;
    public bool CreatedBySync { get; set; }
    public bool PublishHeld { get; set; }
    public JsonObject? Unknown { get; set; }
    public long Seq { get; set; }
    public string Origin { get; set; } = "";
    public bool Deleted { get; set; }
    public DataSyncTombstoneKind TombstoneKind { get; set; } = DataSyncTombstoneKind.Deleted;
    public bool Served { get; set; }
    public bool DeletedLocally { get; set; }
    public (DateTime At, DataSyncEntityChangeList Changes)? LastApply { get; set; }
    public SyncKey Primary => Keys[0];
    public string Name => Content?.Name ?? "";
}

internal sealed class SimLink
{
    public required int Id { get; init; }
    public required SimNode Peer { get; init; }
    public DataSyncLinkMode Mode { get; set; }
    public long Cursor { get; set; }
    public Dictionary<SyncKey, DataSyncPeerBase> Bases { get; } = new();
    public DataSyncMergeFlags OnceFlags { get; set; } = DataSyncMergeFlags.None;
    public DataSyncPauseReason? Paused { get; set; }
    public string? PausedDetail { get; set; }
    public bool FirstContact { get; set; } = true;
}

internal sealed class SimItem
{
    public required long Id { get; init; }
    public int? LinkId { get; init; }
    public required string Kind { get; init; }
    public required SyncKey Key { get; init; }
    public string? LocalKey { get; set; }
    public required DataSyncInboxItemType Type { get; init; }
    public required DataSyncInboxItemOrigin Origin { get; init; }
    public required string Subject { get; init; }
    public required DataSyncInboxPayload Payload { get; set; }
    public string? RecordHash { get; set; }
    public DataSyncVersionVector? RecordVv { get; set; }
    public required string Token { get; set; }
    public DataSyncMergeFlags Flags { get; set; } = DataSyncMergeFlags.None;
    public DataSyncInboxClosure? Closure { get; set; }
    public DataSyncEditorRef? ClosedBy { get; set; }
    public bool Open => Closure is null;
}

/// <summary>
/// One device of the scenarios: an adapter over <see cref="TestItemCodec"/> content, the side rows, the links with
/// their bases and pending records, and an inbox with both origins. It runs Refresh through
/// <see cref="DataSyncRefreshRules"/>, serves its feed through the real wire writer and reader, merges with the real
/// <see cref="DataSyncMerger"/>, and records applies through <see cref="DataSyncRecordApply"/> and
/// <see cref="DataSyncRevisionRules"/> — the helpers the Business implementation calls too.
/// </summary>
internal sealed partial class SimNode
{
    public const string Kind = TestItemCodec.Kind;
    private static readonly IDataSyncKindCodec Codec = TestItemCodec.Instance;
    private static readonly DataSyncLimits Limits = DataSyncLimits.Default;
    private static int _nextLinkId;

    private readonly SimClock _clock;
    private int _nextLocalKey;
    private int _nextKey;
    private long _nextItemId;

    public SimNode(string name, SimClock clock, bool headless = false)
    {
        Name = name;
        NodeId = name.ToLowerInvariant().Replace(' ', '-');
        Headless = headless;
        _clock = clock;
        Actor = DataSyncActorId.Derive(NodeId, "epoch", "000000000000000" + (name.Length % 10).ToString(CultureInfo.InvariantCulture));
    }

    public string Name { get; }
    public string NodeId { get; }
    public bool Headless { get; }
    public DataSyncActorId Actor { get; }
    public long ActorCounter { get; private set; }
    public long LastSeq { get; private set; }
    public List<SimRow> Rows { get; } = [];
    public Dictionary<string, SimLink> Links { get; } = new(StringComparer.Ordinal);
    public List<SimItem> Items { get; } = [];
    public Dictionary<string, Dictionary<string, int>> Usage { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Values { get; } = new(StringComparer.Ordinal);
    public List<DataSyncMergeNote> Notes { get; } = [];
    public DataSyncMergeResult? LastResult { get; private set; }

    public DataSyncEditorRef Editor => new(NodeId, Name, Actor.Value);

    /// <summary>What this node's head reports to its readers (§7.5.1): counts only.</summary>
    public int OpenDecisions => Items.Count(i => i.Open);

    public IEnumerable<SimItem> OpenItems => Items.Where(i => i.Open);

    public override string ToString() => Name;

    // ---- links -------------------------------------------------------------------------------------

    /// <summary>This node pulls <paramref name="peer"/>.</summary>
    public SimLink Follow(SimNode peer, DataSyncLinkMode mode = DataSyncLinkMode.TwoWay)
    {
        var link = new SimLink { Id = Interlocked.Increment(ref _nextLinkId), Peer = peer, Mode = mode };
        Links[peer.NodeId] = link;
        return link;
    }

    public SimLink LinkTo(SimNode peer) => Links[peer.NodeId];

    // ---- local edits (through "the services") ------------------------------------------------------

    public SimRow Create(TestItemContent content)
    {
        var row = new SimRow
        {
            LocalKey = (++_nextLocalKey).ToString(CultureInfo.InvariantCulture), Content = content,
            LocalHash = ContentHash.Of(Codec.Write(content)),
        };
        Rows.Add(row);
        return row;
    }

    public SimRow Row(string name) => Rows.Single(r => !r.Deleted && r.Content?.Name == name);

    public SimRow? Find(string name) => Rows.SingleOrDefault(r => !r.Deleted && r.Content?.Name == name);

    public SimRow RowOf(SyncKey key) => Rows.First(r => r.Keys.Contains(key));

    public void Edit(string name, Func<TestItemContent, TestItemContent> change)
    {
        var row = Row(name);
        row.Content = change(row.Content!);
    }

    public void Delete(string name) => Row(name).DeletedLocally = true;

    public void Use(string name, string childId, int resources)
    {
        var row = Row(name);
        if (!Usage.TryGetValue(row.LocalKey, out var usage)) Usage[row.LocalKey] = usage = new Dictionary<string, int>();
        usage[childId] = resources;
    }

    public void SetValues(string name, int values) => Values[Row(name).LocalKey] = values;

    private long NextCounter() => ++ActorCounter;

    private SyncKey NewKey()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(NodeId + "/" + (++_nextKey).ToString(CultureInfo.InvariantCulture)));
        var key = Convert.ToHexStringLower(hash)[..32];
        return new SyncKey(key == new string('0', 32) ? "1" + key[1..] : key);
    }

    // ---- Refresh (§6.1) ---------------------------------------------------------------------------

    public void Refresh()
    {
        foreach (var row in Rows.Where(r => !r.Deleted).ToList())
        {
            if (row.DeletedLocally)
            {
                row.DeletedLocally = false;
                row.Deleted = true;
                row.Served = row.Keys.Count > 0 && DataSyncRefreshRules.ServesTombstone(row.State);
                row.TombstoneKind = DataSyncTombstoneKind.Deleted;
                if (row.Keys.Count > 0)
                {
                    row.Vv = DataSyncRefreshRules.LocalRevision(row.Vv, Actor, NextCounter, deletion: true);
                    row.Seq = ++LastSeq;
                    row.LastEditor = Editor;
                }

                row.Content = null;
                foreach (var item in Items.Where(i => i.Open && row.Keys.Contains(i.Key)))
                    item.Closure = DataSyncInboxClosure.Superseded;
                continue;
            }

            var json = Codec.Write(row.Content!);
            if (row.Keys.Count == 0)
            {
                row.Keys.Add(NewKey());
                row.Origin = NodeId;
                var publication = DataSyncPublication.Of(Codec, row.Content!, row.Overlay, false, row.OrderKey, row.Unknown);
                row.LocalHash = ContentHash.Of(json);
                row.SharedHash = publication.SharedHash ?? DataSyncRefreshRules.HeldSharedHash(row.LocalHash);
                row.Vv = DataSyncRefreshRules.LocalRevision(DataSyncVersionVector.Empty, Actor, NextCounter);
                row.Seq = ++LastSeq;
                row.LastEditor = Editor;
                continue;
            }

            var decision = DataSyncRefreshRules.Evaluate(Codec,
                new DataSyncRefreshRow(row.State, row.SharedHash, row.OrderKey, row.Overlay, false, row.PublishHeld,
                    row.Unknown),
                json, false, null,
                typed => row.LastApply is { } last && DataSyncLostUpdateGuard.InWindow(last.At, _clock.Now)
                    ? DataSyncLostUpdateGuard.UndoneChanges(Codec, typed, last.Changes)
                    : null);
            row.LocalHash = decision.LocalHash;
            switch (decision.Action)
            {
                case DataSyncRefreshAction.LocalEdit:
                    row.SharedHash = decision.SharedHash;
                    row.OrderKey = decision.OrderKey;
                    row.Vv = DataSyncRefreshRules.LocalRevision(row.Vv, Actor, NextCounter);
                    row.Seq = ++LastSeq;
                    row.LastEditor = Editor;
                    break;
                case DataSyncRefreshAction.HoldForLostUpdate:
                    row.PublishHeld = true;
                    row.Seq = ++LastSeq;
                    Upsert(null, DataSyncLostUpdateGuard.Draft(Kind, new EntityKeys(row.Keys), row.LocalKey, row.Name, null,
                        decision.Undone!, row.Vv));
                    break;
            }
        }
    }

    // ---- the feed (§7.5) --------------------------------------------------------------------------

    public DataSyncStagedPull Feed(long since)
    {
        Refresh();
        var records = new List<DataSyncWireRecord>();
        foreach (var row in Rows.Where(r => r.Keys.Count > 0 && r.Seq > since).OrderBy(r => r.Seq))
        {
            var keys = row.Keys.Select(k => k.Value).ToList();
            if (row.Deleted)
            {
                if (!row.Served) continue;
                records.Add(new DataSyncWireRecord(keys, row.Origin, row.Seq, row.Vv, row.LastEditor, true, 1, null, null,
                    null, null, 0));
                continue;
            }

            if (row.State != DataSyncEntitySyncState.Synced) continue;
            var publication = DataSyncPublication.Of(Codec, row.Content!, row.Overlay, false, row.OrderKey, row.Unknown);
            var held = row.PublishHeld ? Planning.DataSyncHeldReason.PendingDecision : publication.Held;
            records.Add(new DataSyncWireRecord(keys, row.Origin, row.Seq, row.Vv, row.LastEditor, false, 1, row.OrderKey,
                held is null ? publication.Content : null, held is null ? publication.Hash : null, held, 0));
        }

        var written = DataSyncWireWriter.WriteKind("snap", Kind, since, records, Limits);
        var assembler = new DataSyncRecordAssembler(Codec, Limits);
        foreach (var page in written.Pages) assembler.Add(DataSyncWireReader.ReadPage(page, "snap", Kind, Limits));
        var live = Rows.Count(r => !r.Deleted && r.Keys.Count > 0 && r.State == DataSyncEntitySyncState.Synced);
        var feedKind = new DataSyncFeedKind(Kind, 1, LastSeq, 0, live, Rows.Count(r => r.Deleted && r.Served),
            written.ContentHash, since, written.Records.Count, false);
        var staged = assembler.Complete(feedKind, since == 0);
        if (assembler.Problem is { } problem) throw new InvalidOperationException($"{Name}'s feed: {problem}");
        var manifest = new DataSyncFeedManifest("snap", 120_000, NodeId, "epoch", Actor.Value, DataSyncContract.Version,
            DataSyncContract.MinimumPeerVersion, "1.0.0", [feedKind], null,
            new DataSyncSourceAttention(Headless, OpenDecisions, 0, false, 0));
        return new DataSyncStagedPull(NodeId, Name, manifest, [staged], _clock.Now);
    }

    // ---- a pull (§8.10.2) -------------------------------------------------------------------------

    /// <summary>Pulls <paramref name="peer"/> through this node's link to it: fetch, merge, apply.</summary>
    public DataSyncMergeResult Pull(SimNode peer)
    {
        var link = LinkTo(peer);
        var result = Merge(link, peer.Feed(link.Cursor), null);
        link.FirstContact = false;
        return result;
    }

    /// <summary>Re-merges pending records of one link without a pull (a resolution's re-merge, Apply all).</summary>
    public DataSyncMergeResult Remerge(SimLink link, IEnumerable<SyncKey> keys) =>
        Merge(link, null, keys.Select(k => (Kind, k)).ToList());

    private DataSyncMergeResult Merge(SimLink link, DataSyncStagedPull? pull, IReadOnlyList<(string, SyncKey)>? pending)
    {
        Refresh();
        var full = pull?.Kinds.Any(k => k.FullReconciliation) ?? false;
        pending ??= link.Bases.Values
            .Where(b => b.Pending is { } p && DataSyncPendingRecords.ShouldRemerge(p, SeqOf(b.Key), full, link.OnceFlags, false))
            .Select(b => (Kind, b.Key)).ToList();
        var back = link.Peer.Links.GetValueOrDefault(NodeId);
        var effective = link.Mode == DataSyncLinkMode.Follow && back?.Mode == DataSyncLinkMode.Follow
            ? DataSyncLinkMode.TwoWay
            : link.Mode;
        var context = new DataSyncLinkContext(link.Id, link.Peer.NodeId, link.Peer.Name, link.Mode, effective, [Kind],
            link.FirstContact ? [Kind] : [], Headless, Actor, new Dictionary<string, long> { [Actor.Value] = ActorCounter },
            link.Peer.Actor.Value, new Dictionary<string, int> { [Kind] = Codec.ComparisonFormVersion }, link.OnceFlags);
        var bases = link.Bases.ToDictionary(b => (Kind, b.Key), b => b.Value);
        var open = Items.Where(i => i.Open && i.LinkId == link.Id && i.Origin == DataSyncInboxItemOrigin.Merger)
            .Select(ToOpen).ToList();
        var input = new DataSyncMergeInput(context, pull,
            new Dictionary<string, DataSyncLocalKindState> { [Kind] = LocalState() }, bases, pending,
            new Dictionary<string, IDataSyncKindCodec> { [Kind] = Codec },
            new Dictionary<(string, string), IReadOnlyDictionary<string, int>>(), new Dictionary<(string, string), int>(),
            open, DataSyncAutoApplyPolicy.Default, Limits);

        var usage = new Dictionary<(string, string), IReadOnlyDictionary<string, int>>();
        var values = new Dictionary<(string, string), int>();
        foreach (var query in DataSyncMerger.CollectUsageQueries(input))
        {
            var known = Usage.GetValueOrDefault(query.LocalKey) ?? new Dictionary<string, int>();
            usage[(query.Kind, query.LocalKey)] = query.ChildIds.ToDictionary(id => id, id => known.GetValueOrDefault(id));
            if (query.NeedValueCount) values[(query.Kind, query.LocalKey)] = Values.GetValueOrDefault(query.LocalKey);
        }

        var result = DataSyncMerger.Merge(input with { ChildUsage = usage, ValueCounts = values });
        LastResult = result;
        if (result.Anomaly is not null || result.Pause is not null)
        {
            link.Paused = result.Pause;
            link.PausedDetail = result.PauseDetail;
            return result;
        }

        Apply(link, pull, bases, result);
        link.OnceFlags = DataSyncMergeFlags.None;
        return result;
    }

    private long? SeqOf(SyncKey key) => Rows.FirstOrDefault(r => r.Keys.Contains(key))?.Seq;

    private DataSyncLocalKindState LocalState()
    {
        var entities = Rows.Where(r => !r.Deleted && r.Keys.Count > 0)
            .Select(r => new DataSyncLocalEntityState(r.LocalKey, new EntityKeys(r.Keys), r.Content!, r.LocalHash,
                r.SharedHash ?? "", r.Vv, r.LastEditor is { } e ? new DataSyncActorId(e.ActorId) : null, r.LastEditor,
                r.OrderKey, r.State, r.Overlay, false, r.CreatedBySync, r.PublishHeld, r.Unknown, null, r.Seq))
            .ToList();
        var tombstones = Rows.Where(r => r.Deleted && r.Keys.Count > 0)
            .Select(r => new DataSyncTombstoneState(new EntityKeys(r.Keys), r.Vv, r.LastEditor,
                r.Served ? DataSyncEntitySyncState.Synced : DataSyncEntitySyncState.LocalOnly, r.TombstoneKind, r.Served, r.Seq))
            .ToList();
        return new DataSyncLocalKindState(Kind, entities, tombstones);
    }

    // ---- apply (§8.10.2 apply half) ---------------------------------------------------------------

    private void Apply(SimLink link, DataSyncStagedPull? pull, IReadOnlyDictionary<(string, SyncKey), DataSyncPeerBase> bases,
        DataSyncMergeResult result)
    {
        var records = (pull?.Kinds.SelectMany(k => k.Entities).Select(e => e.Record) ?? [])
            .Concat(link.Bases.Values.Where(b => b.Pending is not null).Select(b => b.Pending!.Record)).ToList();
        var before = new Dictionary<SimRow, TestItemContent?>();
        var changed = new List<string>();
        foreach (var op in result.Batches.SelectMany(b => b.Operations))
        {
            switch (op)
            {
                case CreateEntityOperation create:
                    var revived = Rows.FirstOrDefault(r => r.Deleted && r.Keys.Any(create.Keys.Contains));
                    var row = revived ?? new SimRow { LocalKey = "" };
                    row.LocalKey = (++_nextLocalKey).ToString(CultureInfo.InvariantCulture);
                    foreach (var key in create.Keys.All.Where(k => !row.Keys.Contains(k))) row.Keys.Add(key);
                    row.Content = (TestItemContent)Codec.ReadLocal(create.Content);
                    row.Origin = create.OriginNodeId;
                    row.CreatedBySync = true;
                    row.Deleted = false;
                    row.State = DataSyncEntitySyncState.Synced;
                    before[row] = null;
                    if (revived is null) Rows.Add(row);
                    break;
                case UpdateEntityOperation update:
                    var target = Rows.Single(r => !r.Deleted && r.LocalKey == update.LocalKey);
                    if (target.LocalHash != update.ExpectedLocalHash)
                    {
                        changed.Add(update.ItemId);
                        break;
                    }

                    before[target] = target.Content;
                    target.Content = (TestItemContent)Codec.ReadLocal(update.MergedContent);
                    AddAliases(target, update.AliasKeysToAdd);
                    break;
                case BindOnlyOperation bind:
                    var bound = Rows.Single(r => !r.Deleted && r.LocalKey == bind.LocalKey);
                    AddAliases(bound, bind.AliasKeysToAdd);
                    bound.Seq = ++LastSeq;
                    break;
                case DeleteEntityOperation delete:
                    var deleted = Rows.Single(r => !r.Deleted && r.LocalKey == delete.LocalKey);
                    before[deleted] = deleted.Content;
                    break;
            }
        }

        if (changed.Count > 0) result = DataSyncRecordApply.WithoutChangedDuringApply(result, changed, bases);

        foreach (var overlay in result.OverlayChanges)
        {
            var row = Rows.Single(r => !r.Deleted && r.LocalKey == overlay.LocalKey);
            row.Overlay = row.Overlay with
            {
                HeldChildren = row.Overlay.HeldChildren.Where(h => !overlay.Release.Contains(h)).Concat(overlay.Hold).ToList(),
            };
        }

        foreach (var revision in result.Revisions)
        {
            var row = revision.LocalKey is { } localKey
                ? Rows.Single(r => r.LocalKey == localKey && (r.Deleted == false || revision.Revision == DataSyncRevisionKind.AcceptRemoteDelete))
                : Rows.Single(r => r.Keys.Contains(revision.Keys.Primary!.Value));
            var record = records.LastOrDefault(r => r.Keys.Any(k => revision.Keys.Contains(new SyncKey(k))));
            var remoteShared = record is null ? null : DataSyncPublication.SharedHashOfRecord(Codec, record, Limits);
            var isCreate = revision.Revision is DataSyncRevisionKind.Create or DataSyncRevisionKind.Revive;
            var deletion = revision.Revision == DataSyncRevisionKind.AcceptRemoteDelete;
            var applied = DataSyncRecordApply.Revise(Codec, revision, isCreate ? DataSyncVersionVector.Empty : row.Vv,
                isCreate ? null : row.SharedHash, deletion ? null : row.Content, row.Overlay, remoteShared,
                record?.EditedBy, Editor, Actor, NextCounter);
            row.Vv = applied.Vv;
            row.LastEditor = applied.LastEditor;
            row.LocalHash = applied.LocalHash ?? row.LocalHash;
            row.SharedHash = applied.SharedHash ?? row.SharedHash;
            row.OrderKey = revision.OrderKey;
            row.Unknown = revision.Unknown;
            row.Seq = ++LastSeq;
            if (deletion)
            {
                row.Deleted = true;
                row.Served = true;
                row.TombstoneKind = DataSyncTombstoneKind.Deleted;
                row.Content = null;
            }

            if (before.TryGetValue(row, out var was) && was is not null && row.Content is not null)
                row.LastApply = (_clock.Now, DataSyncEntityChangeList.Between(Codec, was, row.Content));
        }

        foreach (var update in result.BaseUpdates) UpsertBase(link, update);
        foreach (var (_, key) in result.TombstonesToServe ?? [])
        {
            var row = RowOf(key);
            row.Served = true;
            row.Seq = ++LastSeq;
        }

        Notes.AddRange(result.Notes);
        var reconciliation = DataSyncInboxRules.Reconcile(
            Items.Where(i => i.Open && i.LinkId == link.Id).Select(ToOpen).ToList(), result.Inbox, result.Evaluated,
            result.ClosureHints);
        foreach (var upsert in reconciliation.Upserts) Upsert(link.Id, upsert.Draft, upsert.ExistingId);
        foreach (var close in reconciliation.Closes) Close(Items.Single(i => i.Id == close.ItemId), close.Closure, close.By);
        CloseStaleStateItems();
        CloseDominated();
        if (result.CursorAdvance.TryGetValue(Kind, out var cursor)) link.Cursor = cursor;
    }

    private static void AddAliases(SimRow row, EntityKeys aliases)
    {
        foreach (var key in aliases.All.Where(k => !row.Keys.Contains(k))) row.Keys.Add(key);
    }

    /// <summary>The store's base write (C's <c>UpsertBasesAsync</c> semantics).</summary>
    public static void UpsertBase(SimLink link, DataSyncBaseUpdate update)
    {
        var existing = link.Bases.GetValueOrDefault(update.Key);
        var excluded = update.State == DataSyncBaseState.Excluded;
        var exclusionKeys = (existing?.ExclusionKeys ?? []).ToList();
        if (excluded)
        {
            exclusionKeys.Add(update.Key.Value);
            exclusionKeys.AddRange(update.Record?.Keys ?? []);
        }

        var record = !excluded && update.Record is not null ? update.Record : existing?.Record;
        var pending = update.Pending ?? (update.ClearPending ? null : existing?.Pending);
        link.Bases[update.Key] = new DataSyncPeerBase(Kind, update.Key, update.State, excluded ? update.Exclusion : null,
            record?.Vv, update.ChildMap ?? existing?.ChildMap ?? new Dictionary<string, string>(), pending, record,
            excluded ? exclusionKeys.Distinct().ToList() : []);
    }

    // ---- the inbox (§9.3) -------------------------------------------------------------------------

    private static DataSyncOpenInboxItem ToOpen(SimItem i) =>
        new(i.Id, i.LinkId, i.Kind, i.Key, i.Type, i.Origin, i.Subject, i.Token, i.RecordVv);

    private void Upsert(int? linkId, DataSyncInboxDraft draft, long? existingId = null)
    {
        var item = existingId is { } id
            ? Items.Single(i => i.Id == id)
            : Items.FirstOrDefault(i => i.Open && i.LinkId == linkId && i.Kind == draft.Kind && i.Key == draft.Key &&
                                        i.Type == draft.Type && i.Subject == draft.SubjectPath);
        if (item is null)
        {
            item = new SimItem
            {
                Id = ++_nextItemId, LinkId = linkId, Kind = draft.Kind, Key = draft.Key, Type = draft.Type,
                Origin = draft.Origin, Subject = draft.SubjectPath, Payload = draft.Payload, Token = draft.Token,
            };
            Items.Add(item);
        }

        item.LocalKey = draft.LocalKey;
        item.Payload = draft.Payload;
        item.RecordHash = draft.RecordHash;
        item.RecordVv = draft.RecordVv;
        item.Token = draft.Token;
        item.Flags = draft.Flags;
    }

    private static void Close(SimItem item, DataSyncInboxClosure closure, DataSyncEditorRef? by)
    {
        item.Closure = closure;
        item.ClosedBy = by;
    }

    private void CloseStaleStateItems()
    {
        foreach (var item in Items.Where(i => i.Open && i.Origin == DataSyncInboxItemOrigin.State).ToList())
        {
            var link = item.LinkId is { } id ? Links.Values.FirstOrDefault(l => l.Id == id) : null;
            var row = item.Kind.Length == 0 ? null : Rows.FirstOrDefault(r => !r.Deleted && r.Keys.Contains(item.Key));
            var facts = item.Type switch
            {
                DataSyncInboxItemType.ChildDeletedInUse => new DataSyncStateItemFacts(HoldExists: HoldExists(item, row, link)),
                DataSyncInboxItemType.MassChildDeletion => new DataSyncStateItemFacts(
                    BasePendingReason: link?.Bases.GetValueOrDefault(row?.Primary ?? item.Key)?.Pending?.Reason),
                DataSyncInboxItemType.SuspectedLostUpdate => new DataSyncStateItemFacts(PublishHeld: row?.PublishHeld ?? false),
                _ => new DataSyncStateItemFacts(LargeChangeRecordsWaiting:
                    link?.Bases.Values.Any(b => b.Pending?.Reason == DataSyncPendingReason.LargeChange) ?? false),
            };
            if (!DataSyncInboxRules.StateItemStands(item.Type, facts)) Close(item, DataSyncInboxClosure.Superseded, null);
        }
    }

    private static bool HoldExists(SimItem item, SimRow? row, SimLink? link)
    {
        if (row is null || link is null) return false;
        var peerId = item.Subject[(item.Subject.IndexOf(':') + 1)..];
        var local = link.Bases.GetValueOrDefault(row.Primary)?.ChildMap.GetValueOrDefault(peerId) ?? peerId;
        return row.Overlay.HeldChildren.Any(h => h.LinkId == link.Id && h.ChildId == local) &&
               row.Content!.Children.Any(c => c.Id == local);
    }

    /// <summary>Dominance across links (§9.3): after a commit, any link's merger-derived item a row's vector covers closes.</summary>
    public void CloseDominated()
    {
        foreach (var item in Items.Where(i => i.Open && i.Origin == DataSyncInboxItemOrigin.Merger).ToList())
        {
            var row = Rows.FirstOrDefault(r => r.Keys.Contains(item.Key));
            if (row is null) continue;
            var close = DataSyncInboxRules.DominanceClosure(ToOpen(item), row.Vv, row.LastEditor,
                row.LastEditor?.NodeId == NodeId);
            if (close is not null) Close(item, close.Closure, close.By);
        }
    }
}
