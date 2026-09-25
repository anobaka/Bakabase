using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// The first-contact planner's input (§2.6): a staged pull instead of v3.1's package. <c>LocalState</c> supplies
/// version vectors for the §8.3 refinement (<see cref="DataSyncPlanItemReason.LocalIsNewer"/>) and the keys of rows
/// kept out of sync (§5.2).
/// </summary>
/// <param name="LinkMode">
/// The mode the link will have once the review is applied: <c>TwoWay</c> adds
/// <see cref="DataSyncWarningCode.NameUsedEverywhere"/> to every item that offers a separate name (§8.3). A copy once
/// passes <c>Off</c>.
/// </param>
public sealed record DataSyncPlanInput(
    DataSyncStagedPull Incoming,
    IReadOnlyDictionary<string, LocalKindSnapshot> Local,
    IReadOnlyDictionary<string, DataSyncLocalKindState> LocalState,
    IReadOnlyDictionary<string, IDataSyncKindCodec> Codecs,
    string? OwnNodeId,                      // to flag FromThisDevice
    DataSyncLinkMode LinkMode = DataSyncLinkMode.Off)
{
    /// <summary>The input over this device's local state, with <c>Local</c> built by <c>ToPlannerSnapshot()</c>.</summary>
    public static DataSyncPlanInput FromLocalState(DataSyncStagedPull incoming,
        IReadOnlyDictionary<string, DataSyncLocalKindState> localState, IReadOnlyDictionary<string, IDataSyncKindCodec> codecs,
        string? ownNodeId, DataSyncLinkMode linkMode = DataSyncLinkMode.Off)
    {
        ArgumentNullException.ThrowIfNull(localState);
        return new DataSyncPlanInput(incoming,
            localState.ToDictionary(p => p.Key, p => p.Value.ToPlannerSnapshot(), StringComparer.Ordinal), localState,
            codecs, ownNodeId, linkMode);
    }
}

/// <summary>
/// The first-link review's planner (v3.1 §7, with §8.3's changes). Pure: no clock, no randomness, no I/O; the same
/// input always gives the same plan, byte for byte, and the same <see cref="DataSyncPlan.PlanId"/>.
/// </summary>
public static class DataSyncPlanner
{
    /// <summary>The full plan: no truncation (HTTP carries <see cref="DataSyncPlanView.Truncate"/>). Pure.</summary>
    public static DataSyncPlan Plan(DataSyncPlanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new DataSyncPlanBuilder(input).Build();
    }

    /// <summary>
    /// strict = true at the HTTP boundary: every problem is an Error and nothing is resolved.
    /// strict = false inside the BTask: total — never an Error; each problem is that item's ChangedSinceReview
    /// outcome and the rest resolve normally.
    /// </summary>
    /// <remarks>
    /// Without <c>LocalState</c> the rows kept out of sync are unknown, so their keys may be offered as aliases (and
    /// refused by the identity pre-flight). Prefer the overload taking the <see cref="DataSyncPlanInput"/> the plan was
    /// built from.
    /// </remarks>
    public static ResolveResult Resolve(DataSyncPlan plan, DataSyncStagedPull incoming,
        IReadOnlyDictionary<string, LocalKindSnapshot> local, IReadOnlyDictionary<string, IDataSyncKindCodec> codecs,
        IReadOnlyList<DataSyncPlanDecision> decisions, bool strict) =>
        Resolve(plan, new DataSyncPlanInput(incoming, local, new Dictionary<string, DataSyncLocalKindState>(), codecs, null),
            decisions, strict);

    /// <summary>
    /// Resolve over the input <paramref name="plan"/> was built from, so the alias rule also knows the rows kept out
    /// of sync (§5.3).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Items come one per plan item, in plan order. At the boundary a missing decision takes the item's default when
    /// the item needs no confirmation; inside the task it never does, because every item the task writes must carry
    /// the token of what the person saw (B1): a missing decision there is that item's <c>decisionMissing</c>.
    /// </para>
    /// <para>
    /// Each resolved item carries what its base needs (§8.3 step 5): <see cref="ResolvedItem.TargetLocalKey"/> and the
    /// merge's or create's <see cref="ResolvedItem.ChildMap"/>. A review never removes, so no
    /// <see cref="UpdateEntityOperation"/> it makes has removed children, and an Unchanged item (LocalIsNewer included)
    /// never writes content: at most it records keys.
    /// </para>
    /// </remarks>
    public static ResolveResult Resolve(DataSyncPlan plan, DataSyncPlanInput input,
        IReadOnlyList<DataSyncPlanDecision> decisions, bool strict)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(decisions);
        return DataSyncDecisionResolver.Resolve(plan, input, decisions, strict);
    }

    /// <summary>
    /// The strict checks of Resolve alone, against the full plan: the errors a refused apply returns in
    /// <c>DataSyncApplyStart.DecisionErrors</c>
    /// (see <see cref="DataSyncPlanView.RejectDecisions"/>). Empty when the decisions may be enqueued. Item errors come
    /// in plan order, then unknown and duplicate decisions in the order they were sent.
    /// </summary>
    public static IReadOnlyList<DataSyncDecisionError> ValidateDecisions(DataSyncPlan plan,
        IReadOnlyList<DataSyncPlanDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(decisions);
        return DataSyncDecisionResolver.Validate(plan, decisions);
    }

    /// <summary>
    /// After a strict Resolve succeeded: one explicit decision per item that is not Held — the caller's own, else
    /// the item's default — each with the token it was checked against. Defaults are copied only where they need no
    /// confirmation, exactly as the strict check accepts them; Held items get none.
    /// </summary>
    public static IReadOnlyList<DataSyncPlanDecision> CompleteDecisions(DataSyncPlan plan,
        IReadOnlyList<DataSyncPlanDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(decisions);
        var sent = new Dictionary<string, DataSyncPlanDecision>(StringComparer.Ordinal);
        foreach (var decision in decisions) sent.TryAdd(decision.ItemId, decision);

        var complete = new List<DataSyncPlanDecision>();
        foreach (var item in plan.Kinds.SelectMany(s => s.Items))
        {
            if (item.Type == DataSyncPlanItemType.Held || item.AllowedResolutions.Count == 0) continue;
            if (sent.TryGetValue(item.ItemId, out var own)) complete.Add(own);
            else if (item.DefaultResolution is not null && !item.RequiresConfirmation) complete.Add(DefaultDecision(item));
        }

        return complete;
    }

    /// <summary>The item's default made explicit, with the token of the item or of its default target candidate.</summary>
    internal static DataSyncPlanDecision DefaultDecision(DataSyncPlanItem item)
    {
        var target = item.DefaultTargetLocalKey;
        var token = item.Candidates.FirstOrDefault(c => target is not null && c.LocalKey == target)?.ReviewToken ??
                    item.ReviewToken;
        return new DataSyncPlanDecision(item.ItemId, item.DefaultResolution!.Value, target, null, [], token);
    }

    /// <summary>
    /// Per kind, in <paramref name="kindOrder"/> (then any other kind, ordinal): each subtype change alone in its own
    /// batch (phase one of Convert, §8.5.6), then one batch of creates (resolved order, which is incoming position),
    /// updates and binds (resolved order), deletes (§2.3).
    /// </summary>
    public static IReadOnlyList<ApplyBatch> BuildBatches(ResolveResult resolved, IReadOnlyList<string> kindOrder)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(kindOrder);
        var byKind = resolved.Items.Where(i => i.Operation is not null).GroupBy(i => i.Kind, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(i => i.Operation!).ToList(), StringComparer.Ordinal);
        var kinds = kindOrder.Distinct(StringComparer.Ordinal)
            .Concat(byKind.Keys.Except(kindOrder, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal));

        var batches = new List<ApplyBatch>();
        foreach (var kind in kinds)
        {
            if (!byKind.TryGetValue(kind, out var operations)) continue;
            batches.AddRange(operations.OfType<ChangeSubtypeOperation>().Select(op => new ApplyBatch(kind, [op])));
            var rest = operations.OfType<CreateEntityOperation>().Cast<ApplyOperation>()
                .Concat(operations.Where(op => op is UpdateEntityOperation or BindOnlyOperation))
                .Concat(operations.OfType<DeleteEntityOperation>())
                .Concat(operations.Where(op => op is not (CreateEntityOperation or UpdateEntityOperation or BindOnlyOperation
                    or DeleteEntityOperation or ChangeSubtypeOperation)))
                .ToList();
            if (rest.Count > 0) batches.Add(new ApplyBatch(kind, rest));
        }

        return batches;
    }
}
