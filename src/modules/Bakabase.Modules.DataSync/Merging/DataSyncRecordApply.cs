using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pure half of <c>RecordApply</c> (§8.10.2, §6.4): what an applied revision gives its row. Hashes and the
/// "result equals" flags come from the entity <b>re-read</b> after the write, never from the payload, so the next
/// Refresh finds nothing to do and a normalization the codec does not model costs at most one extra hop.
/// </summary>
public static class DataSyncRecordApply
{
    /// <summary>
    /// The row a revision leaves (§2.8 through <see cref="DataSyncRevisionRules"/>):
    /// <list type="bullet">
    /// <item><c>resultEqualsRemote</c> / <c>resultEqualsLocal</c> compare the re-read entity's comparison form with the
    /// peer record's and with the local form before the apply;</item>
    /// <item>the last editor is the peer's (<paramref name="remoteEditor"/>) when the new vector is exactly the peer's
    /// (a create, a fast-forward or an accepted deletion that added no counter of this device's), else this device;</item>
    /// <item><see cref="DataSyncAppliedRevision.NormalizationChanged"/> is set when the merge meant to reach the peer's
    /// form and the re-read did not (the history line's <c>NormalizationChanged</c>).</item>
    /// </list>
    /// </summary>
    /// <param name="decision">The merger's decision for the entity.</param>
    /// <param name="localVv">The row's vector before the apply; <see cref="DataSyncVersionVector.Empty"/> for a create.</param>
    /// <param name="localSharedHashBefore">The row's comparison-form hash before the apply; null for a create.</param>
    /// <param name="reRead">The entity re-read after the write (<c>ReadLocal</c>); null for a deletion.</param>
    /// <param name="overlayAfter">The entity's overlay after the apply's hold changes.</param>
    /// <param name="remoteSharedHash">The peer record's comparison-form hash (<see cref="DataSyncPublication.SharedHashOfRecord"/>); null for a tombstone.</param>
    public static DataSyncAppliedRevision Revise(IDataSyncKindCodec codec, DataSyncRevisionDecision decision,
        DataSyncVersionVector localVv, string? localSharedHashBefore, object? reRead, DataSyncOverlay overlayAfter,
        string? remoteSharedHash, DataSyncEditorRef? remoteEditor, DataSyncEditorRef self, DataSyncActorId selfActor,
        Func<long> nextCounter)
    {
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(localVv);
        ArgumentNullException.ThrowIfNull(overlayAfter);
        ArgumentNullException.ThrowIfNull(self);

        string? localHash = null, sharedHash = null;
        DataSyncPublication? publication = null;
        if (reRead is not null)
        {
            localHash = ContentHash.Of(codec.Write(reRead));
            publication = DataSyncPublication.Of(codec, reRead, overlayAfter, decision.ChildrenLocal ?? false,
                decision.OrderKey, decision.Unknown);
            sharedHash = publication.SharedHash ?? DataSyncRefreshRules.HeldSharedHash(localHash);
        }

        var equalsRemote = sharedHash is not null && sharedHash == remoteSharedHash;
        var equalsLocal = sharedHash is not null && sharedHash == localSharedHashBefore;
        // A result that has seen both sides is never the peer's revision, whatever its form (SeenBoth).
        var vv = DataSyncRevisionRules.Next(decision.Revision, localVv, decision.RemoteVv, equalsRemote && !decision.SeenBoth,
            equalsLocal, selfActor, nextCounter, decision.TombstoneVv);
        var editor = remoteEditor is not null && decision.RemoteVv is { } remote && vv == remote ? remoteEditor : self;
        return new DataSyncAppliedRevision(vv, editor, localHash, sharedHash, publication,
            decision.ResultEqualsRemote && !equalsRemote && reRead is not null);
    }

    /// <summary>
    /// The shared order for the adapter's <c>ApplyOrderAsync</c> (§3.7): the assignment's local keys, with every
    /// entity the merge created mapped from its item id to its new local key. An entity whose create did not happen
    /// (changed during apply) is left out, so its slot is untouched.
    /// </summary>
    public static IReadOnlyList<string> ResolveOrder(DataSyncOrderAssignment assignment,
        IReadOnlyDictionary<string, string> createdLocalKeysByItemId)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(createdLocalKeysByItemId);
        var result = new List<string>(assignment.Synced.Count);
        foreach (var (localKey, _) in assignment.Synced)
        {
            if (DataSyncMergeItemIds.TryParse(localKey, out _, out _))
            {
                if (createdLocalKeysByItemId.TryGetValue(localKey, out var created)) result.Add(created);
                continue;
            }

            result.Add(localKey);
        }

        return result;
    }

    /// <summary>
    /// A merge result without the operations the adapter skipped as <c>ChangedDuringApply</c> (their expected hash
    /// no longer held, or the identity pre-flight refused a key): their bases are not advanced and their records
    /// become <c>Retry</c> pending records, merged again with the next pull after the cursor moved past them
    /// (§7.5.5, §8.10.2); their revisions, hold changes and order entries are dropped. Items stay as derived.
    /// </summary>
    /// <param name="bases">The link's bases as the merge read them (for the rows' states).</param>
    public static DataSyncMergeResult WithoutChangedDuringApply(DataSyncMergeResult result,
        IReadOnlyCollection<string> changedItemIds, IReadOnlyDictionary<(string Kind, SyncKey Key), DataSyncPeerBase> bases)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(changedItemIds);
        ArgumentNullException.ThrowIfNull(bases);
        if (changedItemIds.Count == 0) return result;

        var changed = new HashSet<(string, SyncKey)>();
        foreach (var itemId in changedItemIds)
        {
            if (DataSyncMergeItemIds.TryParse(itemId, out var kind, out var key)) changed.Add((kind, key));
        }

        var ops = result.Batches.SelectMany(b => b.Operations.Select(o => (b.Kind, Op: o)))
            .Where(x => changedItemIds.Contains(x.Op.ItemId)).ToList();
        var localKeys = ops.Select(x => (x.Kind, x.Op switch
        {
            UpdateEntityOperation u => u.LocalKey,
            BindOnlyOperation b => b.LocalKey,
            DeleteEntityOperation d => d.LocalKey,
            ChangeSubtypeOperation s => s.LocalKey,
            _ => null,
        })).Where(x => x.Item2 is not null).ToHashSet();

        var batches = result.Batches
            .Select(b => new ApplyBatch(b.Kind, b.Operations.Where(o => !changedItemIds.Contains(o.ItemId)).ToList()))
            .Where(b => b.Operations.Count > 0).ToList();
        var revisions = result.Revisions
            .Where(r => !(r.LocalKey is not null && localKeys.Contains((r.Kind, r.LocalKey))) &&
                        !(r.LocalKey is null && r.Keys.All.Any(k => changed.Contains((r.Kind, k)))))
            .ToList();
        var baseUpdates = result.BaseUpdates.Select(u =>
        {
            if (!changed.Contains((u.Kind, u.Key))) return u;
            var record = u.Record ?? u.Pending?.Record;
            if (record is null) return u;
            var existing = bases.GetValueOrDefault((u.Kind, u.Key));
            var retry = DataSyncPendingRecords.Create(record, DataSyncPendingReason.Retry,
                u.Pending?.EvaluatedAtLocalSeq ?? 0, u.Pending?.Flags ?? DataSyncMergeFlags.None);
            return new DataSyncBaseUpdate(u.Kind, u.Key, existing?.State ?? DataSyncBaseState.Unbound,
                existing?.Exclusion, null, null, retry, false);
        }).ToList();
        var overlays = result.OverlayChanges.Where(o => !localKeys.Contains((o.Kind, o.LocalKey))).ToList();
        var order = result.Order.Select(a => new DataSyncOrderAssignment(a.Kind,
                a.Synced.Where(e => !localKeys.Contains((a.Kind, e.LocalKey)) && !changedItemIds.Contains(e.LocalKey))
                    .ToList()))
            .ToList();

        return result with
        {
            Batches = batches, Revisions = revisions, BaseUpdates = baseUpdates, OverlayChanges = overlays, Order = order,
        };
    }
}

/// <summary>What an applied revision gives its row (<see cref="DataSyncRecordApply.Revise"/>).</summary>
/// <param name="LocalHash">From the re-read content; null for a deletion.</param>
/// <param name="SharedHash">From the re-read content's publication; null for a deletion.</param>
/// <param name="Publication">What the row publishes now; null for a deletion.</param>
/// <param name="NormalizationChanged">The re-read differs from the peer's form although the merge meant to reach it.</param>
public sealed record DataSyncAppliedRevision(DataSyncVersionVector Vv, DataSyncEditorRef LastEditor, string? LocalHash,
    string? SharedHash, DataSyncPublication? Publication, bool NormalizationChanged);
