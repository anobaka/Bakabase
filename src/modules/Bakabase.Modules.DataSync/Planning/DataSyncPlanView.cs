using Bakabase.Modules.DataSync.Services;

namespace Bakabase.Modules.DataSync.Planning;

/// <summary>What HTTP sees: the full plan cut to the inline caps, and pages of the rest (v3.1 §7.8).</summary>
/// <remarks>
/// Scalar changes (name, flags, settings, defaultValue) are always inline. Child changes fill each item and candidate
/// up to <see cref="MaxInline"/> changes, from a plan-wide budget of <see cref="MaxInlineChildChanges"/> spent in plan
/// order (sections, items, then each item's candidates). Warnings stay inline when their change does, plus up to
/// <see cref="MaxInline"/> item-level ones. Counts always describe the full lists.
/// </remarks>
public static class DataSyncPlanView
{
    public const int MaxInline = 200;                 // per item and per candidate
    public const int MaxInlineChildChanges = 20_000;  // whole plan, option/extension changes only
    public const int MaxPageSize = 500;

    public static DataSyncPlan Truncate(DataSyncPlan full)
    {
        ArgumentNullException.ThrowIfNull(full);
        var budget = MaxInlineChildChanges;
        var sections = new List<DataSyncPlanKindSection>(full.Kinds.Count);
        foreach (var section in full.Kinds)
        {
            var items = new List<DataSyncPlanItem>(section.Items.Count);
            foreach (var item in section.Items)
            {
                var (changes, changesCut) = Cut(item.Changes, ref budget);
                var (warnings, warningsCut) = Cut(item.Warnings, item.Changes, changes);
                var candidates = new List<DataSyncPlanCandidate>(item.Candidates.Count);
                foreach (var candidate in item.Candidates)
                {
                    var (cChanges, cChangesCut) = Cut(candidate.Changes, ref budget);
                    var (cWarnings, cWarningsCut) = Cut(candidate.Warnings, candidate.Changes, cChanges);
                    candidates.Add(candidate with
                    {
                        Changes = cChanges, ChangesTruncated = candidate.ChangesTruncated || cChangesCut,
                        Warnings = cWarnings, WarningsTruncated = candidate.WarningsTruncated || cWarningsCut,
                    });
                }

                items.Add(item with
                {
                    Changes = changes, ChangesTruncated = item.ChangesTruncated || changesCut,
                    Warnings = warnings, WarningsTruncated = item.WarningsTruncated || warningsCut,
                    Candidates = candidates,
                });
            }

            sections.Add(section with { Items = items });
        }

        return full with { Kinds = sections };
    }

    /// <summary>
    /// The changes <c>[skip, skip + take)</c> of one item (<paramref name="candidateLocalKey"/> null) or one of its
    /// candidates, over the full ordered list (so <c>skip = 0</c> repeats the inline ones), with the warnings of those
    /// changes. <paramref name="take"/> is clamped to 0..<see cref="MaxPageSize"/>. An unknown item or candidate
    /// answers <see cref="DataSyncProblemCode.UnknownItem"/>.
    /// </summary>
    public static DataSyncChangePage Page(DataSyncPlan full, string itemId, string? candidateLocalKey,
        int skip, int take)
    {
        ArgumentNullException.ThrowIfNull(full);
        var item = full.Kinds.SelectMany(s => s.Items).FirstOrDefault(i => i.ItemId == itemId);
        if (item is null) return Unknown(full, "item");

        IReadOnlyList<DataSyncFieldChange> changes = item.Changes;
        IReadOnlyList<DataSyncPlanWarning> warnings = item.Warnings;
        if (candidateLocalKey is not null)
        {
            var candidate = item.Candidates.FirstOrDefault(c => c.LocalKey == candidateLocalKey);
            if (candidate is null) return Unknown(full, "candidate");
            changes = candidate.Changes;
            warnings = candidate.Warnings;
        }

        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 0, MaxPageSize);
        var page = changes.Skip(skip).Take(take).ToList();
        var ids = page.Select(c => c.ChangeId).ToHashSet(StringComparer.Ordinal);
        return new DataSyncChangePage(full.PlanId, page,
            warnings.Where(w => w.ChangeId is not null && ids.Contains(w.ChangeId)).ToList(), changes.Count, null);
    }

    /// <summary>
    /// The answer to an apply whose decisions failed the strict check (<see cref="DataSyncPlanner.ValidateDecisions"/>):
    /// <see cref="DataSyncProblemCode.DecisionsInvalid"/>, the errors, and the fresh plan, truncated (§10.1).
    /// </summary>
    public static DataSyncApplyStart RejectDecisions(DataSyncPlan full, IReadOnlyList<DataSyncDecisionError> errors)
    {
        ArgumentNullException.ThrowIfNull(full);
        ArgumentNullException.ThrowIfNull(errors);
        return new DataSyncApplyStart(null, new DataSyncProblem(DataSyncProblemCode.DecisionsInvalid, null), errors,
            Truncate(full));
    }

    /// <summary>Every scalar, then child changes up to <see cref="MaxInline"/> in all and while the budget lasts.</summary>
    private static (IReadOnlyList<DataSyncFieldChange> Changes, bool Cut) Cut(IReadOnlyList<DataSyncFieldChange> changes,
        ref int budget)
    {
        var scalars = changes.Count(c => !DataSyncPlanFormat.IsChild(c));
        var room = Math.Min(Math.Max(0, MaxInline - scalars), budget);
        var inline = new List<DataSyncFieldChange>(Math.Min(changes.Count, scalars + room));
        foreach (var change in changes)
        {
            if (!DataSyncPlanFormat.IsChild(change))
            {
                inline.Add(change);
            }
            else if (room > 0)
            {
                inline.Add(change);
                room--;
                budget--;
            }
        }

        return (inline, inline.Count < changes.Count);
    }

    /// <summary>
    /// The warnings of inline changes, plus up to <see cref="MaxInline"/> item-level ones (no change id, or one no
    /// change carries), in their order.
    /// </summary>
    private static (IReadOnlyList<DataSyncPlanWarning> Warnings, bool Cut) Cut(IReadOnlyList<DataSyncPlanWarning> warnings,
        IReadOnlyList<DataSyncFieldChange> all, IReadOnlyList<DataSyncFieldChange> inline)
    {
        var allIds = all.Select(c => c.ChangeId).ToHashSet(StringComparer.Ordinal);
        var inlineIds = inline.Select(c => c.ChangeId).ToHashSet(StringComparer.Ordinal);
        var kept = new List<DataSyncPlanWarning>();
        var itemLevel = 0;
        foreach (var warning in warnings)
        {
            if (warning.ChangeId is not null && allIds.Contains(warning.ChangeId))
            {
                if (inlineIds.Contains(warning.ChangeId)) kept.Add(warning);
            }
            else if (itemLevel < MaxInline)
            {
                kept.Add(warning);
                itemLevel++;
            }
        }

        return (kept, kept.Count < warnings.Count);
    }

    private static DataSyncChangePage Unknown(DataSyncPlan full, string what) =>
        new(full.PlanId, [], [], 0, new DataSyncProblem(DataSyncProblemCode.UnknownItem, what));
}
