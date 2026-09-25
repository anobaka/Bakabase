using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// The allowed actions of an open item (§9.1, "Allowed actions"). Nothing is pre-chosen: the default action is null
/// for every type. A closed item allows nothing.
/// </summary>
public static class DataSyncInboxActions
{
    /// <param name="twoWay">
    /// The link's effective mode is TwoWay (§8.1: TwoWay, or both devices follow each other); false for items that
    /// belong to no link.
    /// </param>
    public static IReadOnlyList<DataSyncInboxAction> Allowed(DataSyncInboxItemType type, string subjectPath,
        DataSyncInboxPayload? payload, bool twoWay)
    {
        var actions = new List<DataSyncInboxAction>();
        switch (type)
        {
            case DataSyncInboxItemType.FieldConflict:
                actions.AddRange([DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote]);
                // "Other name…" is offered for the name only.
                if (subjectPath == "name") actions.Add(DataSyncInboxAction.UseCustom);
                actions.Add(DataSyncInboxAction.Detach);
                break;
            case DataSyncInboxItemType.ChildRenameConflict:
                actions.AddRange([DataSyncInboxAction.KeepLocal, DataSyncInboxAction.UseRemote]);
                // A label can be typed; a parent cannot.
                if (!subjectPath.EndsWith(":parent", StringComparison.Ordinal))
                    actions.Add(DataSyncInboxAction.UseCustom);
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
                if (payload?.Records is {Count: > 0}) actions.Add(DataSyncInboxAction.KeepRecordLinked);
                else if (payload?.Candidates?.Any(c => c.Updatable) == true)
                    actions.Add(DataSyncInboxAction.KeepWithEntity);
                actions.Add(DataSyncInboxAction.Detach);
                break;
            case DataSyncInboxItemType.MassChildDeletion:
                actions.AddRange([DataSyncInboxAction.ReviewEach, DataSyncInboxAction.ApplyAll]);
                if (twoWay) actions.Add(DataSyncInboxAction.RestoreEverywhere);
                break;
            case DataSyncInboxItemType.SuspectedLostUpdate:
                actions.Add(DataSyncInboxAction.Publish);
                // Reapply needs the apply's change list; once it is gone the card says so (§6.5).
                if (payload?.Detail != DataSyncLostUpdateGuard.ReapplyUnavailable) actions.Add(DataSyncInboxAction.Reapply);
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
}
