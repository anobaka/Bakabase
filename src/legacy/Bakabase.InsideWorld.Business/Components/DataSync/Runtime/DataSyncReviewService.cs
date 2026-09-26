using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Models.Db;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// The first-link review and copy once as the facade serves them (§8.3, §8.10.3, v3.1 §7–§8): read-only re-plans of
/// a staged pull against this device's last committed definitions, "Fetch again", the change pages, and the apply's
/// boundary — strict validation of the decisions under the gate, then <c>DataSyncReview:{reviewId}</c>.
/// </summary>
public sealed class DataSyncReviewService
{
    private readonly IServiceProvider _services;
    private readonly IDataSyncReviewStore _reviews;

    /// <param name="services">A scope.</param>
    public DataSyncReviewService(IServiceProvider services)
    {
        _services = services;
        _reviews = services.GetRequiredService<IDataSyncReviewStore>();
    }

    private IDataSyncStore Store => _services.GetRequiredService<IDataSyncStore>();
    private DataSyncTaskLauncher Launcher => _services.GetRequiredService<DataSyncTaskLauncher>();

    /// <summary>
    /// A read-only re-plan (§8.3 step 4): against the last committed local state, without Refresh and without any
    /// write; the apply's in-transaction re-plan catches anything newer. Never gated.
    /// </summary>
    public async Task<DataSyncReviewResult> GetAsync(string reviewId, CancellationToken ct)
    {
        var entry = _reviews.Get(reviewId);
        if (entry is null) return Expired(reviewId);
        var plan = await PlanAsync(entry, ct);
        return await ToResultAsync(entry, DataSyncPlanView.Truncate(plan), null, ct);
    }

    /// <summary>
    /// "Fetch again" (§8.3): the only way to replace a staged review. It fetches a fresh snapshot for the review's
    /// link under the per-peer fetch lock (§7.6), without the gate, and answers the review that fetch staged.
    /// </summary>
    public async Task<DataSyncReviewResult> RefetchAsync(string reviewId, CancellationToken ct)
    {
        var entry = _reviews.Get(reviewId);
        if (entry is null) return Expired(reviewId);
        if (StateOf(entry, out _) is DataSyncReviewState.Applying)
            return await ToResultAsync(entry, null, new DataSyncProblem(DataSyncProblemCode.ApplyInProgress, null), ct);
        if (entry.LinkId is not { } linkId)
            return await ToResultAsync(entry, null, new DataSyncProblem(DataSyncProblemCode.NothingToReview, null), ct);

        var links = _services.GetRequiredService<DataSyncLinkService>();
        if (await links.GetAsync(linkId, ct) is null) return Refused(entry, DataSyncProblemCode.LinkNotFound, null);
        _reviews.Discard(reviewId);
        var link = await links.MutateAsync(linkId, row =>
        {
            if (row.ReviewId != reviewId) return DataSyncLinkWrite.None;
            row.ReviewId = null;
            return DataSyncLinkWrite.Bookkeeping;
        }, ct);
        if (link is null) return Refused(entry, DataSyncProblemCode.LinkNotFound, null);

        var local = await Store.GetLocalStateAsync(ct);
        await _services.GetRequiredService<DataSyncFetcher>().FetchLinkAsync(link, false, local, ct);

        var staged = _reviews.GetForLink(linkId);
        if (staged is not null) return await GetAsync(staged.ReviewId, ct);
        var after = await links.GetAsync(linkId, ct);
        var problem = after?.LastErrorCode is { } code
            ? new DataSyncProblem(Enum.TryParse<DataSyncPeerErrorCode>(code, out var peerCode)
                ? DataSyncLinkService.ProblemOf(peerCode)
                : DataSyncProblemCode.PeerUnreachable, code)
            : new DataSyncProblem(DataSyncProblemCode.NothingToReview, null);
        return Refused(entry, problem.Code, problem.Detail);
    }

    /// <summary>One plan item's changes beyond the inline cap (v3.1 §9.3); never gated.</summary>
    public async Task<DataSyncChangePage> GetChangesAsync(string reviewId, string planId, string itemId,
        string? candidateLocalKey, int skip, int take, CancellationToken ct)
    {
        var entry = _reviews.Get(reviewId);
        if (entry is null)
            return new DataSyncChangePage(planId, [], [], 0,
                new DataSyncProblem(DataSyncProblemCode.ReviewExpired, null));
        var plan = entry.LastPlan ?? await PlanAsync(entry, ct);
        if (!string.Equals(plan.PlanId, planId, StringComparison.Ordinal))
            return new DataSyncChangePage(planId, [], [], 0,
                new DataSyncProblem(DataSyncProblemCode.PlanChanged, plan.PlanId));
        return DataSyncPlanView.Page(plan, itemId, candidateLocalKey, skip, take);
    }

    /// <summary>
    /// The apply's boundary (§8.10.3), under the gate the caller holds: refused while any data sync write task waits
    /// or runs (<c>ApplyInProgress</c>); a strict <c>Resolve</c> against a fresh plan answers every problem as a
    /// decision error with that plan; otherwise the completed decisions go to <c>DataSyncReview:{reviewId}</c>.
    /// </summary>
    public async Task<DataSyncApplyStart> ApplyAsync(string reviewId, DataSyncReviewApplyInput input,
        CancellationToken ct)
    {
        var entry = _reviews.Get(reviewId);
        if (entry is null) return Refused(DataSyncProblemCode.ReviewExpired, null);
        switch (StateOf(entry, out _))
        {
            case DataSyncReviewState.Applying:
                return Refused(DataSyncProblemCode.ApplyInProgress, null);
            case DataSyncReviewState.Applied:
                return Refused(DataSyncProblemCode.NothingToReview, "applied");
        }

        var launcher = Launcher;
        if (launcher.IsWriteTaskActiveOrPending())
            return Refused(DataSyncProblemCode.ApplyInProgress, launcher.GetActiveWriteTaskId());

        var (plan, planInput) = await PlanWithInputsAsync(entry, ct);
        var decisions = input.Decisions ?? [];
        // Strict, over the input the plan was built from, so the alias rule knows the rows kept out of sync (§5.3).
        var resolved = DataSyncPlanner.Resolve(plan, planInput, decisions, strict: true);
        if (resolved.Errors.Count > 0)
            return new DataSyncApplyStart(null, new DataSyncProblem(DataSyncProblemCode.DecisionsInvalid, null),
                resolved.Errors, DataSyncPlanView.Truncate(plan));

        var complete = DataSyncPlanner.CompleteDecisions(plan, decisions);
        var reviews = _reviews;
        var attempt = await launcher.EnqueueReviewAsync(reviewId, complete,
            new DataSyncApplyOptions(input.BackupBeforeDestructive),
            logId =>
            {
                if (logId is { } id) reviews.MarkApplied(reviewId, id);
                return Task.CompletedTask;
            }, entry.LinkId);
        if (attempt is null) return Refused(DataSyncProblemCode.ApplyInProgress, null);
        _reviews.MarkApplying(reviewId, attempt.TaskId);
        return new DataSyncApplyStart(attempt.TaskId, null, [], null);
    }

    /// <summary>
    /// Calls off a review's apply (v3.1 B2), never gated: a waiting task is removed and the review stays staged; a
    /// running one rolls back its current chunk.
    /// </summary>
    public async Task<DataSyncReviewCancelResult> CancelApplyAsync(string reviewId, CancellationToken ct)
    {
        var entry = _reviews.Get(reviewId);
        if (entry is null)
            return new DataSyncReviewCancelResult(null, new DataSyncProblem(DataSyncProblemCode.ReviewExpired, null));
        if (entry.TaskId is not { } taskId) return new DataSyncReviewCancelResult(DataSyncReviewState.Staged, null);
        var outcome = await Launcher.CancelAsync(taskId);
        // A waiting task was removed, so its body never runs to say the apply ended: the review is not applying any
        // more, and expires and can be replaced again.
        if (outcome == DataSyncTaskCancelOutcome.Removed) _reviews.MarkApplyEnded(reviewId);
        var state = outcome switch
        {
            DataSyncTaskCancelOutcome.Removed => DataSyncReviewState.Staged,
            DataSyncTaskCancelOutcome.Stopping => DataSyncReviewState.Applying,
            _ => StateOf(_reviews.Get(reviewId) ?? entry, out _),
        };
        return new DataSyncReviewCancelResult(state, null);
    }

    /// <summary>Drops a staged review; never one being applied. The link's next cycle stages a fresh one (§8.3).</summary>
    public async Task DiscardAsync(string reviewId, CancellationToken ct)
    {
        var entry = _reviews.Get(reviewId);
        if (entry is null || StateOf(entry, out _) == DataSyncReviewState.Applying) return;
        _reviews.Discard(reviewId);
        if (entry.LinkId is { } linkId)
        {
            await _services.GetRequiredService<DataSyncLinkService>().MutateAsync(linkId, row =>
            {
                if (row.ReviewId != reviewId) return DataSyncLinkWrite.None;
                row.ReviewId = null;
                return DataSyncLinkWrite.Bookkeeping;
            }, ct);
        }
    }

    // ---- planning ----------------------------------------------------------------------------------------------

    private async Task<DataSyncPlan> PlanAsync(DataSyncReviewEntry entry, CancellationToken ct) =>
        (await PlanWithInputsAsync(entry, ct)).Plan;

    /// <summary>
    /// The full plan of a staged pull against the last committed local state (read-only, §8.3), as the apply's
    /// in-transaction re-plan builds it: with the mode the link will have (<c>Off</c> for copy once), so a two-way
    /// link's separate names say they are used everywhere, and with the rename rows' reach filled in.
    /// </summary>
    private async Task<(DataSyncPlan Plan, DataSyncPlanInput Input)> PlanWithInputsAsync(DataSyncReviewEntry entry,
        CancellationToken ct)
    {
        var kinds = entry.Pull.Kinds.Select(k => k.Kind).Distinct(StringComparer.Ordinal).ToList();
        var state = await _services.GetRequiredService<IDataSyncLocalStateReader>().ReadAsync(kinds, ct);
        var adapters = Adapters();
        var codecs = adapters.ToDictionary(p => p.Key, p => p.Value.Codec, StringComparer.Ordinal);
        var device = await _services.GetRequiredService<IDataSyncDeviceIdentity>().GetAsync(ct);
        var link = entry.LinkId is { } linkId ? await Store.GetLinkAsync(linkId, ct) : null;
        var mode = entry.CopyOnce ? DataSyncLinkMode.Off : link?.Mode ?? DataSyncLinkMode.Off;
        var input = DataSyncPlanInput.FromLocalState(entry.Pull, state, codecs, device.NodeId, mode);
        var plan = await WithInUseCountsAsync(DataSyncPlanner.Plan(input), adapters, ct);
        _reviews.SetLastPlan(entry.ReviewId, plan);
        return (plan, input);
    }

    private IReadOnlyDictionary<string, IDataSyncKind> Adapters() =>
        (_services.GetService<IEnumerable<IDataSyncKind>>() ?? [])
        .GroupBy(k => k.Codec.Descriptor.Kind, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>Rename rows beyond this many for one entity keep <c>InUseCount</c> null (v3.1 §7.4, M9).</summary>
    internal const int MaxRenameRowsWithReach = 500;

    /// <summary>
    /// After planning (v3.1 §7.4, M9): every rename row gets <c>InUseCount</c>, the resources whose value references
    /// the local option, read from the adapter's usage — for entities with at most
    /// <see cref="MaxRenameRowsWithReach"/> rename rows. A rename row names the option by the id both sides share
    /// (<c>{prefix}:rename:{uuid}</c>: a child is renamed only when its uuid is found locally). Enrichment only: no token
    /// and not the <see cref="DataSyncPlan.PlanId"/> depend on it, and it reads without writing.
    /// </summary>
    private static async Task<DataSyncPlan> WithInUseCountsAsync(DataSyncPlan plan,
        IReadOnlyDictionary<string, IDataSyncKind> adapters, CancellationToken ct)
    {
        static bool IsRename(DataSyncFieldChange c) => c.Kind == DataSyncFieldChangeKind.RenameChild;
        static string ChildIdOf(DataSyncFieldChange c) => c.ChangeId[(c.ChangeId.LastIndexOf(':') + 1)..];

        var sections = new List<DataSyncPlanKindSection>(plan.Kinds.Count);
        foreach (var section in plan.Kinds)
        {
            var queries = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

            void Collect(string? localKey, IReadOnlyList<DataSyncFieldChange> changes)
            {
                if (localKey is null) return;
                var renames = changes.Where(IsRename).ToList();
                if (renames.Count is 0 or > MaxRenameRowsWithReach) return;
                if (!queries.TryGetValue(localKey, out var ids))
                    queries[localKey] = ids = new HashSet<string>(StringComparer.Ordinal);
                ids.UnionWith(renames.Select(ChildIdOf));
            }

            foreach (var item in section.Items)
            {
                Collect(item.Local?.LocalKey, item.Changes);
                foreach (var candidate in item.Candidates) Collect(candidate.LocalKey, candidate.Changes);
            }

            if (queries.Count == 0 || !adapters.TryGetValue(section.Kind, out var adapter))
            {
                sections.Add(section);
                continue;
            }

            var usage = await adapter.GetUsageAsync(
                queries.ToDictionary(q => q.Key, q => (IReadOnlyCollection<string>) q.Value, StringComparer.Ordinal), ct);

            IReadOnlyList<DataSyncFieldChange> Fill(string? localKey, IReadOnlyList<DataSyncFieldChange> changes) =>
                localKey is null || !queries.ContainsKey(localKey)
                    ? changes
                    : changes.Select(c => IsRename(c)
                        ? c with
                        {
                            InUseCount = usage.GetValueOrDefault(localKey)?.ResourceCountByChildId
                                .GetValueOrDefault(ChildIdOf(c)) ?? 0,
                        }
                        : c).ToList();

            sections.Add(section with
            {
                Items = section.Items.Select(item => item with
                {
                    Changes = Fill(item.Local?.LocalKey, item.Changes),
                    Candidates = item.Candidates
                        .Select(candidate => candidate with { Changes = Fill(candidate.LocalKey, candidate.Changes) })
                        .ToList(),
                }).ToList(),
            });
        }

        return plan with { Kinds = sections };
    }

    // ---- results -----------------------------------------------------------------------------------------------

    /// <summary>
    /// Where a review stands: applied once its task logged it or completed, failed when its task ended in an error,
    /// applying while its task waits or runs, and staged otherwise (a cancelled task leaves it staged).
    /// </summary>
    private DataSyncReviewState StateOf(DataSyncReviewEntry entry, out string? error)
    {
        error = null;
        if (entry.ApplyLogId is not null) return DataSyncReviewState.Applied;
        if (entry.TaskId is not { } taskId) return DataSyncReviewState.Staged;
        var task = _services.GetRequiredService<BTaskManager>().GetTaskViewModel(taskId);
        switch (task?.Status)
        {
            case null:
            case BTaskStatus.Cancelled:
                return DataSyncReviewState.Staged;
            case BTaskStatus.Completed:
                return DataSyncReviewState.Applied;
            case BTaskStatus.Error:
                error = task.BriefError ?? task.Error;
                return DataSyncReviewState.Failed;
            default:
                return DataSyncReviewState.Applying;
        }
    }

    private async Task<DataSyncReviewResult> ToResultAsync(DataSyncReviewEntry entry, DataSyncPlan? plan,
        DataSyncProblem? problem, CancellationToken ct)
    {
        var state = StateOf(entry, out var error);
        var link = entry.LinkId is { } linkId ? await Store.GetLinkAsync(linkId, ct) : null;
        var manifest = entry.Pull.Manifest;
        var source = new DataSyncReviewSource(entry.Pull.PeerNodeId, entry.Pull.PeerName, manifest.AppVersion,
            DataSyncViews.Utc(entry.Pull.FetchedAtUtc),
            entry.Pull.Kinds.Select(k => new DataSyncKindCount(k.Kind, k.Entities.Count)).ToList());
        return new DataSyncReviewResult(entry.ReviewId, entry.LinkId, entry.CopyOnce,
            link?.Mode ?? DataSyncLinkMode.Off, state, source, plan, entry.ApplyLogId, entry.TaskId, error, problem);
    }

    private static DataSyncReviewResult Expired(string reviewId) =>
        new(reviewId, null, false, DataSyncLinkMode.Off, null, null, null, null, null, null,
            new DataSyncProblem(DataSyncProblemCode.ReviewExpired, null));

    private static DataSyncReviewResult Refused(DataSyncReviewEntry entry, DataSyncProblemCode code, string? detail) =>
        new(null, entry.LinkId, entry.CopyOnce, DataSyncLinkMode.Off, null, null, null, null, null, null,
            new DataSyncProblem(code, detail));

    private static DataSyncApplyStart Refused(DataSyncProblemCode code, string? detail) =>
        new(null, new DataSyncProblem(code, detail), [], null);
}
