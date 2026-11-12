using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.DownloadIntents;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed class AcquisitionSourceRevisionPreviewStore
{
    internal sealed record Entry(AcquisitionSourceRevisionPreview Preview, string Version,
        AcquisitionSourceRevisionCandidate Candidate, AcquisitionPlan Plan, IReadOnlyList<WorkflowSourceRef> SourceRefs, int DownloadTaskId)
    { internal bool Claimed; }
    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    internal void Add(Entry entry)
    {
        foreach (var pair in _entries) if (pair.Value.Preview.ExpiresAt <= DateTimeOffset.UtcNow) _entries.TryRemove(pair.Key, out _);
        if (_entries.Count >= 100) throw new InvalidOperationException("SOURCE_REVISION_PREVIEW_LIMIT");
        _entries[entry.Preview.PreviewToken] = entry;
    }
    internal Entry Claim(int taskId, string token)
    {
        if (!Guid.TryParseExact(token, "N", out _) || !_entries.TryGetValue(token, out var entry) ||
            entry.Preview.TaskId != taskId || entry.Preview.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("SOURCE_REVISION_PREVIEW_EXPIRED");
        lock (entry)
        {
            if (entry.Claimed) throw new InvalidOperationException("SOURCE_REVISION_OUTCOME_UNKNOWN");
            if (!entry.Preview.CanApply) throw new InvalidOperationException(entry.Preview.ReasonCode);
            entry.Claimed = true;
            return entry;
        }
    }
}

/// <summary>Explicit source changes produce a new task-local plan, never a rewritten old checkpoint.
/// Initial support is deliberately bounded to a single part with a proven empty failed download.</summary>
public sealed class AcquisitionSourceRevisionService(BakabaseDbContext db,
    IEnumerable<IAcquisitionSourceRevisionCandidates> candidateProviders, IAcquisitionSourceRevisionSafety safety,
    DownloadIntentService downloads, AcquisitionSourceRevisionPreviewStore previews, DownloaderManager downloader,
    BTaskManager? tasks = null)
{
    public const string RevisionReason = "sourceSelection.userRevision";
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;
    private sealed record State(AcquisitionTaskDbModel Task, WorkflowRunDbModel? Run, AcquisitionPlan? Plan,
        DownloadWorkset? Workset, IReadOnlyList<WorkflowSourceRef> Sources, string? Problem, string Version);

    public async Task<AcquisitionSourceRevisionOptions> GetAsync(int id, CancellationToken ct = default)
    {
        var state = await ReadAsync(id, ct);
        var current = state.Plan?.Parts.FirstOrDefault()?.Sources.FirstOrDefault(s => s.Id == state.Plan.Parts[0].SelectedSourceId);
        var candidates = state.Plan == null ? [] : await CandidatesAsync(state, ct);
        var reason = state.Problem;
        if (reason == null)
        {
            var proof = await safety.CheckAsync(state.Run!.Id, state.Workset!.Targets.Single(), state.Workset.OutputDirectory, ct);
            if (!proof.Safe) reason = proof.ReasonCode ?? "SOURCE_REVISION_OPERATION_REVIEW_REQUIRED";
        }
        return new(id, state.Task.PlanRevision, state.Task.WorkflowRunId, reason == null && candidates.Count > 0,
            reason ?? (candidates.Count == 0 ? "SOURCE_REVISION_NO_MIRRORS" : null), current, candidates);
    }

    public async Task<AcquisitionSourceRevisionPreview> PreviewAsync(int id, AcquisitionSourceRevisionPreviewInput input, CancellationToken ct = default)
    {
        var state = await ReadAsync(id, ct);
        RequireExpected(state, input.ExpectedRevision, input.ExpectedRunId);
        if (state.Problem != null) throw new InvalidOperationException(state.Problem);
        var candidate = (await CandidatesAsync(state, ct)).SingleOrDefault(c => c.Id == input.CandidateId && c.PartId == input.PartId)
            ?? throw new ArgumentException("SOURCE_REVISION_CANDIDATE_CHANGED");
        var proof = await safety.CheckAsync(state.Run!.Id, state.Workset!.Targets.Single(), state.Workset.OutputDirectory, ct);
        var check = await CheckSourceAsync(state.Plan!, candidate.Source, id, ct);
        var proposed = Freeze(candidate.Source, check);
        var currentPart = state.Plan!.Parts.Single();
        var current = currentPart.Sources.Single(s => s.Id == currentPart.SelectedSourceId);
        var newPlan = RevisedPlan(state.Plan, candidate.Source, proposed);
        AcquisitionPlanCompiler.Validate(newPlan);
        var reason = !proof.Safe ? proof.ReasonCode ?? "SOURCE_REVISION_OPERATION_REVIEW_REQUIRED" :
            !check.CanSubmit ? "SOURCE_REVISION_SOURCE_NOT_READY" : null;
        var preview = new AcquisitionSourceRevisionPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            id, state.Task.PlanRevision, state.Run.Id, currentPart.Id, reason == null, reason, current, proposed,
            currentPart.Processing, proposed.Processing, Serialize(currentPart.Processing) != Serialize(proposed.Processing), candidate.Evidence, check);
        var latest = await ReadAsync(id, ct);
        if (latest.Version != state.Version) throw new InvalidOperationException("SOURCE_REVISION_TASK_CHANGED");
        previews.Add(new(preview, Version(state, proof), candidate, newPlan,
            state.Sources.Concat(candidate.SourceRefs).Distinct().ToList(), state.Workset.Targets.Single().DownloadTaskId!.Value));
        return preview;
    }

    public async Task<AcquisitionSourceRevisionResult> ApplyAsync(int id, AcquisitionSourceRevisionApplyInput input,
        CancellationToken ct = default, Func<IReadOnlyList<string>, CancellationToken, Task>? guardDirectories = null)
    {
        if (!Guid.TryParseExact(input.PreviewToken, "N", out _)) throw new ArgumentException("SOURCE_REVISION_PREVIEW_EXPIRED");
        var taskGate = AcquisitionTaskGate.For(id); await taskGate.WaitAsync(ct);
        try
        {
            var saved = await AlreadyApplied(id, input.PreviewToken, ct);
            if (saved != null) return saved;
            var entry = previews.Claim(id, input.PreviewToken);
            // Downloader actions take their task lock before the workflow owner gate. Keep that order.
            return await downloader.WithTaskActionLock(entry.DownloadTaskId, async () =>
            {
                var runGate = WorkflowRunMutationGate.For(entry.Preview.ExpectedRunId); await runGate.WaitAsync(ct);
                try
                {
                    var state = await ReadAsync(id, ct);
                    RequireExpected(state, entry.Preview.ExpectedRevision, entry.Preview.ExpectedRunId);
                    if (state.Problem != null) throw new InvalidOperationException(state.Problem);
                    var candidate = (await CandidatesAsync(state, ct)).SingleOrDefault(c => c.Id == entry.Candidate.Id);
                    if (candidate == null || Serialize(candidate) != Serialize(entry.Candidate))
                        throw new InvalidOperationException("SOURCE_REVISION_CANDIDATE_CHANGED");
                    var proof = await safety.CheckAsync(state.Run!.Id, state.Workset!.Targets.Single(), state.Workset.OutputDirectory, ct);
                    if (!proof.Safe) throw new InvalidOperationException(proof.ReasonCode);
                    if (Version(state, proof) != entry.Version) throw new InvalidOperationException("SOURCE_REVISION_TASK_CHANGED");
                    if (guardDirectories != null) await guardDirectories(Directories(entry.Plan), ct);
                    var check = await CheckSourceAsync(entry.Plan, candidate.Source, id, ct);
                    if (!check.CanSubmit || Serialize(Freeze(candidate.Source, check)) != Serialize(entry.Preview.ProposedSource) ||
                        Serialize(check.Candidates.SingleOrDefault()?.ResolvedSource) != Serialize(entry.Preview.Preflight.Candidates.SingleOrDefault()?.ResolvedSource))
                        throw new InvalidOperationException("SOURCE_REVISION_SOURCE_CHANGED");
                    // Recheck after network probes, then atomically retire the old execution and append the plan.
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    var fresh = await ReadAsync(id, ct);
                    var finalProof = await safety.CheckAsync(state.Run.Id, state.Workset.Targets.Single(), state.Workset.OutputDirectory, ct);
                    if (fresh.Problem != null || !finalProof.Safe || Version(fresh, finalProof) != entry.Version)
                        throw new InvalidOperationException("SOURCE_REVISION_TASK_CHANGED");
                    var freshCandidate = (await CandidatesAsync(fresh, ct)).SingleOrDefault(c => c.Id == entry.Candidate.Id);
                    if (Serialize(freshCandidate) != Serialize(entry.Candidate)) throw new InvalidOperationException("SOURCE_REVISION_CANDIDATE_CHANGED");
                    var row = fresh.Task; var run = fresh.Run!;
                    var history = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(row.PlanHistoryJson ?? "[]", Json) ?? [];
                    if (history.Count >= 100) throw new InvalidOperationException("SOURCE_REVISION_HISTORY_LIMIT");
                    var revision = row.PlanRevision + 1;
                    history.Add(new(revision, entry.Plan, entry.SourceRefs, DateTime.UtcNow, RevisionReason, run.Id, null,
                        new(input.PreviewToken, entry.Preview.PartId, entry.Preview.CurrentSource.Id,
                            entry.Preview.ProposedSource.Id, candidate.Evidence, candidate.EvidenceVersion,
                            SelectedExecutionIdentity(new() {Id = entry.Preview.PartId,
                                SelectedSourceId = entry.Preview.ProposedSource.Id,
                                Sources = [entry.Preview.Preflight.Candidates.Single().ResolvedSource!],
                                AccountId = entry.Preview.Preflight.Candidates.Single().ResolvedSource!.AccountId}, entry.Plan.OutputDirectory))));
                    // Detached rows were compared above; only named fields change. Purchases, old plan snapshots,
                    // node attempts, operation bindings and download rows remain byte-for-byte retained.
                    var changed = await db.AcquisitionTasks.Where(t => t.Id == id && t.PlanRevision == row.PlanRevision &&
                        t.WorkflowRunId == run.Id && t.DeletedAt == null).ExecuteUpdateAsync(u => u
                        .SetProperty(t => t.PlanRevision, revision).SetProperty(t => t.PlanJson, Serialize(entry.Plan))
                        .SetProperty(t => t.PlanHistoryJson, Serialize(history)).SetProperty(t => t.SourceRefsJson, Serialize(entry.SourceRefs))
                        .SetProperty(t => t.WorkflowRunId, (int?)null).SetProperty(t => t.Status, AcquisitionStatus.Waiting)
                        .SetProperty(t => t.Phase, "ready").SetProperty(t => t.WaitReason, (AcquisitionWaitReason?)null)
                        .SetProperty(t => t.Error, (string?)null).SetProperty(t => t.CompletedAt, (DateTime?)null)
                        .SetProperty(t => t.UpdatedAt, DateTime.Now), ct);
                    if (changed != 1) throw new InvalidOperationException("SOURCE_REVISION_TASK_CHANGED");
                    var retired = await db.WorkflowRuns.Where(r => r.Id == run.Id && r.Status == run.Status && r.PendingSignalJson == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled)
                            .SetProperty(r => r.CompletedAt, run.CompletedAt ?? DateTime.Now), ct);
                    if (retired != 1) throw new InvalidOperationException("SOURCE_REVISION_TASK_CHANGED");
                    await tx.CommitAsync(ct);
                    return new AcquisitionSourceRevisionResult(id, revision, run.Id, true);
                }
                finally { runGate.Release(); }
            });
        }
        finally { taskGate.Release(); }
    }

    private async Task<State> ReadAsync(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new ArgumentException("ACQUISITION_TASK_NOT_FOUND");
        var run = task.WorkflowRunId is {} rid ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == rid, ct) : null;
        AcquisitionPlan? plan = null; DownloadWorkset? workset = null; IReadOnlyList<WorkflowSourceRef> refs = [];
        string? reason = null;
        try
        {
            plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson ?? "null", Json);
            refs = JsonSerializer.Deserialize<List<WorkflowSourceRef>>(task.SourceRefsJson ?? "[]", Json) ?? [];
            workset = AcquisitionEffectiveSourceProjection.Read(run?.CurrentItemJson);
            var definition = WorkflowRunPlans.Read(run?.PlanSnapshotJson);
            if (plan?.Parts.Count != 1 || workset?.Targets.Count != 1 || plan.Parts[0].ContinueFrom != null ||
                workset.Targets[0].Id != plan.Parts[0].Id || workset.Targets[0].DownloadTaskId == null ||
                workset.Targets[0].SelectedSource?.Id != plan.Parts[0].SelectedSourceId ||
                definition?.TriggerKind != CloudWorkflow.Trigger || definition.Nodes.ElementAtOrDefault(run?.CurrentStepIndex ?? -1)?.Kind != "download.awaitCompletion")
                reason = "SOURCE_REVISION_CHECKPOINT_UNSUPPORTED";
            if (reason == null)
            {
                var frozen = plan!.Parts[0].Sources.Single(s => s.Id == plan.Parts[0].SelectedSourceId);
                var executed = workset!.Targets[0].SelectedSource!;
                if (frozen.Url != executed.Url && frozen.Url != executed.OriginalUrl || frozen.ArchivePassword != executed.ArchivePassword ||
                    frozen.ProviderId is not (null or "unknown") && frozen.ProviderId != executed.ProviderId)
                    reason = "SOURCE_REVISION_CHECKPOINT_UNSUPPORTED";
            }
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { reason = "SOURCE_REVISION_CHECKPOINT_UNSUPPORTED"; plan = null; workset = null; }
        reason = task.DeletedAt != null ? "ACQUISITION_TASK_DELETED" :
            run == null ? "SOURCE_REVISION_NO_RUN" :
            tasks?.IsPending("workflow.run." + run.Id) == true || run.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending ? "ACQUISITION_TASK_ACTIVE" :
            run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted or WorkflowRunStatus.Cancelled) ? "SOURCE_REVISION_ALREADY_COMPLETED" :
            run.PendingSignalJson != null ? "SOURCE_REVISION_PENDING_ANSWER" : reason;
        return new(task, run, plan, workset, refs, reason, CloudOperationService.Key(Serialize(task), Serialize(run)));
    }
    private async Task<IReadOnlyList<AcquisitionSourceRevisionCandidate>> CandidatesAsync(State state, CancellationToken ct)
    {
        var output = new List<AcquisitionSourceRevisionCandidate>();
        foreach (var part in state.Plan!.Parts)
        foreach (var source in part.Sources.Where(s => s.Id != part.SelectedSourceId && s.ProcessingIssueCode == null))
            output.Add(new("plan:" + part.Id + ":" + source.Id, part.Id, source, state.Sources, "savedPlanMirror",
                CloudOperationService.Key(Serialize(source), Serialize(state.Sources))));
        foreach (var provider in candidateProviders)
        {
            try { output.AddRange(await provider.ReadAsync(state.Plan, state.Sources, ct)); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { /* stale producer evidence offers no candidate */ }
        }
        return output.DistinctBy(c => (c.PartId, c.Source.Url, c.Source.AccessCode, c.Source.ArchivePassword))
            .Take(100).ToList();
    }
    private Task<DownloadIntentPreflight> CheckSourceAsync(AcquisitionPlan plan, AcquisitionPlanSource source, int id, CancellationToken ct) =>
        downloads.PreflightAsync(new() {Mode = "files", Title = plan.Title, CheckSourceAvailability = true,
            AvailabilityContextKey = "acquisition:" + id,
            Candidates = [new() {Id = source.Id, Url = source.Url, Kind = source.Kind, ProviderId = source.ProviderId,
                MethodId = source.MethodId, AccountId = source.AccountId, AccessCode = source.AccessCode,
                ArchivePassword = source.ArchivePassword, ExtractionPlanJson = Serialize(source.Processing)}], SelectedCandidateIds = [source.Id]}, plan.OutputDirectory, ct);
    private static AcquisitionPlanSource Freeze(AcquisitionPlanSource source, DownloadIntentPreflight check) =>
        check.Candidates.SingleOrDefault()?.ResolvedSource is {} resolved ? source with {Url = resolved.Url, OriginalUrl = resolved.OriginalUrl,
            ResolutionChain = resolved.ResolutionChain.ToList(), ProviderId = resolved.ProviderId, Kind = resolved.Kind,
            MethodId = resolved.MethodId, AccountId = resolved.AccountId} : source;
    internal static AcquisitionPlan RevisedPlan(AcquisitionPlan plan, AcquisitionPlanSource reviewed, AcquisitionPlanSource source)
    {
        var part = plan.Parts.Single();
        // Compare a retained ID with the reviewed input, not with a subsequently expanded URL.
        // Only the preflight-produced source is frozen; arbitrary ID collisions remain rejected.
        if (reviewed.Id != source.Id || part.Sources.Any(s => s.Id == source.Id && s.Url != reviewed.Url &&
                !(s.OriginalUrl == reviewed.Url && s.ResolutionChain is {Count: > 1} &&
                    s.ResolutionChain[0] == reviewed.Url && s.ResolutionChain[^1] == s.Url)))
            throw new ArgumentException("SOURCE_REVISION_CANDIDATE_CHANGED");
        return plan with {Parts = [part with {Sources = part.Sources.Where(s => s.Id != source.Id).Append(source).ToList(),
            SelectedSourceId = source.Id, SelectionMode = "manual", Processing = source.Processing,
            ProcessingSource = source.ProcessingSource, AppliedProcessingRevisionId = source.AppliedProcessingRevisionId}]};
    }
    private async Task<AcquisitionSourceRevisionResult?> AlreadyApplied(int id, string token, CancellationToken ct)
    {
        var json = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == id && t.DeletedAt == null).Select(t => t.PlanHistoryJson).SingleOrDefaultAsync(ct);
        var revision = JsonSerializer.Deserialize<List<AcquisitionPlanRevision>>(json ?? "[]", Json)?.SingleOrDefault(r => r.SourceRevision?.DecisionId == token);
        return revision?.PreviousRunId is {} runId ? new(id, revision.Revision, runId, true) : null;
    }
    private static IReadOnlyList<string> Directories(AcquisitionPlan plan) => new[] {plan.OutputDirectory, plan.DeliveryLayout?.RootDirectory,
        plan.DeliveryLayout?.TaskDirectory}.Concat(plan.DeliveryLayout?.PartDirectories.Values.AsEnumerable() ?? []).Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().Distinct().ToList();
    private static void RequireExpected(State state, int revision, int runId)
    {if (state.Task.PlanRevision != revision || state.Run?.Id != runId) throw new InvalidOperationException("SOURCE_REVISION_TASK_CHANGED");}
    private static string Version(State state, AcquisitionSourceRevisionSafetyResult safety) => CloudOperationService.Key(state.Version, safety.Version);
    /// <summary>Also freezes fields that legacy v1 download identity deliberately omits. This is
    /// approval identity, not artifact identity: a new method/account generation needs a new review.</summary>
    internal static string SelectedExecutionIdentity(CloudDownloadTarget target, string directory)
    {
        var source = target.SelectedSource ?? throw new InvalidOperationException("Choose a source.");
        return CloudOperationService.Key("source-revision.execution.v1", DownloadArtifactCoordinator.Source(target),
            source.MethodId ?? "", source.MethodScope ?? "", source.AccountVersion ?? "", Path.GetFullPath(directory),
            target.AcquisitionNonce ?? "");
    }
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
}
