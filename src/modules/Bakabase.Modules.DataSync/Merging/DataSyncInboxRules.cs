namespace Bakabase.Modules.DataSync.Merging;

/// <summary>
/// The pure rules of the inbox: the actions an item allows (§9.1). When items close (§9.3) is the store's
/// (<c>DataSyncStore.Inbox</c>).
/// </summary>
public static class DataSyncInboxRules
{
    /// <summary>
    /// A SuspectedLostUpdate item's <c>Detail</c> once Reapply cannot run: the apply's change list is gone (retention,
    /// or the apply was undone), so nothing says what to write back. The item then offers Publish only (§6.5).
    /// </summary>
    public const string ReapplyUnavailableDetail = "reapplyUnavailable";

    /// <summary>
    /// A SuspectedLostUpdate item's <c>Detail</c> while Reapply would remove children resources here use (its
    /// <c>Children</c> lists them): Reapply never removes one in use, so it waits until nothing uses them. It stays
    /// offered (§6.5).
    /// </summary>
    public const string ReapplyInUseDetail = "reapplyInUse";

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
                actions.Add(DataSyncInboxAction.Publish);
                if (payload?.Detail != ReapplyUnavailableDetail) actions.Add(DataSyncInboxAction.Reapply);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown inbox item type.");
        }

        return actions;
    }
}
