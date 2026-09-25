using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Ordering;

namespace Bakabase.Modules.DataSync.Tests.Simulation;

/// <summary>A copy of an install taken at some point: its database, and what lives beside it.</summary>
internal sealed record SimBackup(SimDb Db, SimWatermark Watermark, DateTime At);

// Undo (§8.11), restores and the restore choices (§5.6, §9.5), link stop/reset/resume (§8.1, §8.7), entity sync
// state (§3.6) and retention (§4.6).
internal sealed partial class SimNode
{
    // ---- undo (§8.11) ------------------------------------------------------------------------------

    /// <summary>
    /// Undoes one history entry with the continuous rules: an undone create leaves an unserved
    /// <c>UndoneCreate</c> tombstone and <c>Excluded(Undone)</c> bases; an undone bind excludes the link's base; an
    /// undone update restores the changed paths as a new local revision; an undone deletion re-creates the
    /// definition with its child ids and revives its key. Entities that changed since are refused one by one.
    /// </summary>
    /// <returns>The refusals, one per refused entity.</returns>
    public IReadOnlyList<string> Undo(SimHistoryEntry entry)
    {
        if (entry.Kind == DataSyncHistoryKind.Undo || entry.Undone) return ["notUndoable"];
        if (!Verified) return ["unverified"];
        CheckActor();
        Refresh();
        var refusals = new List<string>();
        var undo = new SimHistoryEntry { Id = ++Db.NextHistoryId, Kind = DataSyncHistoryKind.Undo, At = Now };
        foreach (var change in Enumerable.Reverse(entry.Changes))
        {
            var kind = SimKinds.Of(change.Kind);
            var row = Rows.FirstOrDefault(r => r.Kind == change.Kind && r.Keys.Contains(change.Primary));
            if (row is null)
            {
                refusals.Add("gone");
                continue;
            }

            switch (change.Action)
            {
                case "created":
                    if (!row.IsLive) break;
                    if (Db.Values.GetValueOrDefault((row.Kind, row.LocalKey)) > 0)
                    {
                        refusals.Add("inUse");
                        break;
                    }

                    // Never served, keys kept: the peers keep theirs, and nothing is proposed for deletion.
                    row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Undo, row.Vv, null, false, false, Actor, NextCounter);
                    row.LastEditor = Editor;
                    Tombstone(row, DataSyncTombstoneKind.UndoneCreate);
                    foreach (var link in Links.Values)
                    {
                        link.IncludedUndone.Remove((row.Kind, row.Primary));
                        // §8.11: every link's base becomes Excluded(Undone) with the entity's keys, so a record under
                        // any of them — an alias a Link added included — is ignored until the person includes it.
                        var b = link.Bases.GetValueOrDefault((row.Kind, row.Primary));
                        UpsertBase(link, new DataSyncBaseUpdate(row.Kind, row.Primary, DataSyncBaseState.Excluded,
                            DataSyncExclusionReason.Undone, b?.Record ?? b?.Pending?.Record, null, null, true));
                        var excluded = link.Bases[(row.Kind, row.Primary)];
                        link.Bases[(row.Kind, row.Primary)] = excluded with
                        {
                            ExclusionKeys = excluded.ExclusionKeys.Concat(row.Keys.Select(k => k.Value))
                                .Distinct(StringComparer.Ordinal).ToList(),
                        };
                    }

                    break;
                case "bound":
                    if (LinkById(change.LinkId) is { } bindLink)
                    {
                        var b = bindLink.Bases.GetValueOrDefault((row.Kind, row.Primary));
                        UpsertBase(bindLink, new DataSyncBaseUpdate(row.Kind, row.Primary, DataSyncBaseState.Excluded,
                            DataSyncExclusionReason.Undone, b?.Record, null, null, true));
                        var excluded = bindLink.Bases[(row.Kind, row.Primary)];
                        bindLink.Bases[(row.Kind, row.Primary)] = excluded with
                        {
                            ExclusionKeys = excluded.ExclusionKeys.Concat(row.Keys.Select(k => k.Value))
                                .Concat(change.AliasesAdded.Select(k => k.Value)).Distinct(StringComparer.Ordinal).ToList(),
                        };
                    }

                    break;
                case "updated":
                {
                    if (!row.IsLive || change.Before is null || change.After is null) break;
                    var applied = DataSyncEntityChangeList.Between(kind.Codec, change.Before, change.After);
                    var usage = Db.Usage.GetValueOrDefault((row.Kind, row.LocalKey));
                    if (applied.Added.Any(a => usage?.GetValueOrDefault(a.ChildId) > 0))
                    {
                        refusals.Add("addedOptionsInUse");
                        break;
                    }

                    var reverted = kind.Revert(row.Content!, applied, out var refusal);
                    if (reverted is null)
                    {
                        refusals.Add(refusal ?? "changedSinceImport");
                        break;
                    }

                    row.Content = reverted;
                    row.PublishHeld = false;
                    row.LastApply = null;
                    Revise(row, DataSyncRevisionKind.Undo, null);
                    break;
                }
                case "deleted":
                    if (!row.Deleted || change.Before is null) break;
                    // Re-created with the same child ids; the tombstone's key revives (Undo ≥ the tombstone).
                    row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.Undo, row.Vv, null, false, false, Actor, NextCounter);
                    row.Content = kind.Store(change.Before, null);
                    row.Deleted = false;
                    row.DeletedAt = null;
                    row.State = DataSyncEntitySyncState.Synced;
                    row.LocalKey = NewLocalKey();
                    row.LocalHash = ContentHash.Of(kind.Codec.Write(row.Content));
                    row.SharedHash = DataSyncPublication.Of(kind.Codec, row.Content, row.Overlay, row.ChildrenLocal, row.OrderKey,
                        row.Unknown).SharedHash;
                    row.LastEditor = Editor;
                    row.Seq = NextSeq();
                    if (kind.HasOrder) PlaceNew(row);
                    break;
            }

            undo.Changes.Add(change with { Action = "undo:" + change.Action });
            _world.Count("undo:" + change.Action);
        }

        entry.Undone = true;
        Db.History.Add(undo);
        CloseStaleStateItems();
        CloseDominated();
        WriteWatermark();
        _world.Log($"{Name}: undo #{entry.Id} ({entry.Kind}, {entry.Changes.Count} changes) refusals [{string.Join(",", refusals)}]");
        return refusals;
    }

    // ---- restores (§5.6, §9.5) ---------------------------------------------------------------------

    public SimBackup Backup() => new(Db.Clone(), Watermark, Now);

    /// <summary>The database alone goes back to <paramref name="backup"/>; <c>actor.json</c> stays. Then a start.</summary>
    public void RestoreDatabase(SimBackup backup)
    {
        Db = backup.Db.Clone();
        _world.Log($"{Name}: database restored to {backup.At:MM-dd HH:mm}");
        Restart();
    }

    /// <summary>The whole data directory goes back (database and <c>actor.json</c>). Then a start.</summary>
    public void RestoreDirectory(SimBackup backup)
    {
        Db = backup.Db.Clone();
        Watermark = backup.Watermark;
        _world.Log($"{Name}: data directory restored to {backup.At:MM-dd HH:mm}");
        Restart(directoryRestored: true);
    }

    /// <summary>
    /// The restore choice (§9.5). ThisDeviceWins: every live synced entity and every served tombstone (of the
    /// suspected link's bases only, when suspected) gets <c>RestoreWins</c>; OthersWin raises nothing and makes the
    /// first link's next cycle take the peer's version of concurrent entities. Either way cursors reset, bases are
    /// kept and the paused links resume.
    /// </summary>
    public void ChooseRestore(DataSyncRestoreChoice choice)
    {
        var local = Db.Local;
        if (local.RestoreReason is null) return;
        CheckActor();
        Refresh();
        var scoped = local.RestoreReason == DataSyncPauseReason.LocalRestoreSuspected ? LinkById(local.RestoreLinkId) : null;
        var links = scoped is not null ? [scoped] : Links.Values.Where(l => !l.Stopped).ToList();
        if (choice == DataSyncRestoreChoice.ThisDeviceWins)
        {
            foreach (var row in Rows.Where(r => r.HasSideRow && ((r.IsLive && r.State == DataSyncEntitySyncState.Synced) ||
                                                                 (r.Deleted && r.Served))).ToList())
            {
                var vectors = links.Select(l => l.Bases.GetValueOrDefault((row.Kind, row.Primary)))
                    .Where(b => b is not null).SelectMany(b => new[] { b!.Vv, b.Pending?.Record.Vv })
                    .Concat(Items.Where(i => i.Kind == row.Kind && row.Keys.Contains(i.Key)).Select(i => i.RecordVv))
                    .Where(v => v is not null).Select(v => v!).ToList();
                if (scoped is not null && !scoped.Bases.ContainsKey((row.Kind, row.Primary))) continue;
                var remote = DataSyncRevisionRules.RestoreWinsRemote(vectors, local.RetiredActors);
                row.Vv = DataSyncRevisionRules.Next(DataSyncRevisionKind.RestoreWins, row.Vv, remote, false, false, Actor, NextCounter);
                row.LastEditor = Editor;
                row.Seq = NextSeq();
            }
        }
        else
        {
            var first = links.OrderByDescending(l => l.LastSuccess).ThenBy(l => l.Id).FirstOrDefault();
            if (first is not null) first.OthersWinNext = true;
        }

        foreach (var link in links)
        {
            link.Cursors.Clear();
            link.LastFullReconciliation = DateTime.MinValue;
            if (link.Paused is DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected)
            {
                link.Paused = null;
                link.PausedDetail = null;
            }
        }

        local.RestoreReason = null;
        local.RestoreLinkId = null;
        local.Evidence.Clear();
        Db.History.Add(new SimHistoryEntry { Id = ++Db.NextHistoryId, Kind = DataSyncHistoryKind.Restore, At = Now });
        WriteWatermark();
        _world.Log($"{Name}: restore choice {choice}{(scoped is null ? "" : $" (link {scoped.Id})")}");
        _world.Count("restoreChoice:" + choice);
    }

    /// <summary>
    /// "Reset this device's identity", the guidance of a duplicated actor that is this device's own (§5.6, B7): a
    /// deliberate reset rotates the actor with no pause. (The federation reset also renews the node id; the
    /// simulator keeps it, which is all data sync sees of it: a new actor.)
    /// </summary>
    public void ResetIdentity()
    {
        _world.Count("resetIdentity");
        Rotate(null);
    }

    // ---- links (§8.1, §8.7) ------------------------------------------------------------------------

    /// <summary>Mode := Off: bases and pending records kept; items close LinkStopped; its holds become local-only.</summary>
    public void StopLink(SimLink link)
    {
        if (link.Stopped) return;
        link.LastMode = link.Mode;
        link.Mode = DataSyncLinkMode.Off;
        EndLinkHolds(link, DataSyncInboxClosure.LinkStopped);
        _world.Log($"{Name}: stopped {link}");
    }

    /// <summary>Stopped → Active: no new review; every pending record is re-merged with the next pull.</summary>
    public void StartLink(SimLink link)
    {
        if (!link.Stopped) return;
        link.Mode = link.LastMode;
        link.RemergeAllPending = true;
        _world.Log($"{Name}: started {link}");
    }

    /// <summary>
    /// Reset: the link row, bases and pending records go; items close LinkRemoved; its holds become local-only;
    /// definitions are untouched. A new link to the same peer (same mode) starts a first contact.
    /// </summary>
    public SimLink ResetLink(SimLink link)
    {
        EndLinkHolds(link, DataSyncInboxClosure.LinkRemoved);
        Links.Remove(link.Peer.NodeId);
        _world.Log($"{Name}: reset {link}");
        return Follow(link.Peer, link.Stopped ? link.LastMode : link.Mode);
    }

    private void EndLinkHolds(SimLink link, DataSyncInboxClosure closure)
    {
        foreach (var item in Items.Where(i => i.Open && i.LinkId == link.Id).ToList()) Close(item, closure, null);
        foreach (var row in Rows.Where(r => r.IsLive && r.Overlay.HeldChildren.Any(h => h.LinkId == link.Id)))
        {
            var mine = row.Overlay.HeldChildren.Where(h => h.LinkId == link.Id).Select(h => h.ChildId).ToList();
            var others = row.Overlay.HeldChildren.Where(h => h.LinkId != link.Id).ToList();
            var orphaned = mine.Where(c => others.All(o => o.ChildId != c) && !row.Overlay.LocalOnlyChildren.Contains(c));
            row.Overlay = new DataSyncOverlay([.. row.Overlay.LocalOnlyChildren, .. orphaned], others);
        }
    }

    /// <summary>
    /// The resume actions of §8.7: B1b resets the cursors (a full reconciliation), B2/B3 resume with a once flag,
    /// the others resume from the cursor. Restore pauses resume only through <see cref="ChooseRestore"/>.
    /// </summary>
    public void Resume(SimLink link, bool reviewDeletions = false)
    {
        switch (link.Paused)
        {
            case null or DataSyncPauseReason.LocalRestoreDetected or DataSyncPauseReason.LocalRestoreSuspected:
                return;
            case DataSyncPauseReason.PeerReset when link.PausedDetail?.StartsWith("restored", StringComparison.Ordinal) == true:
                link.Cursors.Clear();
                link.LastFullReconciliation = DateTime.MinValue;
                break;
            case DataSyncPauseReason.MassDeletion or DataSyncPauseReason.KindEmptied:
                link.OnceFlags = reviewDeletions
                    ? link.OnceFlags with { DeletionsAsItems = true }
                    : link.OnceFlags with { SkipDeletionBreaker = true };
                break;
            case DataSyncPauseReason.TooManyDecisions:
                if (!DataSyncBreakers.MayResumeTooManyDecisions(Items.Count(i => i.Open && i.LinkId == link.Id), Limits)) return;
                break;
        }

        _world.Log($"{Name}: resumed {link}");
        link.Paused = null;
        link.PausedDetail = null;
    }

    /// <summary>
    /// [Include] on an excluded base (§5.4, §8.11): the exclusion goes; the next full reconciliation re-merges it. An
    /// undone exclusion leaves the row behind, unbound (or agreed on the record it kept): that row is what lets row T0
    /// revive an undone create on this link and no other.
    /// </summary>
    public void Include(SimLink link, (string Kind, SyncKey Key) baseKey)
    {
        if (!link.Bases.TryGetValue(baseKey, out var b) || b.State != DataSyncBaseState.Excluded) return;
        if (b.Exclusion == DataSyncExclusionReason.Undone)
        {
            link.Bases[baseKey] = b with
            {
                State = b.Record is null ? DataSyncBaseState.Unbound : DataSyncBaseState.Normal, Exclusion = null,
                ExclusionKeys = [],
            };
            link.IncludedUndone.Add(baseKey);
        }
        else
        {
            link.Bases.Remove(baseKey);
        }

        link.LastFullReconciliation = DateTime.MinValue;
        _world.Log($"{Name}: included {baseKey.Kind}/{baseKey.Key.Value[..6]} on {link}");
    }

    // ---- entity sync state (§3.6) ------------------------------------------------------------------

    /// <summary>
    /// "Keep on this device only", "Stop syncing this one", or rejoin (Synced). Leaving closes the entity's items and
    /// clears its pending records and <c>PublishHeld</c>; rejoining drops the entity's bases and exclusions on every
    /// link, so the next pulls merge it through its keys with no base (§3.6). Either way a Seq bump.
    /// </summary>
    public void SetEntitySync(SimRow row, DataSyncEntitySyncState state)
    {
        if (!row.IsLive || row.State == state) return;
        CheckActor();
        Refresh();
        if (state == DataSyncEntitySyncState.Synced)
        {
            row.State = state;
            foreach (var link in Links.Values)
            {
                foreach (var key in link.Bases.Keys.Where(k => k.Kind == row.Kind &&
                                                               (row.Keys.Contains(k.Key) ||
                                                                link.Bases[k].ExclusionKeys.Any(e => row.Keys.Contains(new SyncKey(e)))))
                             .ToList())
                    link.Bases.Remove(key);
            }

            row.Seq = NextSeq();
            Refresh();
        }
        else
        {
            Detach(row);
            row.State = state;
        }

        _world.Log($"{Name}: {row.Name} → {state}");
    }

    // ---- retention (§4.6) --------------------------------------------------------------------------

    /// <summary>Tombstones served for 180 days become unserved (never deleted) and raise their kind's floor.</summary>
    public void RunRetention()
    {
        foreach (var row in Rows.Where(r => r.Deleted && r.Served && r.DeletedAt is { } at && Now - at >= TombstoneRetention))
        {
            row.Served = false;
            var floors = Db.Local.TombstoneFloors;
            floors[row.Kind] = Math.Max(floors.GetValueOrDefault(row.Kind), row.Seq);
        }
    }
}
