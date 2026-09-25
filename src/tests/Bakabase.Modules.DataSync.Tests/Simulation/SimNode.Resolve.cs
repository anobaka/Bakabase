using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

// The §9.2 action table. Vectors come from DataSyncRevisionRules only; re-merges run through the real merger.
internal sealed partial class SimNode
{
    public SimItem Item(DataSyncInboxItemType type) => OpenItems.Single(i => i.Type == type);

    /// <summary>Whether the item's link works as TwoWay (§8.1); false for items that belong to no link.</summary>
    public bool IsTwoWay(int? linkId) => LinkById(linkId) is { } link && EffectiveMode(link) == DataSyncLinkMode.TwoWay;

    public IReadOnlyList<DataSyncInboxAction> AllowedActions(SimItem item) =>
        DataSyncInboxRules.AllowedActions(item.Type, item.Subject, item.Payload, IsTwoWay(item.LinkId));

    /// <summary>
    /// Resolves one item (for conflicts, every open conflict item of its entity with every device, §9.2). The item
    /// is validated first: a merger-derived item whose pending record is gone, or a state-derived item whose state
    /// is gone, closes <c>Superseded</c> instead.
    /// </summary>
    /// <returns>False when the item closed as stale instead.</returns>
    public bool Resolve(SimItem item, DataSyncInboxAction action, string? custom = null, string? targetLocalKey = null)
    {
        if (!item.Open) throw new InvalidOperationException("The item is closed.");
        if (!AllowedActions(item).Contains(action))
            throw new InvalidOperationException($"{action} is not allowed for {item.Type}.");
        if (!Verified) throw new InvalidOperationException("Resolving waits until the actor is verified.");
        CheckActor();
        Refresh();
        if (!item.Open) return false;
        if (!Validate(item))
        {
            Close(item, DataSyncInboxClosure.Superseded, null);
            CloseStaleStateItems();
            return false;
        }

        var targeted = action is DataSyncInboxAction.Link or DataSyncInboxAction.KeepWithEntity or DataSyncInboxAction.KeepRecordLinked;
        if (item.Origin == DataSyncInboxItemOrigin.Merger && !Rederives(item, targeted ? targetLocalKey ?? "" : null))
        {
            // §9.2: a card the merger no longer derives the same way is updated (or closed), never applied.
            _world.Count("resolve:rederived");
            var link = LinkById(item.LinkId)!;
            var pending = PendingKeysOf(item, link);
            if (pending.Count > 0) Remerge(link, pending);
            if (item.Open && !Rederives(item)) Close(item, DataSyncInboxClosure.Superseded, null);
            CloseStaleStateItems();
            return false;
        }

        var history = new SimHistoryEntry { Id = ++Db.NextHistoryId, Kind = DataSyncHistoryKind.Resolution, At = Now };
        _world.Log($"{Name}: resolve {item} with {action}");
        _world.Count($"resolve:{item.Type}:{action}");
        try
        {
            Apply(item, action, custom, targetLocalKey, history);
        }
        catch (StaleItemException)
        {
            // §9.2: the card no longer matches what the merger derives (its target went away): it is derived again.
            _world.Log($"{Name}: {item} is stale; derived again");
            if (LinkById(item.LinkId) is { } link && PendingKeysOf(item, link) is { Count: > 0 } pending) Remerge(link, pending);
            CloseStaleStateItems();
            return false;
        }

        if (history.Changes.Count > 0) Db.History.Add(history);
        CloseStaleStateItems();
        CloseDominated();
        WriteWatermark();
        return true;
    }

    private sealed class StaleItemException : Exception;

    private void Apply(SimItem item, DataSyncInboxAction action, string? custom, string? targetLocalKey, SimHistoryEntry history)
    {
        switch (item.Type)
        {
            case DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict:
                if (action == DataSyncInboxAction.Detach) Detach(RowOf(item.Kind, item.Key));
                else ResolveConflicts(item, action, custom, history);
                break;
            case DataSyncInboxItemType.TypeChange:
                ResolveTypeChange(item, action, history);
                break;
            case DataSyncInboxItemType.DeletedThere:
                ResolveDeletedThere(item, action, history);
                break;
            case DataSyncInboxItemType.DeletedHereEditedThere:
                ResolveDeletedHere(item, action, history);
                break;
            case DataSyncInboxItemType.ChildDeletedInUse:
                ResolveChildInUse(item, action);
                break;
            case DataSyncInboxItemType.LinkSuggestion:
                ResolveSuggestion(item, action, custom, targetLocalKey, history);
                break;
            case DataSyncInboxItemType.IdentityConflict:
                ResolveIdentity(item, action, targetLocalKey);
                break;
            case DataSyncInboxItemType.MassChildDeletion:
            {
                var link = LinkById(item.LinkId)!;
                var row = RowOf(item.Kind, item.Key);
                var b = link.Bases[(row.Kind, row.Primary)];
                var mode = action switch
                {
                    DataSyncInboxAction.ApplyAll => DataSyncChildDeletionMode.Apply,
                    DataSyncInboxAction.ReviewEach => DataSyncChildDeletionMode.ReviewEach,
                    _ => DataSyncChildDeletionMode.Restore,
                };
                link.Bases[(row.Kind, row.Primary)] = b with
                {
                    Pending = b.Pending! with { Flags = b.Pending.Flags with { ChildDeletions = mode } },
                };
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
                Remerge(link, [(row.Kind, row.Primary)]);
                break;
            }
            case DataSyncInboxItemType.LargeChange:
            {
                var link = LinkById(item.LinkId)!;
                link.OnceFlags = link.OnceFlags with { SkipLargeChange = true };
                Remerge(link, link.Bases.Where(x => x.Value.Pending?.Reason == DataSyncPendingReason.LargeChange)
                    .Select(x => x.Key).ToList());
                break;
            }
            case DataSyncInboxItemType.SuspectedLostUpdate:
                ResolveLostUpdate(item, action, history);
                break;
            default:
                throw new NotSupportedException($"{item.Type}/{action} is not emulated.");
        }
    }

    /// <summary>A target the card named must still be a live synced candidate here.</summary>
    private SimRow Target(string kind, string? localKey) =>
        localKey is not null && LiveRow(kind, localKey) is { State: DataSyncEntitySyncState.Synced } row
            ? row
            : throw new StaleItemException();

    /// <summary>§9.2 step 2: the item's state (state-derived) or pending record (merger-derived) must still exist.</summary>
    private bool Validate(SimItem item)
    {
        var link = LinkById(item.LinkId);
        if (item.LinkId is not null && link is null) return false;
        switch (item.Type)
        {
            case DataSyncInboxItemType.SuspectedLostUpdate:
                return Rows.Any(r => r.Kind == item.Kind && r.IsLive && r.Keys.Contains(item.Key) && r.PublishHeld);
            case DataSyncInboxItemType.LargeChange:
                return link!.Bases.Values.Any(b => b.Pending?.Reason == DataSyncPendingReason.LargeChange);
            case DataSyncInboxItemType.ChildDeletedInUse:
                return HoldExists(item, Rows.FirstOrDefault(r => r.Kind == item.Kind && r.IsLive && r.Keys.Contains(item.Key)), link);
            case DataSyncInboxItemType.LinkSuggestion or DataSyncInboxItemType.IdentityConflict:
                return link!.Bases.GetValueOrDefault((item.Kind, item.Key))?.Pending is not null ||
                       Rows.FirstOrDefault(r => r.Kind == item.Kind && r.Keys.Contains(item.Key)) is { } bound &&
                       link.Bases.GetValueOrDefault((bound.Kind, bound.Primary))?.Pending is not null;
            default:
                var row = Rows.FirstOrDefault(r => r.Kind == item.Kind && r.Keys.Contains(item.Key));
                return row is not null && link!.Bases.GetValueOrDefault((row.Kind, row.Primary))?.Pending is not null &&
                       (item.Type == DataSyncInboxItemType.DeletedHereEditedThere ? row.Deleted : row.IsLive);
        }
    }

    private static object? ContentOf(string kind, DataSyncWireRecord record) =>
        record.Content is null ? null : SimKinds.Of(kind).Codec.Read(record.Content, SimKinds.Limits).Content;

    /// <summary>
    /// KeepLocal / UseRemote / UseCustom: every open conflict item of the entity, with every device, in one batch;
    /// the revision is <c>Max(L, R of every item) + self</c>, every link's base takes its record, and every item
    /// closes (§9.2).
    /// </summary>
    private void ResolveConflicts(SimItem chosen, DataSyncInboxAction action, string? custom, SimHistoryEntry history)
    {
        var row = RowOf(chosen.Kind, chosen.Key);
        var kind = SimKinds.Of(row.Kind);
        var items = OpenItems.Where(i => i.Kind == chosen.Kind && row.Keys.Contains(i.Key) &&
                                         i.Type is DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict)
            .ToList();
        var before = row.Content!;
        var content = before;
        var remote = DataSyncVersionVector.Empty;
        var agreed = new List<(SimLink Link, DataSyncWireRecord Record)>();
        foreach (var item in items)
        {
            var link = LinkById(item.LinkId);
            var b = link?.Bases.GetValueOrDefault((row.Kind, row.Primary));
            if (link is null || b?.Pending is not { } pending) continue;
            remote = DataSyncVersionVector.Max(remote, pending.Record.Vv);
            if (agreed.All(a => a.Link != link)) agreed.Add((link, pending.Record));
            if (action == DataSyncInboxAction.KeepLocal) continue;
            var value = kind.SetField(content, item.Subject, ContentOf(row.Kind, pending.Record),
                action == DataSyncInboxAction.UseCustom ? custom ?? kind.NameOf(content) + " ✓" : null, b.ChildMap);
            content = value ?? content;
        }

        row.Content = content;
        Revise(row, DataSyncRevisionKind.Resolution, remote);
        if (!Equals(before, content))
            history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "updated", before, content, chosen.LinkId, []));
        foreach (var (link, record) in agreed)
            UpsertBase(link, new DataSyncBaseUpdate(row.Kind, row.Primary, DataSyncBaseState.Normal, null, record, null, null, true));
        foreach (var item in items) Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveTypeChange(SimItem item, DataSyncInboxAction action, SimHistoryEntry history)
    {
        var row = RowOf(item.Kind, item.Key);
        var link = LinkById(item.LinkId)!;
        var b = link.Bases[(row.Kind, row.Primary)];
        var record = b.Pending!.Record;
        switch (action)
        {
            case DataSyncInboxAction.Detach:
                Detach(row);
                return;
            case DataSyncInboxAction.KeepLocal:
                // A revision that dominates R with the local type; the peer then gets the TypeChange item.
                Revise(row, DataSyncRevisionKind.Resolution, record.Vv);
                UpsertBase(link, new DataSyncBaseUpdate(row.Kind, row.Primary, DataSyncBaseState.Normal, null, record, null, null, true));
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
                return;
            default:
            {
                // Convert (§8.5.6): phase one changes the subtype (the service rebuilds the children); phase two is
                // the merger's Convert merge of the waiting record against the re-read entity, with no Refresh between.
                var kind = SimKinds.Of(row.Kind);
                var before = row.Content!;
                var subtype = ContentOf(row.Kind, record) is { } theirs ? kind.Codec.SubtypeOf(theirs) : null;
                row.Content = kind.ChangeSubtype(before, subtype, _world.NewChildId);
                Db.Usage.Remove((row.Kind, row.LocalKey));
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
                Remerge(link, [(row.Kind, row.Primary)], skipRefresh: true);
                if (row.IsLive && row.Content is not null)
                    history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "updated", before, row.Content, link.Id, []));
                return;
            }
        }
    }

    private void ResolveDeletedThere(SimItem item, DataSyncInboxAction action, SimHistoryEntry history)
    {
        var row = RowOf(item.Kind, item.Key);
        var link = LinkById(item.LinkId)!;
        var b = link.Bases[(row.Kind, row.Primary)];
        var record = b.Pending!.Record;
        switch (action)
        {
            case DataSyncInboxAction.DeleteHere:
                history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "deleted", row.Content, null, link.Id, []));
                row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.AcceptRemoteDelete, row.Vv, record.Vv, false, false,
                    Actor, NextCounter);
                row.LastEditor = Editor;
                Tombstone(row, DataSyncTombstoneKind.Deleted);
                UpsertBase(link, new DataSyncBaseUpdate(row.Kind, b.Key, DataSyncBaseState.Normal, null, record, null, null, true));
                break;
            case DataSyncInboxAction.KeepHereOnly:
                Detach(row);
                break;
            default:
                // RestoreEverywhere: a revision that dominates the tombstone; the peer gets DeletedHereEditedThere.
                Revise(row, DataSyncRevisionKind.Resolution, record.Vv);
                UpsertBase(link, new DataSyncBaseUpdate(row.Kind, b.Key, b.State, b.Exclusion, null, null, null, true));
                break;
        }

        foreach (var open in OpenItems.Where(i => i.Kind == item.Kind && row.Keys.Contains(i.Key)).ToList())
            Close(open, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveDeletedHere(SimItem item, DataSyncInboxAction action, SimHistoryEntry history)
    {
        var row = RowOf(item.Kind, item.Key);
        var link = LinkById(item.LinkId)!;
        var b = link.Bases[(row.Kind, row.Primary)];
        var record = b.Pending!.Record;
        if (action == DataSyncInboxAction.RestoreHere)
        {
            // The definition returns (empty of values), reviving the tombstone's key: Max(T, R) + self.
            var kind = SimKinds.Of(row.Kind);
            row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Resolution, row.Vv, record.Vv, false, false, Actor,
                NextCounter);
            row.Content = kind.Store(ContentOf(row.Kind, record)!, null);
            row.Deleted = false;
            row.DeletedAt = null;
            row.CreatedBySync = false;
            row.State = DataSyncEntitySyncState.Synced;
            row.LocalKey = NewLocalKey();
            row.OrderKey = record.OrderKey;
            row.Unknown = ContentOf(row.Kind, record) is null ? null : kind.Codec.Read(record.Content!, Limits).Unknown;
            row.LocalHash = ContentHash.Of(kind.Codec.Write(row.Content));
            row.SharedHash = DataSyncPublication.Of(kind.Codec, row.Content, row.Overlay, false, row.OrderKey, row.Unknown).SharedHash;
            row.LastEditor = Editor;
            row.Seq = NextSeq();
            if (kind.HasOrder) PlaceNew(row);
            history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "created", null, row.Content, link.Id, []));
            UpsertBase(link, new DataSyncBaseUpdate(row.Kind, b.Key, DataSyncBaseState.Normal, null, record, null, null, true));
        }
        else
        {
            row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.KeepDeleted, row.Vv, record.Vv, false, false, Actor,
                NextCounter);
            row.LastEditor = Editor;
            row.Seq = NextSeq();
            UpsertBase(link, new DataSyncBaseUpdate(row.Kind, b.Key, b.State, b.Exclusion, null, null, null, true));
        }

        Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveChildInUse(SimItem item, DataSyncInboxAction action)
    {
        var row = RowOf(item.Kind, item.Key);
        var kind = SimKinds.Of(row.Kind);
        var link = LinkById(item.LinkId)!;
        var local = LocalChildOf(link, row, item.Subject);
        var hold = new DataSyncHeldChild(local, link.Id);
        var held = row.Overlay.HeldChildren.Where(h => h != hold).ToList();
        switch (action)
        {
            case DataSyncInboxAction.DeleteHere:
                // Never published, so no revision: the values keep the id and show nothing.
                row.Content = kind.WithoutChildren(row.Content!, [local]);
                row.Overlay = row.Overlay with
                {
                    HeldChildren = held.Where(h => h.ChildId != local).ToList(),
                };
                row.LocalHash = ContentHash.Of(kind.Codec.Write(row.Content));
                break;
            case DataSyncInboxAction.KeepHereOnly:
                row.Overlay = new DataSyncOverlay([.. row.Overlay.LocalOnlyChildren.Where(c => c != local), local],
                    held.Where(h => h.ChildId != local).ToList());
                break;
            default:
                // RestoreEverywhere: released and published again; the peer receives it as an addition.
                row.Overlay = row.Overlay with { HeldChildren = held };
                Revise(row, DataSyncRevisionKind.Resolution, link.Bases.GetValueOrDefault((row.Kind, row.Primary))?.Vv);
                break;
        }

        Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveSuggestion(SimItem item, DataSyncInboxAction action, string? newName, string? targetLocalKey,
        SimHistoryEntry history)
    {
        var link = LinkById(item.LinkId)!;
        var b = link.Bases[(item.Kind, item.Key)];
        var record = b.Pending!.Record;
        var kind = SimKinds.Of(item.Kind);
        switch (action)
        {
            case DataSyncInboxAction.Link:
            {
                // BindOnly (R's keys become the candidate's aliases, never a key live elsewhere), then R merges with
                // it as K4/K5/K6 without a base; the Unbound base is replaced by the candidate's.
                var target = Target(item.Kind, targetLocalKey);
                var keys = record.Keys.Select(k => new SyncKey(k)).Where(k => !target.Keys.Contains(k)).ToList();
                if (keys.Any(k => LiveOwner(item.Kind, k) is { } owner && owner != target)) throw new StaleItemException();
                var retires = new List<(SimRow, SimRow)>();
                AddAliases(target, new EntityKeys(keys), retires);
                foreach (var (live, tombstone) in retires) Retire(live, tombstone);
                target.Seq = NextSeq();
                history.Changes.Add(new SimHistoryChange(item.Kind, target.Primary, "bound", null, null, link.Id, keys));
                link.Bases.Remove((item.Kind, item.Key));
                var existing = link.Bases.GetValueOrDefault((item.Kind, target.Primary));
                link.Bases[(item.Kind, target.Primary)] = new DataSyncPeerBase(item.Kind, target.Primary, DataSyncBaseState.Normal,
                    null, existing?.Vv, existing?.ChildMap ?? new Dictionary<string, string>(),
                    b.Pending with { Reason = DataSyncPendingReason.Retry }, existing?.Record, []);
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
                Remerge(link, [(item.Kind, target.Primary)]);
                return;
            }
            case DataSyncInboxAction.Skip:
                UpsertBase(link, new DataSyncBaseUpdate(item.Kind, item.Key, DataSyncBaseState.Excluded,
                    DataSyncExclusionReason.Skipped, record, null, null, true));
                break;
            default:
            {
                // KeepBoth: created here under another name, so the revision adds this device's counter.
                var theirs = ContentOf(item.Kind, record)!;
                var content = kind.Renamed(theirs, newName ?? $"{kind.NameOf(theirs)} ({link.Peer.Name})");
                var row = Create(item.Kind, kind.Store(content, null));
                foreach (var key in record.Keys) row.Keys.Add(new SyncKey(key));
                row.Origin = record.Origin;
                row.CreatedBySync = true;
                row.OrderKey = record.OrderKey;
                row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Create, DataSyncVersionVector.Empty, record.Vv,
                    false, false, Actor, NextCounter);
                row.SharedHash = DataSyncPublication.Of(kind.Codec, row.Content!, row.Overlay, false, row.OrderKey, null).SharedHash;
                row.LastEditor = Editor;
                row.Seq = NextSeq();
                if (kind.HasOrder) PlaceNew(row);
                history.Changes.Add(new SimHistoryChange(item.Kind, row.Primary, "created", null, row.Content, link.Id, []));
                UpsertBase(link, new DataSyncBaseUpdate(item.Kind, item.Key, DataSyncBaseState.Normal, null, record,
                    kind.Codec.ChildrenOf(theirs).ToDictionary(c => c.Id, c => c.Id), null, true));
                break;
            }
        }

        Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    /// <summary>
    /// Rows I and M (§9.2): KeepWithEntity moves the record's keys that belong to the other candidates to the
    /// chosen one (re-keying a candidate whose primary moves); KeepRecordLinked drops the other records' keys from
    /// the entity into an <c>Excluded(DroppedIdentity)</c> base; Detach stops syncing the entity (row M) or, for
    /// row I, the record.
    /// </summary>
    private void ResolveIdentity(SimItem item, DataSyncInboxAction action, string? target)
    {
        var link = LinkById(item.LinkId)!;
        var rowM = item.Payload.Records is { Count: > 0 };
        if (action == DataSyncInboxAction.Detach)
        {
            if (rowM) Detach(RowOf(item.Kind, item.Key));
            else
            {
                var b = link.Bases[(item.Kind, item.Key)];
                UpsertBase(link, new DataSyncBaseUpdate(item.Kind, item.Key, DataSyncBaseState.Excluded,
                    DataSyncExclusionReason.DroppedIdentity, b.Pending!.Record, null, null, true));
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
            }

            return;
        }

        if (!rowM)
        {
            var b = link.Bases[(item.Kind, item.Key)];
            var record = b.Pending!.Record;
            var chosen = Target(item.Kind, target);
            foreach (var key in record.Keys.Select(k => new SyncKey(k)))
            {
                var owner = LiveOwner(item.Kind, key);
                if (owner is null || owner == chosen) continue;
                if (owner.Primary == key) Rekey(owner);
                owner.Keys.Remove(key);
                owner.Seq = NextSeq();
                chosen.Keys.Add(key);
            }

            chosen.Seq = NextSeq();
            Close(item, DataSyncInboxClosure.ResolvedHere, null);
            // "Then R merges with A" (§9.2): the rekey may have deleted the row R waited on, so R waits on A's row.
            link.Bases.Remove((item.Kind, item.Key));
            var existing = link.Bases.GetValueOrDefault((item.Kind, chosen.Primary));
            link.Bases[(item.Kind, chosen.Primary)] = new DataSyncPeerBase(item.Kind, chosen.Primary,
                existing?.State ?? DataSyncBaseState.Normal, existing?.Exclusion, existing?.Vv,
                existing?.ChildMap ?? new Dictionary<string, string>(), b.Pending! with { Reason = DataSyncPendingReason.Retry },
                existing?.Record, existing?.ExclusionKeys ?? []);
            Remerge(link, [(item.Kind, chosen.Primary)]);
            return;
        }

        // Row M: keep record P (target = its primary), drop the others.
        var entity = RowOf(item.Kind, item.Key);
        var kept = target ?? item.Payload.Records![0].PrimaryKey;
        var remerge = new List<(string, SyncKey)>();
        foreach (var dropped in item.Payload.Records!.Where(r => r.PrimaryKey != kept))
        {
            var key = new SyncKey(dropped.PrimaryKey);
            var b = link.Bases.GetValueOrDefault((item.Kind, key));
            var record = b?.Pending?.Record;
            var droppedKeys = (record?.Keys ?? [dropped.PrimaryKey]).Select(k => new SyncKey(k)).ToList();
            if (droppedKeys.Contains(entity.Primary)) Rekey(entity);
            entity.Keys.RemoveAll(droppedKeys.Contains);
            UpsertBase(link, new DataSyncBaseUpdate(item.Kind, key, DataSyncBaseState.Excluded,
                DataSyncExclusionReason.DroppedIdentity, record, null, null, true));
        }

        entity.Seq = NextSeq();
        foreach (var b in link.Bases.Values.Where(b => b.Kind == item.Kind && b.Pending is not null &&
                                                        b.Pending.Record.Keys.Contains(kept)).ToList())
            remerge.Add((b.Kind, b.Key));
        Close(item, DataSyncInboxClosure.ResolvedHere, null);
        Remerge(link, remerge);
    }

    /// <summary>
    /// Rekey (§5.3): a fresh primary; the old one stays as an alias until the caller moves it. The entity's bases and
    /// pending records are deleted on every link, so its next merge on each runs without a base.
    /// </summary>
    private void Rekey(SimRow row)
    {
        _world.Count("rekey");
        var fresh = _world.NewKey();
        foreach (var link in Links.Values) link.Bases.Remove((row.Kind, row.Primary));
        row.Keys.Insert(0, fresh);
        row.Seq = NextSeq();
        foreach (var item in OpenItems.Where(i => i.Kind == row.Kind && row.Keys.Contains(i.Key)).ToList())
            Close(item, DataSyncInboxClosure.Superseded, null);
    }

    private void ResolveLostUpdate(SimItem item, DataSyncInboxAction action, SimHistoryEntry history)
    {
        var row = RowOf(item.Kind, item.Key);
        var kind = SimKinds.Of(row.Kind);
        if (action == DataSyncInboxAction.Reapply && row.LastApply is { } last)
        {
            // Only the undone changes are written again (§6.5).
            var before = row.Content!;
            row.Content = kind.Reapply(before, DataSyncLostUpdateGuard.UndoneChanges(kind.Codec, before, last.Changes));
            history.Changes.Add(new SimHistoryChange(row.Kind, row.Primary, "updated", before, row.Content, null, []));
        }

        row.PublishHeld = false;
        row.LastApply = null;
        if (action == DataSyncInboxAction.Reapply) Revise(row, DataSyncRevisionKind.Resolution, null);
        else Refresh();
        row.Seq = Math.Max(row.Seq, NextSeq());
        Close(item, DataSyncInboxClosure.ResolvedHere, null);
        foreach (var link in Links.Values)
        {
            var keys = link.Bases.Where(b => b.Value.Pending?.Reason == DataSyncPendingReason.PublishHeld).Select(b => b.Key).ToList();
            if (keys.Count > 0 && link.Paused is null && !link.Stopped) Remerge(link, keys);
        }
    }

    /// <summary>
    /// Detach (§9.2): stops publishing (a Seq bump), every open item of the entity closes, its pending records are
    /// cleared and <c>PublishHeld</c> is cleared.
    /// </summary>
    private void Detach(SimRow row)
    {
        row.State = DataSyncEntitySyncState.Detached;
        row.PublishHeld = false;
        row.Seq = NextSeq();
        foreach (var link in Links.Values)
        {
            if (link.Bases.GetValueOrDefault((row.Kind, row.Primary)) is { Pending: not null } b)
                link.Bases[(row.Kind, row.Primary)] = b with { Pending = null };
        }

        foreach (var open in OpenItems.Where(i => i.Kind == row.Kind && row.Keys.Contains(i.Key)).ToList())
            Close(open, DataSyncInboxClosure.ResolvedHere, null);
    }

    /// <summary>A revision of a row's current content through <see cref="DataSyncRevisionRules"/>.</summary>
    private void Revise(SimRow row, DataSyncRevisionKind kind, DataSyncVersionVector? remote)
    {
        var codec = SimKinds.Of(row.Kind).Codec;
        row.Vv = DataSyncRevisionRules.Next(kind, row.Vv, remote, false, false, Actor, NextCounter);
        row.LocalHash = ContentHash.Of(codec.Write(row.Content!));
        row.SharedHash = DataSyncPublication.Of(codec, row.Content!, row.Overlay, false, row.OrderKey, row.Unknown).SharedHash;
        row.LastEditor = Editor;
        row.Seq = NextSeq();
    }

    /// <summary>A definition created or revived by a decision takes its place in the shared order (§3.7).</summary>
    private void PlaceNew(SimRow row)
    {
        var order = OrderOf(row.Kind);
        if (!order.Contains(row.LocalKey)) order.Add(row.LocalKey);
        var synced = Live(row.Kind).Where(r => r.HasSideRow && r.State == DataSyncEntitySyncState.Synced && !r.PublishHeld)
            .Select(r => new DataSyncOrderEntry(r.LocalKey, r.OrderKey, DataSyncOrderPlanner.TieKeyOf(new EntityKeys(r.Keys)))).ToList();
        var placed = DataSyncOrderPlanner.Place(order, synced);
        order.Clear();
        order.AddRange(placed);
    }
}
