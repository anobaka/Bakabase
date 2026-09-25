using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>
/// v3.1 §7.7: decisions checked against the full plan, then turned into operations. The checks run in a fixed order
/// per item — a missing decision, a resolution the item does not allow, a target it does not own, a token that is no
/// longer the target's, an unknown change, an invalid name — and then target uniqueness across items; the first
/// failure is the item's.
/// </summary>
internal sealed class DataSyncDecisionResolver
{
    /// <summary>Names travel to the adapter as they are; v3.1 §7.7 bounds them like a peer's (MaxNameLength).</summary>
    private static readonly int MaxNameLength = DataSyncLimits.Default.MaxNameLength;

    private readonly DataSyncPlan _plan;
    private readonly bool _strict;
    private readonly List<Verdict> _verdicts = [];
    private readonly List<DataSyncDecisionError> _decisionErrors = [];

    private DataSyncDecisionResolver(DataSyncPlan plan, IReadOnlyList<DataSyncPlanDecision> decisions, bool strict)
    {
        _plan = plan;
        _strict = strict;
        Check(decisions);
    }

    /// <summary>The strict checks alone: the errors <c>DataSyncApplyStart.DecisionErrors</c> carries.</summary>
    public static IReadOnlyList<DataSyncDecisionError> Validate(DataSyncPlan plan, IReadOnlyList<DataSyncPlanDecision> decisions) =>
        new DataSyncDecisionResolver(plan, decisions, strict: true).Errors();

    public static ResolveResult Resolve(DataSyncPlan plan, DataSyncPlanInput input,
        IReadOnlyList<DataSyncPlanDecision> decisions, bool strict)
    {
        var resolver = new DataSyncDecisionResolver(plan, decisions, strict);
        if (strict && resolver.Errors() is { Count: > 0 } errors) return new ResolveResult([], errors);
        return new ResolveResult(resolver.Build(input), []);
    }

    // ---- checks ----------------------------------------------------------------------------------

    /// <param name="Candidate">The chosen candidate, when the target is one of the item's candidates.</param>
    /// <param name="Changes">The full change list the decision applies to: the candidate's or the item's.</param>
    private sealed record Verdict(DataSyncPlanItem Item, DataSyncPlanDecision? Decision,
        DataSyncDecisionErrorCode? Failure, DataSyncPlanCandidate? Candidate, IReadOnlyList<DataSyncFieldChange> Changes,
        IReadOnlySet<string> Excluded, bool Held);

    private void Check(IReadOnlyList<DataSyncPlanDecision> decisions)
    {
        var items = _plan.Kinds.SelectMany(s => s.Items).ToList();
        var known = items.Select(i => i.ItemId).ToHashSet(StringComparer.Ordinal);
        var sent = new Dictionary<string, DataSyncPlanDecision>(StringComparer.Ordinal);
        foreach (var decision in decisions)
        {
            // Inside the task these cannot arise from CompleteDecisions: the unknown one is ignored, the first wins.
            if (!known.Contains(decision.ItemId))
                _decisionErrors.Add(new DataSyncDecisionError(decision.ItemId, DataSyncDecisionErrorCode.UnknownItem));
            else if (!sent.TryAdd(decision.ItemId, decision))
                _decisionErrors.Add(new DataSyncDecisionError(decision.ItemId, DataSyncDecisionErrorCode.DuplicateDecision));
        }

        foreach (var item in items)
        {
            var decision = sent.GetValueOrDefault(item.ItemId);
            if (item.Type == DataSyncPlanItemType.Held || item.AllowedResolutions.Count == 0)
            {
                // Held needs no decision. One sent anyway is refused at the boundary, and ignored inside the task.
                DataSyncDecisionErrorCode? failure =
                    decision is not null && _strict ? DataSyncDecisionErrorCode.ResolutionNotAllowed : null;
                _verdicts.Add(new Verdict(item, decision, failure, null, [], EmptySet, Held: true));
                continue;
            }

            // At the boundary a default may stand in for a missing decision; inside the task every item the task may
            // write must carry the token of what the person saw (B1), so a missing one is never filled in there.
            if (decision is null && _strict && item.DefaultResolution is not null && !item.RequiresConfirmation)
                decision = DataSyncPlanner.DefaultDecision(item);
            _verdicts.Add(Evaluate(item, decision));
        }

        // A local target may be used once, by key-bound updates and links alike.
        var twice = Enumerable.Range(0, _verdicts.Count)
            .Where(i => _verdicts[i] is { Failure: null, Held: false } v &&
                        v.Decision!.Resolution is DataSyncPlanResolution.Link or DataSyncPlanResolution.Update)
            .GroupBy(i => (_verdicts[i].Item.Kind, _verdicts[i].Decision!.TargetLocalKey)).Where(g => g.Count() > 1)
            .SelectMany(g => g).ToList();
        foreach (var i in twice) _verdicts[i] = _verdicts[i] with { Failure = DataSyncDecisionErrorCode.TargetUsedTwice };
    }

    private static Verdict Evaluate(DataSyncPlanItem item, DataSyncPlanDecision? d)
    {
        Verdict Fail(DataSyncDecisionErrorCode code) => new(item, d, code, null, [], EmptySet, false);

        if (d is null) return Fail(DataSyncDecisionErrorCode.DecisionMissing);
        if (!item.AllowedResolutions.Contains(d.Resolution)) return Fail(DataSyncDecisionErrorCode.ResolutionNotAllowed);

        // The v3.1 target rule.
        DataSyncPlanCandidate? candidate = null;
        switch (d.Resolution)
        {
            case DataSyncPlanResolution.Create or DataSyncPlanResolution.CreateSeparate or DataSyncPlanResolution.Skip:
                if (d.TargetLocalKey is not null) return Fail(DataSyncDecisionErrorCode.TargetNotAllowed);
                break;
            case DataSyncPlanResolution.Update when item.Type is DataSyncPlanItemType.Update or DataSyncPlanItemType.Unchanged:
                if (d.TargetLocalKey is null || d.TargetLocalKey != item.Local?.LocalKey)
                    return Fail(DataSyncDecisionErrorCode.TargetNotAllowed);
                break;
            case DataSyncPlanResolution.Update when item.Reason == DataSyncPlanItemReason.IdentityConflict:
            case DataSyncPlanResolution.Link:
                candidate = item.Candidates.FirstOrDefault(c => d.TargetLocalKey is not null && c.LocalKey == d.TargetLocalKey);
                if (candidate is null) return Fail(DataSyncDecisionErrorCode.TargetNotAllowed);
                break;
            default:
                return Fail(DataSyncDecisionErrorCode.TargetNotAllowed);
        }

        if (d.ReviewToken != (candidate?.ReviewToken ?? item.ReviewToken))
            return Fail(DataSyncDecisionErrorCode.ChangedSinceReview);

        var changes = candidate?.Changes ?? item.Changes;
        if (!TryExclude(d.ExcludedChangeIds ?? [], changes, out var excluded))
            return Fail(DataSyncDecisionErrorCode.UnknownChange);

        if (d.Resolution == DataSyncPlanResolution.CreateSeparate && d.NewName is { } name && !IsValidName(name))
            return Fail(DataSyncDecisionErrorCode.InvalidName);

        return new Verdict(item, d, null, candidate, changes, excluded, false);
    }

    /// <summary>
    /// v3.1 §7.4: concrete ids must be changes of the item (or chosen candidate); a group id <c>"&lt;prefix&gt;:*"</c>
    /// expands to every change with that prefix, beyond the inline ones too. A prefix is known when the list has such
    /// a change or it is one of v3.1's groups. The set is then closed over <c>DependsOnChangeId</c>.
    /// </summary>
    private static bool TryExclude(IReadOnlyList<string> ids, IReadOnlyList<DataSyncFieldChange> changes,
        out IReadOnlySet<string> excluded)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        excluded = set;
        var all = changes.Select(c => c.ChangeId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (id is null) return false;
            if (id.EndsWith(":*", StringComparison.Ordinal) && id.Length > 2)
            {
                var prefix = id[..^2];
                var group = changes.Where(c => c.ChangeId.StartsWith(prefix + ":", StringComparison.Ordinal))
                    .Select(c => c.ChangeId).ToList();
                if (group.Count == 0 && !DataSyncPlanFormat.WellKnownGroupPrefixes.Contains(prefix)) return false;
                set.UnionWith(group);
            }
            else if (all.Contains(id))
            {
                set.Add(id);
            }
            else
            {
                return false;
            }
        }

        // Excluding an add excludes every add that depends on it, transitively.
        var dependents = changes.Where(c => c.DependsOnChangeId is not null)
            .ToLookup(c => c.DependsOnChangeId!, c => c.ChangeId, StringComparer.Ordinal);
        var queue = new Queue<string>(set);
        while (queue.Count > 0)
        {
            foreach (var child in dependents[queue.Dequeue()])
            {
                if (set.Add(child)) queue.Enqueue(child);
            }
        }

        return true;
    }

    /// <summary>1..MaxNameLength characters, no U+0000, no unpaired surrogate.</summary>
    internal static bool IsValidName(string name)
    {
        if (name.Length == 0 || name.Length > MaxNameLength) return false;
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (c == '\0') return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= name.Length || !char.IsLowSurrogate(name[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<DataSyncDecisionError> Errors()
    {
        var errors = _verdicts.Where(v => v.Failure is not null)
            .Select(v => new DataSyncDecisionError(v.Item.ItemId, v.Failure!.Value)).ToList();
        errors.AddRange(_decisionErrors);
        return errors;
    }

    // ---- operations ------------------------------------------------------------------------------

    private IReadOnlyList<ResolvedItem> Build(DataSyncPlanInput input)
    {
        var incoming = new Dictionary<string, DataSyncReviewIncoming>(StringComparer.Ordinal);
        var locals = new Dictionary<string, DataSyncReviewLocal>(StringComparer.Ordinal);
        foreach (var staged in input.Incoming.Kinds.GroupBy(k => k.Kind, StringComparer.Ordinal).Select(g => g.First()))
        {
            var codec = input.Codecs.GetValueOrDefault(staged.Kind);
            foreach (var r in DataSyncReviewIncoming.Of(staged, codec)) incoming.TryAdd(r.ItemId, r);
            locals[staged.Kind] = new DataSyncReviewLocal(input.Local.GetValueOrDefault(staged.Kind),
                input.LocalState.GetValueOrDefault(staged.Kind), null);
        }

        return _verdicts.Select(v => Build(v, input, incoming, locals)).ToList();
    }

    private ResolvedItem Build(Verdict v, DataSyncPlanInput input, IReadOnlyDictionary<string, DataSyncReviewIncoming> incoming,
        IReadOnlyDictionary<string, DataSyncReviewLocal> locals)
    {
        var item = v.Item;
        if (v.Held) return new ResolvedItem(item.ItemId, item.Kind, DataSyncItemOutcome.Held, DataSyncItemAction.None, null);
        if (v.Failure is { } failure) return Unapplied(item, DetailOf(failure));

        var d = v.Decision!;
        if (d.Resolution == DataSyncPlanResolution.Skip)
            return new ResolvedItem(item.ItemId, item.Kind, DataSyncItemOutcome.SkippedByUser, DataSyncItemAction.None, null);

        // The plan and the input it was built from always agree; a mismatch is handled like a changed item.
        if (!incoming.TryGetValue(item.ItemId, out var r) || r.Entity.Content is not { } content || r.Keys is null ||
            !input.Codecs.TryGetValue(item.Kind, out var codec) || !locals.TryGetValue(item.Kind, out var local))
            return Unapplied(item, "hashMismatch");

        if (d.Resolution is DataSyncPlanResolution.Create or DataSyncPlanResolution.CreateSeparate)
        {
            // CreateSeparate keeps the incoming keys only when none of them is bound here; else a fresh key (§5.1).
            var keys = d.Resolution == DataSyncPlanResolution.CreateSeparate && r.Keys.Any(local.IsBound)
                ? EntityKeys.None
                : new EntityKeys(r.Keys);
            var created = codec.PrepareCreate(content,
                d.Resolution == DataSyncPlanResolution.CreateSeparate ? d.NewName : null);
            var operation = new CreateEntityOperation(item.ItemId, keys, r.Entity.Record.Origin, item.Incoming.Position,
                codec.Write(created.Content));
            return new ResolvedItem(item.ItemId, item.Kind, DataSyncItemOutcome.Applied, DataSyncItemAction.Created,
                operation, ChildMap: created.ChildIdMap);
        }

        // Update or Link.
        var target = local.ByLocalKey(d.TargetLocalKey);
        if (target is null || target.Unreadable) return Unapplied(item, "hashMismatch");

        var accepted = v.Changes.Select(c => c.ChangeId).Where(id => !v.Excluded.Contains(id))
            .ToHashSet(StringComparer.Ordinal);
        var merge = codec.Merge(target.Content, content, accepted);
        var aliases = local.AliasKeysFor(r.Keys, target);
        var link = d.Resolution == DataSyncPlanResolution.Link;

        ApplyOperation? op = null;
        var action = DataSyncItemAction.None;
        if (item.Type != DataSyncPlanItemType.Unchanged)
        {
            var merged = codec.Write(merge.Content);
            if (ContentHash.Of(merged) != target.ContentHash)
            {
                op = new UpdateEntityOperation(item.ItemId, target.LocalKey, target.ContentHash, merged,
                    aliases.Count == 0 ? EntityKeys.None : new EntityKeys(aliases), merge.AddedChildIds, []);
                action = link ? DataSyncItemAction.Linked : DataSyncItemAction.Updated;
            }
        }

        // Unchanged + Update never writes content: at most it records keys (v3.1 §7.7).
        if (op is null && aliases.Count > 0)
        {
            op = new BindOnlyOperation(item.ItemId, target.LocalKey, new EntityKeys(aliases));
            action = link ? DataSyncItemAction.Linked : DataSyncItemAction.KeysRecorded;
        }

        return new ResolvedItem(item.ItemId, item.Kind, op is null ? DataSyncItemOutcome.NoChange : DataSyncItemOutcome.Applied,
            action, op, TargetLocalKey: target.LocalKey, ChildMap: merge.ChildIdMap);
    }

    private static ResolvedItem Unapplied(DataSyncPlanItem item, string detail) =>
        new(item.ItemId, item.Kind, DataSyncItemOutcome.ChangedSinceReview, DataSyncItemAction.None, null, detail);

    private static string DetailOf(DataSyncDecisionErrorCode code) => code switch
    {
        DataSyncDecisionErrorCode.DecisionMissing => "decisionMissing",
        DataSyncDecisionErrorCode.ResolutionNotAllowed => "resolutionNotAllowed",
        DataSyncDecisionErrorCode.TargetNotAllowed => "targetNotAllowed",
        DataSyncDecisionErrorCode.TargetUsedTwice => "targetUsedTwice",
        DataSyncDecisionErrorCode.ChangedSinceReview => "tokenMismatch",
        DataSyncDecisionErrorCode.UnknownChange => "unknownChange",
        DataSyncDecisionErrorCode.InvalidName => "invalidName",
        _ => code.ToString(),
    };

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();
}
