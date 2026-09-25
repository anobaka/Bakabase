using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.Modules.DataSync.Planning;

// v3.1 §2.4 plan records, exposed over HTTP: Newtonsoft-safe (no JsonNode, no object, no enum-keyed
// dictionaries).

public sealed record DataSyncDisplayValue(
    string? Text, string? Color = null, string? Group = null, IReadOnlyList<string>? Path = null,
    bool? Flag = null, int? Number = null);

public sealed record DataSyncFieldChange(
    string ChangeId,                 // stable per item, v3.1 §7.4
    DataSyncFieldChangeKind Kind,
    string Path,                     // "name" | "ignoreCase" | "settings.precision" | "defaultValue" | "choices"
                                     // | "tags" | "nodes" | "extensions"
    DataSyncDisplayValue? From,      // null for AddChild/AddMember
    DataSyncDisplayValue? To,
    string? DependsOnChangeId,       // e.g. a child node add depends on its parent's add
    int? InUseCount);                // rename rows only: resources showing the local option. Not in tokens.

/// <summary>Args keys per code are listed in v3.1 §7.4. ChangeId ties a warning to one change row, when it has one.</summary>
public sealed record DataSyncPlanWarning(DataSyncWarningCode Code, string? ChangeId,
    IReadOnlyDictionary<string, string>? Args);

public sealed record DataSyncChangeCounts(int Total, int Set, int Add, int Rename, int Recolor);

public sealed record DataSyncWarningCount(DataSyncWarningCode Code, int Count);

public sealed record DataSyncPlanEntity(string? LocalKey, string Name, string? Subtype, int Position, int ChildCount);

public sealed record DataSyncPlanCandidate(
    string LocalKey, string Name, string? Subtype, DataSyncNaturalMatch Match,
    IReadOnlyList<DataSyncFieldChange> Changes,   // ≤ 200 inline (v3.1 §7.8); the rest through the changes endpoint
    DataSyncChangeCounts ChangeCounts, bool ChangesTruncated,
    IReadOnlyList<DataSyncPlanWarning> Warnings,  // ≤ 200 inline
    IReadOnlyList<DataSyncWarningCount> WarningCounts, bool WarningsTruncated,
    int UnchangedChildren, int LocalOnlyChildren,
    bool RecordsNewKeys,                          // a Link/Update to this candidate would record ≥ 1 key (Link: always)
    string ReviewToken);

public sealed record DataSyncPlanItem(
    string ItemId,                                          // "{kind}/k/{primaryKey}"
    string Kind,
    DataSyncPlanItemType Type,
    DataSyncPlanItemReason? Reason,
    DataSyncHeldReason? HeldReason,
    DataSyncPlanEntity Incoming,
    DataSyncPlanEntity? Local,                              // Update/Unchanged/TypeMismatch target
    IReadOnlyList<DataSyncPlanCandidate> Candidates,        // Link / NeedsDecision targets
    IReadOnlyList<DataSyncFieldChange> Changes,             // Update/Unchanged (vs Local); ≤ 200 inline
    DataSyncChangeCounts ChangeCounts, bool ChangesTruncated,
    int UnchangedChildren, int LocalOnlyChildren,
    IReadOnlyList<DataSyncPlanResolution> AllowedResolutions,   // empty for Held
    DataSyncPlanResolution? DefaultResolution,              // null → the user must decide (or Held)
    string? DefaultTargetLocalKey,                          // Link: the unique candidate's LocalKey;
                                                            // Update/Unchanged: Local.LocalKey; other types: null
    bool RequiresConfirmation,                              // pending until decided or bulk-confirmed
    bool BulkLinkEligible,                                  // eligible for "Link all exact matches"
    bool OffersSeparateName,                                // UI proposes "<name> (<source>)"
    bool RecordsNewKeys,                                    // items with Local: an Update would record ≥ 1 incoming
                                                            // key (v3.1 §7.7 alias rule). Not in tokens.
    string ReviewToken,
    IReadOnlyList<DataSyncPlanWarning> Warnings,            // item-level + change-level; ≤ 200 inline
    IReadOnlyList<DataSyncWarningCount> WarningCounts, bool WarningsTruncated);

public sealed record DataSyncPlanKindSection(string Kind, int SchemaVersion, bool Supported,
    IReadOnlyList<DataSyncPlanItem> Items, int LocalOnlyCount);

public sealed record DataSyncKindTypeCount(string Kind, DataSyncPlanItemType Type, int Count);

public sealed record DataSyncPlanSummary(
    IReadOnlyList<DataSyncKindTypeCount> Counts,            // one row per (kind, type) with Count > 0
    int PendingCount, int BulkLinkEligibleCount, int HeldCount);

public sealed record DataSyncPlan(
    string PlanId,                                          // 16 hex; v3.1 §7.5
    string SnapshotContentHash,                             // hash of the manifest's kind hashes (§2.6)
    IReadOnlyList<DataSyncPlanKindSection> Kinds,           // apply order
    DataSyncPlanSummary Summary,
    IReadOnlyList<DataSyncPlanWarning> Warnings);           // plan-level (FromThisDevice)

public sealed record DataSyncPlanDecision(
    string ItemId,
    DataSyncPlanResolution Resolution,
    string? TargetLocalKey,                  // required for Link and Update; must be null for Create,
                                             // CreateSeparate, Skip
    string? NewName,                         // CreateSeparate
    IReadOnlyList<string> ExcludedChangeIds, // concrete ids or group ids "<prefix>:*" (v3.1 §7.4)
    string ReviewToken);                     // echoed from the item, or from the chosen candidate

public sealed record DataSyncDecisionError(string ItemId, DataSyncDecisionErrorCode Code);

/// <summary>
/// Detail: why an item was not applied, for ResultJson diagnostics only (never localized, never in HTTP):
/// "tokenMismatch" | "decisionMissing" | "resolutionNotAllowed" | "targetNotAllowed" | "targetUsedTwice"
/// | "unknownChange" | "invalidName" | "hashMismatch" | "identityConflict"; null otherwise.
/// </summary>
/// <param name="TargetLocalKey">
/// The local entity an Update, Link or Unchanged item is bound to, also when it writes nothing (NoChange): the review
/// apply records a base for it (§8.3 step 5). Null for creates, skips, held and unapplied items.
/// </param>
/// <param name="ChildMap">
/// Incoming child id → local child id after the merge (Update, Link, Unchanged) or the create (Create,
/// CreateSeparate): the base's <c>ChildMap</c> (§8.3 step 5). Null when nothing was resolved.
/// </param>
public sealed record ResolvedItem(string ItemId, string Kind, DataSyncItemOutcome Outcome,
    DataSyncItemAction Action, ApplyOperation? Operation, string? Detail = null, string? TargetLocalKey = null,
    IReadOnlyDictionary<string, string>? ChildMap = null);

/// <summary>Errors is always empty when strict = false.</summary>
public sealed record ResolveResult(IReadOnlyList<ResolvedItem> Items, IReadOnlyList<DataSyncDecisionError> Errors);
