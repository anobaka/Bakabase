using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Bounded inspection of existing evidence. Never reconciles task state or probes a remote source.</summary>
public sealed class AcquisitionRuntimeOverviewService(BakabaseDbContext db, IAcquisitionRuntimeDownloadReader downloads,
    SourceAvailabilityService availability, AcquisitionSourceAttentionProjection? sourceAttention = null,
    BTaskManager? backgroundTasks = null, WorkflowActivityRuntimeRegistry? runtimeProviders = null,
    AcquisitionConfirmationRuntime? confirmations = null)
{
    private const int MaximumSources = 50;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AcquisitionRuntimeOverview>> GetAsync(IReadOnlyList<int>? taskIds, CancellationToken ct = default)
    {
        if (taskIds == null || taskIds.Count > 100 || taskIds.Any(id => id <= 0))
            throw new ArgumentException("Provide at most 100 positive acquisition task ids.");
        var ids = taskIds.Distinct().ToArray();
        if (ids.Length == 0) return [];
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.DeletedAt == null && ids.Contains(t.Id)).Select(t => new AcquisitionTaskDbModel
        {Id = t.Id, PlanRevision = t.PlanRevision, WorkflowRunId = t.WorkflowRunId, Status = t.Status, Error = t.Error, UpdatedAt = t.UpdatedAt,
            CompletedAt = t.CompletedAt, BuilderStateJson = t.BuilderStateJson, PlanJson = t.PlanJson,
            BuilderId = t.BuilderId, Phase = t.Phase, TargetDirectory = t.TargetDirectory}).ToListAsync(ct);
        var runIds = tasks.Where(t => t.WorkflowRunId.HasValue).Select(t => t.WorkflowRunId!.Value).Distinct().ToArray();
        var runs = await db.WorkflowRuns.AsNoTracking().Where(r => runIds.Contains(r.Id)).Select(r => new WorkflowRunDbModel
        {Id = r.Id, WorkflowDefinitionId = r.WorkflowDefinitionId, PlanSnapshotJson = r.PlanSnapshotJson, Status = r.Status,
            CurrentStepIndex = r.CurrentStepIndex, CurrentItemJson = r.CurrentItemJson, PayloadJson = r.PayloadJson,
            WaitPromptJson = r.WaitPromptJson, WaitingSince = r.WaitingSince, WaitReason = r.WaitReason,
            ErrorMessage = r.ErrorMessage, PendingSignalJson = r.PendingSignalJson, CompletedAt = r.CompletedAt}).ToDictionaryAsync(r => r.Id, ct);
        // Fetch one invocation per step, not the unbounded retry history.
        var nodes = await db.WorkflowNodeExecutions.AsNoTracking().Where(n => runIds.Contains(n.WorkflowRunId) &&
            !db.WorkflowNodeExecutions.Any(later => later.WorkflowRunId == n.WorkflowRunId &&
                later.StepIndex == n.StepIndex && (later.Attempt > n.Attempt || later.Attempt == n.Attempt && later.Id > n.Id)))
            .ToListAsync(ct);
        var legacyIds = runs.Values.Where(r => r.PlanSnapshotJson == null).Select(r => r.WorkflowDefinitionId).Distinct().ToArray();
        var legacyNodes = await db.Set<WorkflowActivityDbModel>().AsNoTracking().Where(n => legacyIds.Contains(n.WorkflowDefinitionId))
            .Select(n => new {n.WorkflowDefinitionId, n.Order, n.Id, n.Kind}).ToListAsync(ct);
        var operations = await db.CloudDownloadOperations.AsNoTracking().Where(o => runIds.Contains(o.WorkflowRunId) && o.DownloadTaskId != null)
            .OrderByDescending(o => o.UpdatedAt).Select(o => new {o.WorkflowRunId, o.TargetId, o.DownloadTaskId}).ToListAsync(ct);
        var owners = await db.DownloadResultOwners.AsNoTracking().Where(o => runIds.Contains(o.WorkflowRunId))
            .Select(o => new {o.WorkflowRunId, o.DownloadTaskId}).ToListAsync(ct);
        var correctionWorksets = runs.Values.ToDictionary(r => r.Id, r => AcquisitionEffectiveSourceProjection.Read(r.CurrentItemJson));
        var worksets = runs.Values.ToDictionary(r => r.Id, r => correctionWorksets[r.Id] ??
            (r.Status == WorkflowRunStatus.Waiting ? Workset(r.WaitPromptJson) : null) ?? Workset(r.CurrentItemJson) ?? Workset(r.PayloadJson));
        foreach (var (id, completed) in await AcquisitionCompletedSourceProjection.ReadAsync(db,
                     runs.Values.Where(r => r.Status == WorkflowRunStatus.Success && string.IsNullOrEmpty(r.CurrentItemJson))
                         .Select(r => r.Id).ToArray(), ct))
            correctionWorksets[id] = completed;
        var outputs = nodes.ToDictionary(n => n.Id, n => Read<List<WorkflowOutputBlock>>(n.OutputsJson) ?? []);
        var taskBindings = new Dictionary<int, HashSet<string>>();
        var targetBindings = new Dictionary<(int Run, string Target), string>();
        foreach (var run in runs.Values)
        {
            var bound = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes.Where(n => n.WorkflowRunId == run.Id).OrderBy(n => n.StepIndex))
                foreach (var output in outputs[node.Id].Where(o => o.Kind == "task" && o.TaskKind == "download" && o.TaskId != null))
                {
                    if (output.Key.EndsWith(":downloadTask", StringComparison.Ordinal))
                        targetBindings[(run.Id, output.Key[..^13])] = output.TaskId!;
                    else bound.Add(output.TaskId!);
                }
            foreach (var target in worksets[run.Id]?.Targets ?? [])
                if ((target.TransportTaskId ?? (target.DownloadTaskId is {} id ? "site:" + id : null)) is {} taskId)
                { targetBindings[(run.Id, target.Id)] = taskId; }
            foreach (var op in operations.Where(o => o.WorkflowRunId == run.Id))
            {
                var id = "site:" + op.DownloadTaskId;
                targetBindings.TryAdd((run.Id, op.TargetId), id);
            }
            foreach (var owner in owners.Where(o => o.WorkflowRunId == run.Id)) bound.Add("site:" + owner.DownloadTaskId);
            foreach (var binding in targetBindings.Where(b => b.Key.Run == run.Id)) bound.Add(binding.Value);
            taskBindings[run.Id] = bound;
        }
        var allBindings = taskBindings.Values.SelectMany(x => x).Distinct().ToArray();
        var siteIds = allBindings.Where(x => x.StartsWith("site:", StringComparison.Ordinal)).Select(x => int.TryParse(x[5..], out var id) ? id : 0)
            .Where(id => id > 0).ToArray();
        var snapshots = await downloads.ReadAsync(siteIds, allBindings.Where(x => !x.StartsWith("site:", StringComparison.Ordinal)).ToArray(), runIds, ct);
        var downloadActivities = await new AcquisitionDownloadActivityProjection(db, downloads).ReadAsync(ids, ct, snapshots);
        var observations = new Dictionary<string, SourceAvailabilityResult?>();
        var result = new List<AcquisitionRuntimeOverview>();
        foreach (var task in tasks)
        {
            var run = task.WorkflowRunId is {} runId ? runs.GetValueOrDefault(runId) : null;
            var taskNodes = nodes.Where(n => n.WorkflowRunId == run?.Id).ToArray();
            var completed = run != null ? run.Status == WorkflowRunStatus.Success : task.Status == AcquisitionStatus.Completed;
            // Before execution a saved plan may be ready while its underlying task is Waiting.
            // Once a run exists its current status supersedes the task's last recorded status.
            var attention = run != null
                ? run.Status is WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted
                : AcquisitionTask.ResolveStage(task.Status, task.WorkflowRunId, task.BuilderId, task.Phase,
                    task.TargetDirectory, downloadActivities.GetValueOrDefault(task.Id)?.Activity) == "attention";
            WorkflowRunPlan? executionPlan = null;
            try { executionPlan = WorkflowRunPlans.Read(run?.PlanSnapshotJson); } catch (InvalidDataException) { }
            var legacy = legacyNodes.Where(n => n.WorkflowDefinitionId == run?.WorkflowDefinitionId).OrderBy(n => n.Order).ToArray();
            var live = run != null && backgroundTasks?.GetTaskViewModel($"workflow.run.{run.Id}") is
                {Status: BTaskStatus.Running, Data: WorkflowTaskProgress progress} ? progress : null;
            var stepIndex = run?.CurrentStepIndex;
            // The persisted cursor is a restart checkpoint. On the first pass the runner commits
            // the invocation before emitting progress, even when no checkpoint exists yet.
            if (stepIndex == null && run?.Status == WorkflowRunStatus.Running &&
                taskNodes.Where(n => n.Status == "running" && n.CompletedAt == null).Take(2).ToArray() is [var active])
            {
                var planned = executionPlan?.Nodes.ElementAtOrDefault(active.StepIndex);
                var legacyStep = run.PlanSnapshotJson == null ? legacy.ElementAtOrDefault(active.StepIndex) : null;
                if (planned != null && planned.NodeId == active.NodeId && planned.Kind == active.Kind ||
                    legacyStep != null && active.NodeId == "activity:" + legacyStep.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                    legacyStep.Kind == active.Kind)
                {
                    stepIndex = active.StepIndex;
                    if (live?.ReportedAt.ToUniversalTime() < DateTime.SpecifyKind(active.StartedAt, DateTimeKind.Utc))
                        live = null;
                }
            }
            var node = taskNodes.FirstOrDefault(n => n.StepIndex == stepIndex);
            var kind = executionPlan?.Nodes.ElementAtOrDefault(stepIndex ?? -1)?.Kind ??
                legacy.ElementAtOrDefault(stepIndex ?? -1)?.Kind ?? node?.Kind;
            var failureCode = AcquisitionRuntimeProblems.CurrentFailureCode(run?.Status, node?.Status,
                run?.ErrorMessage, node == null ? [] : outputs[node.Id]);
            var bound = run != null ? taskBindings[run.Id] : [];
            var downloadActivity = downloadActivities.GetValueOrDefault(task.Id);
            var targets = run != null && worksets[run.Id] is {} workset ? workset.Targets : PlanTargets(task.PlanJson);
            targets = AcquisitionEffectiveSourceProjection.ApplyDisplaySources(targets,
                run == null ? null : correctionWorksets.GetValueOrDefault(run.Id));
            var excludedDownloads = targets.Where(t => t.Excluded && run != null)
                .Select(t => targetBindings.GetValueOrDefault((run!.Id, t.Id))).Where(id => id != null).ToHashSet();
            var matching = snapshots.Where(d => !excludedDownloads.Contains(d.Id) &&
                (d.ParentId == null || !excludedDownloads.Contains(d.ParentId)) &&
                (d.WorkflowRunId == null || d.WorkflowRunId == run?.Id) &&
                (bound.Contains(d.Id) || d.ParentId != null && bound.Contains(d.ParentId) ||
                 run != null && d.WorkflowRunId == run.Id)).ToArray();
            var replacedParents = matching.Where(d => d.ParentId != null).Select(d => d.ParentId!).ToHashSet();
            var leaves = matching.Where(d => !replacedParents.Contains(d.Id)).DistinctBy(d => d.Id).ToArray();
            attention |= !completed && leaves.Any(d => d.Status == "failed");
            var downloadedTargets = targets.Where(t => !t.Excluded && run != null &&
                targetBindings.TryGetValue((run.Id, t.Id), out var binding) &&
                leaves.Where(d => d.Id == binding || d.ParentId == binding).ToArray() is {Length: > 0} children &&
                children.All(d => d.Status == "completed")).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var waitEvidenceAt = node?.CompletedAt is { } nodeCompleted ? AcquisitionRuntimeDownloadReader.Utc(nodeCompleted) :
                run?.WaitingSince is { } waitingSince ? AcquisitionRuntimeDownloadReader.Local(waitingSince) : (DateTimeOffset?)null;
            var recoveredTargets = targets.Where(t => downloadedTargets.Contains(t.Id) && run != null &&
                targetBindings.TryGetValue((run.Id, t.Id), out var binding) &&
                leaves.Where(d => d.Id == binding || d.ParentId == binding).All(d => waitEvidenceAt == null || d.ObservedAt >= waitEvidenceAt))
                .Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            var completedWait = run?.Status == WorkflowRunStatus.Waiting && kind == "download.awaitCompletion" &&
                targets.Any(t => !t.Excluded) && targets.Where(t => !t.Excluded).All(t => downloadedTargets.Contains(t.Id));
            // This is an observation of completed downloads, not a workflow checkpoint mutation.
            // Verification and any subsequent write remain behind the normal continuation path.
            // A newer artifact-verification failure after completion remains current evidence.
            if (completedWait && targets.Where(t => !t.Excluded).All(t => recoveredTargets.Contains(t.Id))) attention = false;
            var sources = new List<AcquisitionRuntimeSourceObservation>();
            var executionProblems = new List<string>();
            var partProblems = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var target in targets)
            foreach (var source in target.Sources)
            {
                if (sources.Count >= MaximumSources) break;
                var selected = !target.Excluded && source.Id == target.SelectedSourceId;
                var downloadId = run != null ? targetBindings.GetValueOrDefault((run.Id, target.Id)) : null;
                var download = leaves.Where(d => d.Id == downloadId || downloadId != null && d.ParentId == downloadId)
                    .OrderByDescending(d => d.Status == "failed").ThenByDescending(d => d.ObservedAt).FirstOrDefault();
                var downloadCompleted = kind == "download.awaitCompletion" && recoveredTargets.Contains(target.Id);
                var downloadRecovered = downloadActivity?.RecoveredPartIds.Contains(target.Id) == true;
                var targetSucceeded = downloadCompleted || target.DownloadArtifactId != null || target.ProcessedArtifactId != null || target.PublishedArtifactId != null;
                string? code = null; DateTimeOffset? time = null; string? evidence = null;
                if (!completed && selected && !targetSucceeded && !downloadRecovered)
                {
                    // The active cursor's newest invocation wins; an earlier failed attempt is never consulted.
                    if (attention && node?.Status != "succeeded" && failureCode == null)
                    {
                        var warning = node == null ? null : outputs[node.Id].FirstOrDefault(o => o.Kind == "warning" && o.Key == target.Id + ":problem");
                        code = AcquisitionRuntimeProblems.Code(target.Problem) ?? AcquisitionRuntimeProblems.Code(warning?.Code);
                        if (code != null)
                        {
                            evidence = "workflow";
                            time = node != null ? AcquisitionRuntimeDownloadReader.Utc(node.CompletedAt ?? node.StartedAt) :
                                AcquisitionRuntimeDownloadReader.Local(run?.WaitingSince ?? task.UpdatedAt);
                        }
                    }
                    if (download?.ProblemCode != null && (time == null || download.ObservedAt >= time))
                    { code = download.ProblemCode; time = download.ObservedAt; evidence = code == null ? null : "download"; }
                }
                var cacheKey = JsonSerializer.Serialize(new {source.ProviderId, source.Url, source.Kind, source.AccessCode, source.MethodId, source.AccountId, source.AccountVersion});
                if (!observations.TryGetValue(cacheKey, out var cached))
                    observations[cacheKey] = cached = await availability.GetAsync(source, ct);
                var observation = new AcquisitionRuntimeSourceObservation {PartId = target.Id, SourceId = source.Id, Url = source.Url,
                    AccessCode = source.AccessCode, ProviderId = source.ProviderId, Selected = selected, DownloadTaskId = selected ? downloadId : null,
                    ReasonCode = code, Status = AcquisitionRuntimeProblems.Status(code), Message = code == null ? null : AcquisitionRuntimeProblems.Message(code),
                    EvidenceKind = evidence, ObservedAt = time, IsHistorical = evidence != null};
                if (selected && downloadCompleted)
                    observation = observation with {Status = SourceAvailabilityStatuses.Available, ReasonCode = null,
                        Message = null, EvidenceKind = "download", ObservedAt = download?.ObservedAt, IsHistorical = false};
                if (selected && code != null) { executionProblems.Add(code); partProblems[target.Id] = [code]; }
                // A successful delivery makes pre-delivery failures obsolete, including old cache observations.
                var targetSuccessAt = targetSucceeded ? taskNodes.Where(n => n.Status == "succeeded" && outputs[n.Id].Any(o =>
                        o.Kind == "artifact" && o.Key.StartsWith(target.Id + ":", StringComparison.Ordinal)))
                    .Select(n => n.CompletedAt ?? n.StartedAt).OrderByDescending(t => t).Cast<DateTime?>().FirstOrDefault() : null;
                DateTimeOffset? successAt = selected && completed && (run?.CompletedAt ?? task.CompletedAt) is {} completedAt
                    ? AcquisitionRuntimeDownloadReader.Local(completedAt) : downloadCompleted ? download?.ObservedAt :
                    targetSuccessAt is {} targetAt ? AcquisitionRuntimeDownloadReader.Utc(targetAt) : null;
                // A metadata probe cannot clear an execution-only failure such as rejection of
                // the original-file download. Keep that evidence until the execution recovers;
                // the independent probe remains in the source-availability cache.
                if (cached != null && (code == null || ExecutionProblemClassifier.IsRefreshableSourceCode(code)) &&
                    (time == null || cached.CheckedAt > time) &&
                    (!selected || !targetSucceeded || successAt != null) &&
                    (observation.Status != SourceAvailabilityStatuses.ConfirmedUnavailable || cached.Status is SourceAvailabilityStatuses.Available or SourceAvailabilityStatuses.ConfirmedUnavailable) &&
                    (!selected || successAt == null || cached.CheckedAt > successAt))
                    observation = observation with {Status = cached.Status, ReasonCode = cached.ReasonCode, Message = null,
                        EvidenceKind = "probe", ObservedAt = cached.CheckedAt, ExpiresAt = cached.ExpiresAt,
                        IsHistorical = cached.ExpiresAt <= DateTimeOffset.UtcNow};
                sources.Add(observation with {NormalizedReasonCode = ExecutionProblemClassifier.NormalizedReason(observation.ReasonCode)});
            }
            var processingWaitCode = AcquisitionRuntimeProblems.CurrentProcessingWaitCode(run?.Status, kind,
                node?.Status, run?.WaitPromptJson);
            if (processingWaitCode is null or "FILE_DELIVERY_RESULT_UNCERTAIN")
                processingWaitCode = AcquisitionRuntimeProblems.CurrentAssessmentDiagnostic(run?.Status, kind, node?.Status,
                    node == null ? [] : outputs[node.Id]) ?? processingWaitCode;
            if (failureCode == null && downloadActivity?.Activity != null && executionProblems.Count == 0 && leaves.All(d => d.Status != "failed") &&
                targets.Where(t => !t.Excluded && t.Problem != null).All(t => downloadActivity.RecoveredPartIds.Contains(t.Id)))
                attention = false;
            // A failed invocation supersedes the earlier waiting workset (for example a password prompt).
            var problemCode = attention ? failureCode ?? processingWaitCode ?? executionProblems.FirstOrDefault() ??
                AcquisitionRuntimeProblems.Code(node?.WaitReason) ?? AcquisitionRuntimeProblems.Code(run?.WaitReason) ??
                AcquisitionRuntimeProblems.Code(run?.ErrorMessage) ?? AcquisitionRuntimeProblems.Code(task.Error) : null;
            var issues = Read<AcquisitionPlanBuilderState>(task.BuilderStateJson)?.Issues;
            if (attention && problemCode == null) problemCode = AcquisitionRuntimeProblems.Code(issues?.FirstOrDefault(i => i.Blocking)?.Code) ?? "SOURCE_ATTENTION_REQUIRED";
            // Preparation can have several independent blockers. Keep every current blocker
            // in the tags, as task search does, while retaining one problem as the summary.
            // Once execution starts its current diagnostics supersede preparation issues.
            IEnumerable<string> preparationCodes = task.WorkflowRunId == null
                ? (issues ?? []).Where(i => i.Blocking && ExecutionProblemClassifier.IsKnown(i.Code)).Select(i => i.Code)
                : [];
            IReadOnlyList<string> problemCodes = failureCode != null ? [failureCode] : (processingWaitCode == null ? [] : new[] {processingWaitCode})
                .Concat(executionProblems).Concat(problemCode == null ? [] : new[] {problemCode})
                .Concat(preparationCodes).Distinct().ToArray();
            if (attention && sourceAttention != null)
            {
                var checkedCodes = task.WorkflowRunId == null
                    ? await sourceAttention.ProjectPreparationAsync(task.Id, task.PlanRevision, task.PlanJson, task.BuilderStateJson, problemCodes, ct)
                    : problemCodes;
                problemCodes = !ReferenceEquals(checkedCodes, problemCodes) ? checkedCodes :
                    await sourceAttention.ProjectAsync(AcquisitionEffectiveSourceProjection.Apply(task.PlanJson,
                    run == null ? null : correctionWorksets.GetValueOrDefault(run.Id)), problemCodes, partProblems,
                    node == null ? null : AcquisitionRuntimeDownloadReader.Utc(node.CompletedAt ?? node.StartedAt), ct,
                    targets.Where(t => t.DownloadArtifactId != null || t.ProcessedArtifactId != null || t.PublishedArtifactId != null)
                        .Select(t => t.Id).ToHashSet(StringComparer.Ordinal), node?.Status == "succeeded" ? null : kind);
                problemCode = failureCode is not (null or "SOURCE_ATTENTION_REQUIRED") ? failureCode :
                    processingWaitCode ?? problemCodes.FirstOrDefault() ?? problemCode;
            }
            var downloadEvidence = new CloudDownloadRuntimeEvidence(leaves, completed, attention,
                bound.All(id => excludedDownloads.Contains(id) || matching.Any(d => d.Id == id || d.ParentId == id)) &&
                (targets.Count(t => !t.Excluded) <= 1 || targets.Where(t => !t.Excluded && t.DownloadArtifactId == null)
                    .All(t => run != null && targetBindings.ContainsKey((run.Id, t.Id)))));
            var summary = downloadEvidence.Summarize();
            var confirmation = confirmations?.Read(task.Id, task.PlanRevision, task.WorkflowRunId);
            var runtime = confirmation ?? (run != null && kind != null && runtimeProviders != null
                ? await runtimeProviders.ReadAsync(new(run.Id,
                    stepIndex, kind, run.Status, live, [downloadEvidence]), ct) : null);
            result.Add(new() {TaskId = task.Id, PlanRevision = task.PlanRevision, WorkflowRunId = run?.Id, WorkflowStatus = run?.Status.ToString().ToLowerInvariant(),
                CurrentStepIndex = stepIndex, StepCount = executionPlan?.Nodes.Count ?? (run != null && run.PlanSnapshotJson == null ? legacy.Length : null),
                CurrentNodeKind = kind, Capability = runtime?.Capability, Stage = runtime?.Stage,
                Progress = runtime?.Progress, ProgressScope = runtime?.ProgressScope,
                CompletedBytes = runtime?.CompletedBytes ?? (confirmation == null ? summary.CompletedBytes : null),
                TotalBytes = runtime?.TotalBytes ?? (confirmation == null ? summary.TotalBytes : null),
                SpeedBytesPerSecond = runtime?.SpeedBytesPerSecond ?? (confirmation == null ? summary.SpeedBytesPerSecond : null),
                EstimatedRemainingSeconds = runtime?.EstimatedRemainingSeconds,
                BytesPartial = confirmation == null && summary.BytesPartial,
                DownloadCount = summary.Count, ActiveDownloadCount = summary.ActiveCount,
                CompletedDownloadCount = summary.CompletedCount, FailedDownloadCount = summary.FailedCount,
                DownloadActivity = downloadActivity?.Activity,
                DownloadContinuation = run == null ? null : AcquisitionDownloadContinuationService.Read(task)?.Mode ?? AcquisitionDownloadContinuationService.Manual,
                DownloadsCompletedAwaitingWorkflow = completedWait,
                CanDiagnoseDownloadContinuation = completedWait && run?.PendingSignalJson == null,
                ProblemCode = problemCode, Problem = problemCode == null ? null : AcquisitionRuntimeProblems.Message(problemCode),
                ProblemReasonCode = ExecutionProblemClassifier.NormalizedReason(problemCode),
                AttentionTags = attention ? ExecutionProblemClassifier.Classify(problemCodes, node?.WaitReason ?? run?.WaitReason) : [],
                Sources = sources, SourcesTruncated = targets.Sum(t => t.Sources.Count) > MaximumSources});
        }
        return result.OrderBy(r => Array.IndexOf(ids, r.TaskId)).ToArray();
    }

    private static List<CloudDownloadTarget> PlanTargets(string? json) => Read<AcquisitionPlan>(json)?.Parts.Select(part => new CloudDownloadTarget
    {Id = part.Id, SelectedSourceId = part.SelectedSourceId, Sources = part.Sources.Select(source => new CloudSource
    {Id = source.Id, Url = source.Url, ProviderId = source.ProviderId ?? "unknown", Kind = source.Kind, AccessCode = source.AccessCode,
        MethodId = source.MethodId, AccountId = source.AccountId}).ToList()}).ToList() ?? [];

    private static DownloadWorkset? Workset(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8 * 1024 * 1024) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var item = document.RootElement;
            if (item.ValueKind != JsonValueKind.Object) return null;
            if (item.TryGetProperty("item", out var snapshot)) item = snapshot;
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("variables", out var variables) &&
                variables.ValueKind == JsonValueKind.Object && variables.TryGetProperty("download.workset", out var saved) && saved.ValueKind == JsonValueKind.String)
                return Read<DownloadWorkset>(saved.GetString());
            if (item.ValueKind != JsonValueKind.Object || !item.EnumerateObject().Any(p => p.Name.Equals("targets", StringComparison.OrdinalIgnoreCase))) return null;
            // Explicit type only; never load the CLR type named by a checkpoint envelope.
            return item.Deserialize<DownloadWorkset>(Json);
        }
        catch (JsonException) { return null; }
    }
    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 8 * 1024 * 1024) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }
}
