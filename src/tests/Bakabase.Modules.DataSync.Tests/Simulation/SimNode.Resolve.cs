using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Tests.TestKinds;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

// The §9.2 actions the scenarios use, over the test kind. Vectors come from DataSyncRevisionRules only.
internal sealed partial class SimNode
{
    public SimItem Item(DataSyncInboxItemType type) => OpenItems.Single(i => i.Type == type);

    /// <summary>Resolves one item (and, for conflicts, every open conflict item of its entity, §9.2).</summary>
    public void Resolve(SimItem item, DataSyncInboxAction action, string? custom = null, string? targetLocalKey = null)
    {
        if (!item.Open) throw new InvalidOperationException("The item is closed.");
        if (!DataSyncInboxRules.AllowedActions(item.Type, item.Subject, item.Payload, IsTwoWay(item.LinkId)).Contains(action))
            throw new InvalidOperationException($"{action} is not allowed for {item.Type}.");
        Refresh();
        switch (item.Type)
        {
            case DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict:
                ResolveConflicts(item, action, custom);
                break;
            case DataSyncInboxItemType.DeletedThere:
                ResolveDeletedThere(item, action);
                break;
            case DataSyncInboxItemType.DeletedHereEditedThere:
                ResolveDeletedHere(item, action);
                break;
            case DataSyncInboxItemType.ChildDeletedInUse:
                ResolveChildInUse(item, action);
                break;
            case DataSyncInboxItemType.LinkSuggestion:
                ResolveSuggestion(item, action, custom, targetLocalKey);
                break;
            case DataSyncInboxItemType.MassChildDeletion:
                var link = LinkById(item.LinkId);
                var b = link.Bases[RowOf(item.Key).Primary];
                var mode = action switch
                {
                    DataSyncInboxAction.ApplyAll => DataSyncChildDeletionMode.Apply,
                    DataSyncInboxAction.ReviewEach => DataSyncChildDeletionMode.ReviewEach,
                    _ => DataSyncChildDeletionMode.Restore,
                };
                link.Bases[b.Key] = b with { Pending = b.Pending! with { Flags = b.Pending.Flags with { ChildDeletions = mode } } };
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
                Remerge(link, [b.Key]);
                break;
            case DataSyncInboxItemType.LargeChange:
                var waiting = LinkById(item.LinkId);
                waiting.OnceFlags = waiting.OnceFlags with { SkipLargeChange = true };
                Remerge(waiting, waiting.Bases.Values.Where(x => x.Pending?.Reason == DataSyncPendingReason.LargeChange)
                    .Select(x => x.Key).ToList());
                break;
            case DataSyncInboxItemType.SuspectedLostUpdate:
                ResolveLostUpdate(item, action);
                break;
            case DataSyncInboxItemType.TypeChange when action == DataSyncInboxAction.Detach:
                RowOf(item.Key).State = DataSyncEntitySyncState.Detached;
                foreach (var open in OpenItems.Where(i => i.Key == item.Key).ToList())
                    Close(open, DataSyncInboxClosure.ResolvedHere, null);
                break;
            default:
                throw new NotSupportedException($"{item.Type}/{action} is not emulated.");
        }

        CloseStaleStateItems();
        CloseDominated();
    }

    private bool IsTwoWay(int? linkId) =>
        linkId is { } id && LinkById(id) is var link &&
        (link.Mode == DataSyncLinkMode.TwoWay ||
         (link.Mode == DataSyncLinkMode.Follow && link.Peer.Links.GetValueOrDefault(NodeId)?.Mode == DataSyncLinkMode.Follow));

    private SimLink LinkById(int? id) => Links.Values.Single(l => l.Id == id);

    private (SimLink Link, DataSyncPeerBase Base, DataSyncWireRecord Record) PendingOf(SimItem item, SimRow row)
    {
        var link = LinkById(item.LinkId);
        var b = link.Bases[row.Primary];
        return (link, b, b.Pending!.Record);
    }

    private static TestItemContent ContentOf(DataSyncWireRecord record) =>
        (TestItemContent)Codec.Read(record.Content!, Limits).Content!;

    /// <summary>
    /// KeepLocal / UseRemote / UseCustom: every open conflict item of the entity, with every device, in one batch;
    /// the revision is <c>Max(L, R of every item) + self</c> and every item closes (§9.2).
    /// </summary>
    private void ResolveConflicts(SimItem chosen, DataSyncInboxAction action, string? custom)
    {
        var row = RowOf(chosen.Key);
        var items = OpenItems.Where(i => i.Key == chosen.Key &&
                                         i.Type is DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict)
            .ToList();
        var content = row.Content!;
        var remote = DataSyncVersionVector.Empty;
        var agreed = new List<(SimLink, DataSyncPeerBase, DataSyncWireRecord)>();
        foreach (var item in items)
        {
            var (link, b, record) = PendingOf(item, row);
            var theirs = ContentOf(record);
            remote = DataSyncVersionVector.Max(remote, record.Vv);
            if (!agreed.Any(a => a.Item1 == link)) agreed.Add((link, b, record));
            if (item.Subject == "name")
            {
                content = action switch
                {
                    DataSyncInboxAction.UseRemote => content.With(name: theirs.Name),
                    DataSyncInboxAction.UseCustom => content.With(name: custom),
                    _ => content,
                };
            }
            else if (item.Subject.StartsWith(TestItemCodec.ChildPathPrefix, StringComparison.Ordinal))
            {
                var peerId = item.Subject[TestItemCodec.ChildPathPrefix.Length..];
                var localId = b.ChildMap.GetValueOrDefault(peerId) ?? peerId;
                var label = action switch
                {
                    DataSyncInboxAction.UseRemote => theirs.Children.Single(c => c.Id == peerId).Label,
                    DataSyncInboxAction.UseCustom => custom!,
                    _ => null,
                };
                if (label is not null)
                    content = content.With(children: content.Children.Select(c => c.Id == localId ? c with { Label = label } : c));
            }
        }

        row.Content = content;
        Revise(row, DataSyncRevisionKind.Resolution, remote);
        foreach (var (link, b, record) in agreed)
            UpsertBase(link, new DataSyncBaseUpdate(Kind, b.Key, DataSyncBaseState.Normal, null, record, null, null, true));
        foreach (var item in items) Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveDeletedThere(SimItem item, DataSyncInboxAction action)
    {
        var row = RowOf(item.Key);
        var (link, b, record) = PendingOf(item, row);
        switch (action)
        {
            case DataSyncInboxAction.DeleteHere:
                row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.AcceptRemoteDelete, row.Vv, record.Vv, false, false,
                    Actor, NextCounter);
                row.Deleted = true;
                row.Served = true;
                row.Content = null;
                row.Seq = ++LastSeq;
                UpsertBase(link, new DataSyncBaseUpdate(Kind, b.Key, DataSyncBaseState.Normal, null, record, null, null, true));
                break;
            case DataSyncInboxAction.KeepHereOnly:
                row.State = DataSyncEntitySyncState.Detached;
                row.Seq = ++LastSeq;
                UpsertBase(link, new DataSyncBaseUpdate(Kind, b.Key, b.State, null, null, null, null, true));
                break;
            default:
                // RestoreEverywhere: a revision that dominates the tombstone; the peer gets DeletedHereEditedThere.
                Revise(row, DataSyncRevisionKind.Resolution, record.Vv);
                UpsertBase(link, new DataSyncBaseUpdate(Kind, b.Key, b.State, null, null, null, null, true));
                break;
        }

        foreach (var open in OpenItems.Where(i => i.Key == item.Key).ToList()) Close(open, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveDeletedHere(SimItem item, DataSyncInboxAction action)
    {
        var row = RowOf(item.Key);
        var (link, b, record) = PendingOf(item, row);
        if (action == DataSyncInboxAction.RestoreHere)
        {
            // The definition returns (empty of values), reviving the tombstone's key: Max(T, R) + self.
            row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Resolution, row.Vv, record.Vv, false, false, Actor,
                NextCounter);
            row.Content = ContentOf(record);
            row.Deleted = false;
            row.LocalKey = (++_nextLocalKey).ToString(System.Globalization.CultureInfo.InvariantCulture);
            row.LocalHash = ContentHash.Of(Codec.Write(row.Content));
            row.SharedHash = DataSyncPublication.Of(Codec, row.Content, row.Overlay, false, row.OrderKey, row.Unknown).SharedHash;
            row.LastEditor = Editor;
            row.Seq = ++LastSeq;
            UpsertBase(link, new DataSyncBaseUpdate(Kind, b.Key, DataSyncBaseState.Normal, null, record, null, null, true));
        }
        else
        {
            row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.KeepDeleted, row.Vv, record.Vv, false, false, Actor,
                NextCounter);
            row.LastEditor = Editor;
            row.Seq = ++LastSeq;
            UpsertBase(link, new DataSyncBaseUpdate(Kind, b.Key, b.State, null, null, null, null, true));
        }

        Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveChildInUse(SimItem item, DataSyncInboxAction action)
    {
        var row = RowOf(item.Key);
        var link = LinkById(item.LinkId);
        var peerId = item.Subject[(item.Subject.IndexOf(':') + 1)..];
        var local = link.Bases.GetValueOrDefault(row.Primary)?.ChildMap.GetValueOrDefault(peerId) ?? peerId;
        var hold = new DataSyncHeldChild(local, link.Id);
        var held = row.Overlay.HeldChildren.Where(h => h != hold).ToList();
        switch (action)
        {
            case DataSyncInboxAction.DeleteHere:
                // Never published, so no revision: the values keep the id and show nothing.
                row.Content = row.Content!.With(children: row.Content.Children.Where(c => c.Id != local));
                row.Overlay = row.Overlay with { HeldChildren = held };
                row.LocalHash = ContentHash.Of(Codec.Write(row.Content));
                break;
            case DataSyncInboxAction.KeepHereOnly:
                row.Overlay = new DataSyncOverlay([.. row.Overlay.LocalOnlyChildren, local], held);
                break;
            default:
                // RestoreEverywhere: released and published again; the peer receives it as an addition.
                row.Overlay = row.Overlay with { HeldChildren = held };
                Revise(row, DataSyncRevisionKind.Resolution, link.Bases.GetValueOrDefault(row.Primary)?.Vv);
                break;
        }

        Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveSuggestion(SimItem item, DataSyncInboxAction action, string? newName, string? targetLocalKey)
    {
        var link = LinkById(item.LinkId);
        var b = link.Bases[item.Key];
        var record = b.Pending!.Record;
        switch (action)
        {
            case DataSyncInboxAction.Link:
                // BindOnly (R's keys become the candidate's aliases), then R merges with it as K4/K5/K6 without a base.
                var target = Rows.Single(r => !r.Deleted && r.LocalKey == targetLocalKey);
                foreach (var key in record.Keys.Select(k => new SyncKey(k)).Where(k => !target.Keys.Contains(k)))
                    target.Keys.Add(key);
                target.Seq = ++LastSeq;
                link.Bases.Remove(item.Key);
                link.Bases[target.Primary] = new DataSyncPeerBase(Kind, target.Primary, DataSyncBaseState.Normal, null, null,
                    new Dictionary<string, string>(), b.Pending with { Reason = DataSyncPendingReason.Retry }, null, []);
                Close(item, DataSyncInboxClosure.ResolvedHere, null);
                Remerge(link, [target.Primary]);
                return;
            case DataSyncInboxAction.Skip:
                UpsertBase(link, new DataSyncBaseUpdate(Kind, item.Key, DataSyncBaseState.Excluded,
                    DataSyncExclusionReason.Skipped, record, null, null, true));
                break;
            default:
                // KeepBoth: created here under another name, so the revision adds this device's counter.
                var content = ContentOf(record).With(name: newName ?? $"{ContentOf(record).Name} ({link.Peer.Name})");
                var row = Create(content);
                foreach (var key in record.Keys) row.Keys.Add(new SyncKey(key));
                row.Origin = record.Origin;
                row.CreatedBySync = true;
                row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Create, DataSyncVersionVector.Empty, record.Vv,
                    false, false, Actor, NextCounter);
                row.SharedHash = DataSyncPublication.Of(Codec, content, row.Overlay, false, null, null).SharedHash;
                row.LastEditor = Editor;
                row.Seq = ++LastSeq;
                UpsertBase(link, new DataSyncBaseUpdate(Kind, item.Key, DataSyncBaseState.Normal, null, record,
                    record.Content is null ? null : ContentOf(record).Children.ToDictionary(c => c.Id, c => c.Id), null, true));
                break;
        }

        Close(item, DataSyncInboxClosure.ResolvedHere, null);
    }

    private void ResolveLostUpdate(SimItem item, DataSyncInboxAction action)
    {
        var row = RowOf(item.Key);
        if (action == DataSyncInboxAction.Reapply)
        {
            // Only the undone changes are written again (§6.5): here, the scalars and child labels of the last apply.
            var undone = DataSyncLostUpdateGuard.UndoneChanges(Codec, row.Content!, row.LastApply!.Value.Changes);
            var content = row.Content!;
            foreach (var scalar in undone.Scalars.Where(s => s.Path == "name"))
                content = content.With(name: (string)scalar.After!);
            foreach (var rename in undone.Renamed)
                content = content.With(children: content.Children.Select(c => c.Id == rename.ChildId ? c with { Label = rename.After.Text! } : c));
            foreach (var added in undone.Added)
                content = content.With(children: content.Children.Append(new TestChild(added.ChildId, added.Display.Text!)));
            row.Content = content;
        }

        row.PublishHeld = false;
        row.LastApply = null;
        if (action == DataSyncInboxAction.Reapply) Revise(row, DataSyncRevisionKind.Resolution, null);
        else Refresh();
        Close(item, DataSyncInboxClosure.ResolvedHere, null);
        foreach (var link in Links.Values)
        {
            var keys = link.Bases.Values.Where(b => b.Pending?.Reason == DataSyncPendingReason.PublishHeld).Select(b => b.Key).ToList();
            if (keys.Count > 0) Remerge(link, keys);
        }
    }

    /// <summary>A revision of a row's current content through <see cref="DataSyncRevisionRules"/>.</summary>
    private void Revise(SimRow row, DataSyncRevisionKind kind, DataSyncVersionVector? remote)
    {
        row.Vv = DataSyncRevisionRules.Next(kind, row.Vv, remote, false, false, Actor, NextCounter);
        row.LocalHash = ContentHash.Of(Codec.Write(row.Content!));
        row.SharedHash = DataSyncPublication.Of(Codec, row.Content!, row.Overlay, false, row.OrderKey, row.Unknown).SharedHash;
        row.LastEditor = Editor;
        row.Seq = ++LastSeq;
    }
}
