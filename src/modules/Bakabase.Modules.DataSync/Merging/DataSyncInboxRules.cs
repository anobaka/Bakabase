using Bakabase.Modules.DataSync.Identity;

namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pure rules of the inbox: the actions an item allows (§9.1) and when items close (§9.3). The store [C] and
/// the simulator apply them to their own rows.
/// </summary>
public static class DataSyncInboxRules
{
    /// <summary>
    /// The allowed actions of an open item (§9.1, "Allowed actions"). Nothing is pre-chosen. <paramref name="twoWay"/>
    /// is the link's effective mode being TwoWay (§8.1); false for items that belong to no link.
    /// </summary>
    public static IReadOnlyList<DataSyncInboxAction> AllowedActions(DataSyncInboxItemType type, string subjectPath,
        DataSyncInboxPayload? payload, bool twoWay)
    {
        ArgumentNullException.ThrowIfNull(subjectPath);
        var actions = new List<DataSyncInboxAction>();
        switch (type)
        {
            case DataSyncInboxItemType.FieldConflict:
                actions.AddRange([DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote]);
                if (subjectPath == "name") actions.Add(DataSyncInboxAction.UseCustom);
                actions.Add(DataSyncInboxAction.Detach);
                break;
            case DataSyncInboxItemType.ChildRenameConflict:
                actions.AddRange([DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote]);
                if (!subjectPath.EndsWith(":parent", StringComparison.Ordinal)) actions.Add(DataSyncInboxAction.UseCustom);
                actions.Add(DataSyncInboxAction.Detach);
                break;
            case DataSyncInboxItemType.TypeChange:
                actions.AddRange([DataSyncInboxAction.Convert, DataSyncInboxAction.Detach]);
                if (twoWay) actions.Add(DataSyncInboxAction.KeepLocal);
                break;
            case DataSyncInboxItemType.DeletedThere:
            case DataSyncInboxItemType.ChildDeletedInUse:
                actions.AddRange([DataSyncInboxAction.DeleteHere, DataSyncInboxAction.KeepHereOnly]);
                if (twoWay) actions.Add(DataSyncInboxAction.RestoreEverywhere);
                break;
            case DataSyncInboxItemType.DeletedHereEditedThere:
                actions.AddRange([DataSyncInboxAction.RestoreHere, DataSyncInboxAction.KeepDeleted]);
                break;
            case DataSyncInboxItemType.LinkSuggestion:
                if (payload?.Candidates?.Any(c => c.Updatable) == true) actions.Add(DataSyncInboxAction.Link);
                actions.AddRange([DataSyncInboxAction.KeepBoth, DataSyncInboxAction.Skip]);
                break;
            case DataSyncInboxItemType.IdentityConflict:
                // Row M lists the peer's records that bind to one entity; row I lists this device's candidates.
                if (payload?.Records is { Count: > 0 }) actions.Add(DataSyncInboxAction.KeepRecordLinked);
                else if (payload?.Candidates?.Any(c => c.Updatable) == true) actions.Add(DataSyncInboxAction.KeepWithEntity);
                actions.Add(DataSyncInboxAction.Detach);
                break;
            case DataSyncInboxItemType.MassChildDeletion:
                actions.AddRange([DataSyncInboxAction.ReviewEach, DataSyncInboxAction.ApplyAll]);
                if (twoWay) actions.Add(DataSyncInboxAction.RestoreEverywhere);
                break;
            case DataSyncInboxItemType.SuspectedLostUpdate:
                actions.AddRange([DataSyncInboxAction.Publish, DataSyncInboxAction.Reapply]);
                break;
            case DataSyncInboxItemType.LargeChange:
                // Pausing the link is a link action, not an inbox action.
                actions.Add(DataSyncInboxAction.ApplyAll);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown inbox item type.");
        }

        return actions;
    }

    /// <summary>
    /// One pull's reconciliation (§9.3, <c>ReconcileInboxAsync</c>):
    /// <list type="bullet">
    /// <item>every draft is upserted: onto the open item of its subject when there is one (it keeps its creation
    /// time), else as a new item;</item>
    /// <item>a merger-derived item of this link whose entity was evaluated and whose subject was not produced again
    /// closes: <c>ResolvedElsewhere</c> by the hint's editor when the entity took a peer revision edited by another
    /// device (row K5), <c>Superseded</c> otherwise;</item>
    /// <item>state-derived items are never closed for not being produced, and items of subjects the pull did not
    /// evaluate are never touched.</item>
    /// </list>
    /// </summary>
    /// <param name="open">This link's open items (of both origins).</param>
    public static DataSyncInboxReconciliation Reconcile(IReadOnlyList<DataSyncOpenInboxItem> open,
        IReadOnlyList<DataSyncInboxDraft> drafts, IReadOnlyCollection<(string Kind, SyncKey Key)> evaluated,
        IReadOnlyList<DataSyncClosureHint> hints)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(drafts);
        ArgumentNullException.ThrowIfNull(evaluated);
        ArgumentNullException.ThrowIfNull(hints);
        var openBySubject = new Dictionary<(string, string, DataSyncInboxItemType, string), DataSyncOpenInboxItem>();
        foreach (var item in open) openBySubject.TryAdd((item.Kind, item.Key.Value, item.Type, item.SubjectPath), item);

        var upserts = drafts
            .Select(d => new DataSyncInboxUpsert(d,
                openBySubject.TryGetValue((d.Kind, d.Key.Value, d.Type, d.SubjectPath), out var existing)
                    ? existing.Id
                    : null))
            .ToList();

        var produced = drafts.Select(d => (d.Kind, d.Key.Value, d.Type, d.SubjectPath)).ToHashSet();
        var evaluatedKeys = evaluated.Select(e => (e.Kind, e.Key.Value)).ToHashSet();
        var hintByKey = new Dictionary<(string, string), DataSyncClosureHint>();
        foreach (var hint in hints) hintByKey[(hint.Kind, hint.Key.Value)] = hint;

        var closes = new List<DataSyncInboxClose>();
        foreach (var item in open.OrderBy(i => i.Id))
        {
            if (item.Origin != DataSyncInboxItemOrigin.Merger) continue;
            if (!evaluatedKeys.Contains((item.Kind, item.Key.Value))) continue;
            if (produced.Contains((item.Kind, item.Key.Value, item.Type, item.SubjectPath))) continue;
            closes.Add(hintByKey.TryGetValue((item.Kind, item.Key.Value), out var hint)
                ? new DataSyncInboxClose(item.Id, hint.Closure, hint.By)
                : new DataSyncInboxClose(item.Id, DataSyncInboxClosure.Superseded, null));
        }

        return new DataSyncInboxReconciliation(upserts, closes);
    }

    /// <summary>
    /// Dominance across links (§9.3, engineering must-fix 21): after a commit that changed an entity, an open
    /// merger-derived item of ANY link whose record vector is ≤ the entity's new vector is settled — the entity has
    /// incorporated that record's history. It closes <c>ResolvedElsewhere</c> by the revision's editor when that
    /// revision was edited by another device, <c>Superseded</c> when it was produced here. Null when the item
    /// stands (state-derived, no record vector, not dominated, or an identity question).
    /// </summary>
    /// <remarks>
    /// <c>LinkSuggestion</c> and <c>IdentityConflict</c> ask which entities are the same, which no vector settles:
    /// a record bound to two entities here (row I) or two records bound to one (row M) stay so whatever the
    /// entity's vector. Closing them by dominance only made the merger raise them again at the next pull, so the
    /// question flickered and could never be answered (found by the convergence simulator). They close when the
    /// merger no longer produces them.
    /// </remarks>
    public static DataSyncInboxClose? DominanceClosure(DataSyncOpenInboxItem item, DataSyncVersionVector entityVv,
        DataSyncEditorRef? entityLastEditor, bool lastEditorIsThisDevice)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(entityVv);
        if (item.Origin != DataSyncInboxItemOrigin.Merger || item.RecordVv is not { } recordVv) return null;
        if (item.Type is DataSyncInboxItemType.LinkSuggestion or DataSyncInboxItemType.IdentityConflict) return null;
        if (recordVv.CompareTo(entityVv) is not (DataSyncVvRelation.Equal or DataSyncVvRelation.DominatedBy)) return null;
        return !lastEditorIsThisDevice && entityLastEditor is not null
            ? new DataSyncInboxClose(item.Id, DataSyncInboxClosure.ResolvedElsewhere, entityLastEditor)
            : new DataSyncInboxClose(item.Id, DataSyncInboxClosure.Superseded, null);
    }

    /// <summary>
    /// Whether a state-derived item's state still exists (§9.3, <c>CloseStaleStateItemsAsync</c>; also the resolve
    /// validation of §9.2). It closes exactly when this is false:
    /// <list type="bullet">
    /// <item><c>ChildDeletedInUse</c>: the hold <c>{child, link}</c> exists and the child is still here;</item>
    /// <item><c>MassChildDeletion</c>: the base's pending reason is still <c>MassChildDeletion</c>;</item>
    /// <item><c>SuspectedLostUpdate</c>: <c>PublishHeld</c> is still true;</item>
    /// <item><c>LargeChange</c>: a <c>LargeChange</c> pending record of the link remains.</item>
    /// </list>
    /// Merger-derived types have no such state and always stand here (they close by reconciliation or dominance).
    /// </summary>
    public static bool StateItemStands(DataSyncInboxItemType type, DataSyncStateItemFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return type switch
        {
            DataSyncInboxItemType.ChildDeletedInUse => facts.HoldExists,
            DataSyncInboxItemType.MassChildDeletion => facts.BasePendingReason == DataSyncPendingReason.MassChildDeletion,
            DataSyncInboxItemType.SuspectedLostUpdate => facts.PublishHeld,
            DataSyncInboxItemType.LargeChange => facts.LargeChangeRecordsWaiting,
            _ => true,
        };
    }
}

/// <summary>The state a state-derived item depends on (<see cref="DataSyncInboxRules.StateItemStands"/>).</summary>
/// <param name="HoldExists">The entity still holds the item's child for the item's link, and the child is still here.</param>
/// <param name="BasePendingReason">The pending reason of the item's base row on its link.</param>
/// <param name="PublishHeld">The entity's <c>PublishHeld</c>.</param>
/// <param name="LargeChangeRecordsWaiting">The link still has a <c>LargeChange</c> pending record.</param>
public sealed record DataSyncStateItemFacts(bool HoldExists = false, DataSyncPendingReason? BasePendingReason = null,
    bool PublishHeld = false, bool LargeChangeRecordsWaiting = false);

/// <summary>A draft to write: onto the open item <see cref="ExistingId"/> of its subject, or as a new item when null.</summary>
public sealed record DataSyncInboxUpsert(DataSyncInboxDraft Draft, long? ExistingId);

/// <summary>An open item to close, and by whom when it was resolved on another device.</summary>
public sealed record DataSyncInboxClose(long ItemId, DataSyncInboxClosure Closure, DataSyncEditorRef? By);

public sealed record DataSyncInboxReconciliation(IReadOnlyList<DataSyncInboxUpsert> Upserts,
    IReadOnlyList<DataSyncInboxClose> Closes);
