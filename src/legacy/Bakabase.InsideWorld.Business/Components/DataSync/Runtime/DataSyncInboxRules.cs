using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// What a person may do with an inbox item (§9.1 "Allowed actions") and what a resolution must carry (§9.2). The
/// boundary checks these before a resolve batch is enqueued; the task re-derives every item anyway.
/// </summary>
public static class DataSyncInboxRules
{
    /// <summary>
    /// A closed item allows nothing, and nothing is pre-chosen: the default action is null for every type. The one §9.1
    /// table is the engine's (<see cref="Modules.DataSync.Merging.DataSyncInboxRules.AllowedActions"/>): what the page
    /// offers is what the apply runner accepts.
    /// </summary>
    /// <param name="twoWay">
    /// The link's effective mode is two-way (§8.1: TwoWay, or both devices follow each other); false for an item that
    /// belongs to no link.
    /// </param>
    public static IReadOnlyList<DataSyncInboxAction> Allowed(DataSyncInboxItemType type, string subjectPath,
        DataSyncInboxPayload? payload, bool twoWay) =>
        Modules.DataSync.Merging.DataSyncInboxRules.AllowedActions(type, subjectPath, payload, twoWay);

    /// <summary>
    /// Conflict items: every open one of an entity, with every device, must be resolved in one batch (§9.2
    /// <c>ResolveTogether</c>).
    /// </summary>
    public static bool IsConflict(DataSyncInboxItemType type) =>
        type is DataSyncInboxItemType.FieldConflict or DataSyncInboxItemType.ChildRenameConflict;

    /// <summary>§8.1: TwoWay, or both devices follow each other.</summary>
    public static bool IsEffectivelyTwoWay(DataSyncLinkDbModel? link) =>
        link is not null && link.GetEffectiveMode() == DataSyncLinkMode.TwoWay;

    /// <summary>
    /// The inputs one resolution needs (§9.2): a name or label for <c>UseCustom</c>, a candidate for <c>Link</c> and
    /// <c>KeepWithEntity</c>, a record for <c>KeepRecordLinked</c>, and a valid name when <c>KeepBoth</c> names the
    /// copy. Returns why they are invalid, or null.
    /// </summary>
    public static string? ValidateInputs(DataSyncInboxItemDbModel item, DataSyncInboxPayload payload,
        DataSyncResolveInput input, DataSyncLimits limits)
    {
        switch (input.Action)
        {
            case DataSyncInboxAction.UseCustom:
                return item.Type == DataSyncInboxItemType.ChildRenameConflict
                    ? IsValidText(input.CustomValue, limits.MaxLabelLength) ? null : "label"
                    : IsValidText(input.CustomValue, limits.MaxNameLength) ? null : "name";
            case DataSyncInboxAction.Link:
            case DataSyncInboxAction.KeepWithEntity:
                return payload.Candidates?.Any(c => c.Updatable &&
                                                    string.Equals(c.LocalKey, input.TargetLocalKey,
                                                        StringComparison.Ordinal)) == true
                    ? null
                    : "target";
            case DataSyncInboxAction.KeepRecordLinked:
                return payload.Records?.Any(r =>
                    string.Equals(r.PrimaryKey, input.TargetRecordKey, StringComparison.Ordinal)) == true
                    ? null
                    : "record";
            case DataSyncInboxAction.KeepBoth:
                return input.NewName is null || IsValidText(input.NewName, limits.MaxNameLength) ? null : "name";
            default:
                return null;
        }
    }

    /// <summary>
    /// A name (1..256) or label (1..1024) as v3.1 §6.3 accepts it: not blank, within the limit, no U+0000 and no
    /// unpaired surrogate.
    /// </summary>
    public static bool IsValidText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > maxLength) return false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\0') return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }
}
