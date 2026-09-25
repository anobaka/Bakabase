using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// The first-contact planner's input (§2.6): a staged pull instead of v3.1's package. <c>LocalState</c> supplies
/// version vectors for the §8.3 refinement (<see cref="DataSyncPlanItemReason.LocalIsNewer"/>).
/// </summary>
public sealed record DataSyncPlanInput(
    DataSyncStagedPull Incoming,
    IReadOnlyDictionary<string, LocalKindSnapshot> Local,
    IReadOnlyDictionary<string, DataSyncLocalKindState> LocalState,
    IReadOnlyDictionary<string, IDataSyncKindCodec> Codecs,
    string? OwnNodeId);                      // to flag FromThisDevice

/// <summary>The first-link review's planner (v3.1 §7, with §8.3's changes). Pure.</summary>
public static class DataSyncPlanner
{
    /// <summary>The full plan: no truncation. Pure.</summary>
    public static DataSyncPlan Plan(DataSyncPlanInput input) => throw new NotImplementedException();

    /// <summary>
    /// strict = true at the HTTP boundary: every problem is an Error and nothing is resolved.
    /// strict = false inside the BTask: total — never an Error; each problem is that item's ChangedSinceReview
    /// outcome and the rest resolve normally.
    /// </summary>
    public static ResolveResult Resolve(DataSyncPlan plan, DataSyncStagedPull incoming,
        IReadOnlyDictionary<string, LocalKindSnapshot> local, IReadOnlyDictionary<string, IDataSyncKindCodec> codecs,
        IReadOnlyList<DataSyncPlanDecision> decisions, bool strict) => throw new NotImplementedException();

    /// <summary>
    /// After a strict Resolve succeeded: one explicit decision per item that is not Held — the caller's own, else
    /// the item's default — each with the token it was checked against.
    /// </summary>
    public static IReadOnlyList<DataSyncPlanDecision> CompleteDecisions(DataSyncPlan plan,
        IReadOnlyList<DataSyncPlanDecision> decisions) => throw new NotImplementedException();

    /// <summary>Per kind: creates (incoming order), updates and binds (incoming order), deletes (§2.3).</summary>
    public static IReadOnlyList<ApplyBatch> BuildBatches(ResolveResult resolved, IReadOnlyList<string> kindOrder) =>
        throw new NotImplementedException();
}

/// <summary>What HTTP sees: the full plan cut to the inline caps, and pages of the rest (v3.1 §7.8).</summary>
public static class DataSyncPlanView
{
    public const int MaxInline = 200;                 // per item and per candidate
    public const int MaxInlineChildChanges = 20_000;  // whole plan, option/extension changes only

    public static DataSyncPlan Truncate(DataSyncPlan full) => throw new NotImplementedException();

    /// <summary>take ≤ 500.</summary>
    public static DataSyncChangePage Page(DataSyncPlan full, string itemId, string? candidateLocalKey,
        int skip, int take) => throw new NotImplementedException();
}
