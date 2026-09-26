using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Abstractions;

/// <param name="Content">Typed DTO when valid (after dropping invalid children).</param>
/// <param name="Held">Entity-level problem: the content cannot be applied here.</param>
/// <param name="Errors">Entity-level validation failures (Held = Invalid when non-empty).</param>
/// <param name="Warnings">UnknownFieldsIgnored, OptionDropped, …</param>
/// <param name="Unknown">
/// The top-level content members this build does not know, verbatim, for preservation (§8.9).
/// UnknownFieldsIgnored is still reported.
/// </param>
public sealed record CodecReadResult(
    object? Content,
    DataSyncHeldReason? Held,
    IReadOnlyList<string> Errors,
    IReadOnlyList<DataSyncPlanWarning> Warnings,
    JsonObject? Unknown = null);

public sealed record EntityDiff(
    IReadOnlyList<DataSyncFieldChange> Changes,     // every change that brings local towards incoming; never removals
    IReadOnlyList<DataSyncPlanWarning> Warnings,    // incl. fold warnings with their "when" condition
    int UnchangedChildren,                          // children present on both sides with nothing to change
    int LocalOnlyChildren);                         // kept, never removed

public sealed record MergeResult(
    object Content,                                 // after the kind's own normalization (e.g. option folding).
                                                    // Merge keeps every local child, in local order, valid or not:
                                                    // none is dropped or reordered, and one changes only through an
                                                    // accepted rename/recolor. Accepted adds are appended; accepted
                                                    // scalar sets are applied.
    IReadOnlyDictionary<string, string> ChildIdMap, // incoming child id → local child id after the merge
    IReadOnlyList<string> AddedChildIds,            // child ids that exist only because of this merge
    IReadOnlyList<DataSyncPlanWarning> Warnings);

/// <summary>One kind's content: pure validation, canonical form, comparison and merging (v3.1 §2.2, §2.3 here).</summary>
public interface IDataSyncKindCodec
{
    DataSyncKindDescriptor Descriptor { get; }

    /// <summary>
    /// Bring an older content schema up to Descriptor.SchemaVersion. Identity for v1.
    /// Throws DataSyncHeldException(NewerSchema) when fromSchemaVersion &gt; SchemaVersion.
    /// </summary>
    JsonObject Upgrade(JsonObject content, int fromSchemaVersion);

    /// <summary>
    /// PEER input (and "would this build hold it?" for what this device publishes): validate and parse content at
    /// the current schema, dropping invalid children and holding invalid entities. Never throws for bad input.
    /// </summary>
    CodecReadResult Read(JsonObject content, DataSyncLimits limits);

    /// <summary>
    /// LOCAL content only: parse the adapter's own canonical JSON into the typed DTO. Never validates, drops or
    /// holds. Throws only on a codec bug (malformed shape). Invariant, tested per kind: Write(ReadLocal(x)) is
    /// byte-identical to x.
    /// </summary>
    object ReadLocal(JsonObject content);

    /// <summary>Canonical JSON (deterministic, no nulls, no floats) — the only input to hashing.</summary>
    JsonObject Write(object content);

    string NameOf(object content);
    string? SubtypeOf(object content);          // customProperty: PropertyType name; extensionGroup: null
    int ChildCountOf(object content);           // options / extensions, for UI summaries
    DataSyncNaturalMatch MatchNatural(object incoming, object local);
    EntityDiff Diff(object local, object incoming);

    /// <summary>acceptedChangeIds: concrete ids, already expanded from group exclusions and closed over DependsOnChangeId.</summary>
    MergeResult Merge(object local, object incoming, IReadOnlySet<string> acceptedChangeIds);

    MergeResult PrepareCreate(object incoming, string? nameOverride);

    /// <summary>
    /// Bumped whenever ComparisonForm's output changes for any input (§3.4). Stored per kind; a change recomputes
    /// hashes without issuing revisions (§6.1).
    /// </summary>
    int ComparisonFormVersion { get; }

    /// <summary>
    /// What this device publishes for a local entity (§3.5): overlays removed, withheld children removed, then the
    /// codec's own validated Read applied, so the result is exactly what a reader will accept. An entity the reader
    /// would hold comes back with Held set and no content (§3.5 step 4). Pure.
    /// </summary>
    DataSyncPublishable Publish(object localContent, DataSyncOverlay overlay, bool childrenLocal);

    /// <summary>The class-folded, id-free comparison form (§3.4). Input is PUBLISHED content (validated).</summary>
    JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal);

    /// <summary>
    /// Local child ids (subtree roots for multilevel) that Merge3 would delete because the peer deleted them
    /// (§8.5.4). Needs no usage: the merger asks for usage of exactly these ids first (§2.7 CollectUsageQueries).
    /// </summary>
    IReadOnlyList<string> ChildDeletionCandidates(DataSyncChildCandidatesInput input);

    /// <summary>Field-level merge (§8.5). Pure; never throws for valid inputs.</summary>
    DataSyncMerge3Result Merge3(DataSyncMerge3Input input);

    /// <summary>Children with display values, for pickers, overlays and inbox cards.</summary>
    IReadOnlyList<DataSyncChildInfo> ChildrenOf(object content);

    /// <summary>
    /// Where each child of <paramref name="from"/> that <paramref name="to"/> no longer has by id went: the first
    /// child of <paramref name="to"/> in the same label class (§3.4), under <paramref name="from"/>'s folding. A
    /// subtype change rebuilds the children with fresh ids (F73), and everything data sync keeps by local child id
    /// (holds, local-only children, child maps) follows them through this map. A child with no class in
    /// <paramref name="to"/> is absent from the map; so is one <paramref name="to"/> still has by id. The default
    /// compares labels (group and path) ordinally.
    /// </summary>
    IReadOnlyDictionary<string, string> MapChildrenByClass(object from, object to) =>
        DataSyncChildClassMap.Map(ChildrenOf(from), ChildrenOf(to), label => label);

    /// <summary>
    /// §3.5 steps 5–6: a record's content, <c>Write(publishedContent)</c> with the preserved unknown top-level members
    /// (<c>UnknownJson</c>, §8.9) merged back verbatim. A member this codec knows is never overwritten.
    /// </summary>
    /// <remarks>
    /// The default protects only the members <c>Write</c> emitted; <see cref="DataSyncKindCodec{TContent}"/> also
    /// protects the members its kind declares. <see cref="Canonical.DataSyncContentForms"/> calls this member, so the
    /// engine, persistence and the codec compute one form.
    /// </remarks>
    JsonObject WritePublished(object publishedContent, JsonObject? unknown) =>
        Canonical.DataSyncContentForms.WithUnknown(Write(publishedContent), unknown, []);

    /// <summary>
    /// §3.4 with the preserved unknown top-level members added verbatim (never over a member of the form or one this
    /// codec knows): the form every device hashes for an entity or a peer record that carries unknown members.
    /// </summary>
    JsonObject ComparisonForm(object publishedContent, string? orderKey, bool childrenLocal, JsonObject? unknown) =>
        Canonical.DataSyncContentForms.WithUnknown(ComparisonForm(publishedContent, orderKey, childrenLocal), unknown,
            []);

    /// <summary><c>SharedHash = ContentHash(ComparisonForm(publishedContent, orderKey, childrenLocal, unknown))</c> (§3.4).</summary>
    string SharedHash(object publishedContent, string? orderKey, bool childrenLocal, JsonObject? unknown) =>
        Canonical.ContentHash.Of(ComparisonForm(publishedContent, orderKey, childrenLocal, unknown));
}

/// <summary>
/// Thrown by Upgrade only. The wire reader catches it and holds the kind (or entity) with Reason; it never
/// escapes the reader.
/// </summary>
public sealed class DataSyncHeldException(DataSyncHeldReason reason, string? detail = null)
    : Exception(detail ?? reason.ToString())
{
    public DataSyncHeldReason Reason { get; } = reason;
}

/// <summary>Local-only rules on one entity (§3.6). Never published, never synced.</summary>
public sealed record DataSyncOverlay(
    IReadOnlyList<string> LocalOnlyChildren,        // local child ids kept on this device only
    IReadOnlyList<DataSyncHeldChild> HeldChildren)  // local child ids held out of publishing until an item is decided
{
    public static DataSyncOverlay None { get; } = new([], []);

    [JsonIgnore]
    public IEnumerable<string> HiddenChildIds => LocalOnlyChildren.Concat(HeldChildren.Select(h => h.ChildId)).Distinct();
}

/// <summary>A child held because the peer of LinkId deleted it while it is in use here. One child may be held by several links.</summary>
public sealed record DataSyncHeldChild(string ChildId, int LinkId);

/// <summary>
/// Content in the codec's typed form, after Publish. ChildrenWithheld counts children left out (null ids, invalid,
/// overlays).
/// </summary>
/// <param name="Content">The published content; null exactly when <paramref name="Held"/> is set.</param>
/// <param name="Held">
/// The reader would hold this entity (§3.5 step 4): it is published as <c>HeldAtSource</c> with this reason and no
/// content — for example Invalid when it has more children than a reader accepts.
/// </param>
/// <param name="HeldDetail">
/// Why, for this device's own diagnostics and UI (e.g. <c>tooManyChildren</c>); only with <paramref name="Held"/>, and
/// never on the wire.
/// </param>
public sealed record DataSyncPublishable(object? Content, int ChildrenWithheld, DataSyncPlanWarning[] Warnings,
    DataSyncHeldReason? Held = null, string? HeldDetail = null);

public sealed record DataSyncChildInfo(string Id, string? ParentId, DataSyncDisplayValue Display);

public sealed record DataSyncChildCandidatesInput(
    object? Base, object Local, DataSyncOverlay LocalOverlay, object Remote, DataSyncMerge3Mode Mode3,
    IReadOnlyDictionary<string, string> ChildMap, bool ChildrenLocalAnySide);

public sealed record DataSyncMerge3Input(
    object? Base,                                   // the peer's content at the last agreement (peer child ids); null = no base
    object Local,                                   // ReadLocal content: ALL local children
    DataSyncOverlay LocalOverlay,                   // LocalOnly/Held children: invisible to merging, always kept (§8.5.4 step 0)
    object Remote,                                  // validated peer content
    DataSyncMerge3Mode Mode3,                       // ThreeWay (with Base) | FastForward (R dominates L) | NoBase
                                                    // | Convert (§8.5.6 phase two; Base = the base before the type
                                                    //   change, or null: name then merges by the NoBase rule)
    IReadOnlyDictionary<string, string> ChildMap,   // peer child id → local child id (from the base row); many-to-one allowed
    bool LocalChildrenLocal,                        // childrenLocal as this device holds it
    bool BaseChildrenLocal,                         // childrenLocal in the base (false without a base)
    DataSyncLinkMode Mode,                          // Follow: the peer wins conflicting scalar fields (§8.5.2 exceptions)
    bool LocalLastEditorIsSelf,                     // Follow on a hub: overriding another device's value asks (§8.5.2)
    DataSyncMergeSide AppearanceWinner,             // §8.5.5
    IReadOnlyDictionary<string, int> LocalChildUsage,   // local child id → resources using it; a missing id counts as in use
    DataSyncChildDeletionMode ChildDeletions);      // Normal | Apply (skip B4) | ReviewEach | Restore — from item flags (§9.2)

/// <summary>Public: travels in FlagsJson.</summary>
public enum DataSyncChildDeletionMode { Normal = 1, Apply = 2, ReviewEach = 3, Restore = 4 }

public sealed record DataSyncMerge3Result(
    object Merged,
    IReadOnlyList<DataSyncFieldOutcome> Fields,     // one per changed or conflicting path, §8.5.1 path grammar
    IReadOnlyDictionary<string, string> ChildMap,   // updated peer → local map
    IReadOnlyList<string> AddedChildIds,            // local ids that exist only because of this merge
    IReadOnlyList<string> RemovedChildIds,          // local children removed (peer deleted them, unused here, unchanged here)
    IReadOnlyList<string> HeldChildIds,             // peer deleted them, in use here: kept and held
    IReadOnlyList<string> ReleasedChildIds,         // Held children the peer re-added since the base: mapped, hold removed
    IReadOnlyList<string> MassDeletionCandidates,   // non-empty ⇔ B4 tripped: Merged == Local and nothing else applies (§8.5.4 step 7)
    IReadOnlyList<DataSyncPlanWarning> Warnings,
    bool TypeChanged);                              // the peer changed the subtype: Merged == Local, nothing else merged

public sealed record DataSyncFieldOutcome(string Path, DataSyncFieldResolution Resolution,
    DataSyncDisplayValue? Base, DataSyncDisplayValue? Local,
    DataSyncDisplayValue? Remote, DataSyncDisplayValue? Result);
