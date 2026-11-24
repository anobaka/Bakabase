using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.Workflow;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Global facets use task/run indexes, current-node diagnostics and cached source evidence.
/// A ready local revision with registered inputs also checks those registrations against the
/// producing run. Search does not open the files or load full execution history for every task.</summary>
public sealed class AcquisitionTaskSearchService(BakabaseDbContext db, IAcquisitionService acquisitions,
    AcquisitionSourceAttentionProjection? sourceAttention = null, AcquisitionDownloadActivityProjection? downloadActivity = null,
    IArtifactOperationStore? artifacts = null, WorkflowActivityRuntimeRegistry? runtimeProviders = null,
    BTaskManager? backgroundTasks = null)
{
    private static readonly string[] Stages = ["preparing", "ready", "running", "attention", "completed"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Dictionary<int, string>> GetNamesAsync(AcquisitionTaskNamesInput input, CancellationToken ct = default)
    {
        if (input.TaskIds == null || input.TaskIds.Count is 0 or > 10000 || input.TaskIds.Any(id => id <= 0))
            throw new ArgumentException("Choose between one and 10000 fixed acquisition task IDs.");
        var ids = input.TaskIds.Distinct().ToArray();
        // Task labels are also needed when cleaning files left by a soft-deleted task.
        // Do not hydrate plans, execution records or file manifests for a confirmation list.
        var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new {t.Id, t.Title, t.ResourceId}).ToListAsync(ct);
        var resourceIds = tasks.Where(t => string.IsNullOrWhiteSpace(t.Title) && t.ResourceId.HasValue)
            .Select(t => t.ResourceId!.Value).Distinct().ToArray();
        var resourceNames = resourceIds.Length == 0 ? new Dictionary<int, string?>() :
            await acquisitions.GetDisplayNamesAsync(resourceIds, ct);
        var names = new Dictionary<int, string>();
        foreach (var task in tasks)
        {
            var name = string.IsNullOrWhiteSpace(task.Title) && task.ResourceId is {} resourceId
                ? resourceNames.GetValueOrDefault(resourceId) : task.Title;
            if (!string.IsNullOrWhiteSpace(name)) names.Add(task.Id, name);
        }
        return names;
    }

    public async Task<AcquisitionTaskPage> SearchAsync(AcquisitionTaskSearchInput input, CancellationToken ct = default)
    {
        var sort = input.Sort ?? "newest";
        var selectedTags = (input.AttentionTags ?? []).Concat(string.IsNullOrEmpty(input.AttentionTag) ? [] : new[] {input.AttentionTag})
            .Distinct(StringComparer.Ordinal).ToArray();
        var selectedFacts = (input.Facts ?? []).Distinct(StringComparer.Ordinal).ToArray();
        if (input.Page < 1 || input.PageSize is < 1 or > 100 || input.Keyword?.Length > 1000 ||
            !(input.Stage is "all" or "cancelled" || Stages.Contains(input.Stage)) ||
            sort is not ("newest" or "lastProcessed" or "sizeAsc" or "sizeDesc") ||
            (input.AttentionTags?.Count ?? 0) > 32 || (input.Facts?.Count ?? 0) > 16 ||
            selectedTags.Any(tag => !ExecutionProblemClassifier.AttentionTags.Contains(tag)) ||
            !AcquisitionDownloadSourceFilter.IsValid(input.DownloadSource) ||
            selectedFacts.Any(fact => !AcquisitionExecutionFactProjection.FactTags.Contains(fact)) ||
            input.CapabilityId is {Length: > 128} || input.ExecutionState is {Length: > 64})
            throw new ArgumentException("Choose a valid stage, download source, attention tag, sort and page (1–100 tasks per page).");
        var capabilityId = input.CapabilityId?.Trim();
        var executionState = input.ExecutionState?.Trim();
        if (capabilityId?.Length == 0 || executionState?.Length == 0)
            throw new ArgumentException("Choose a non-empty runtime capability and execution state.");
        // No checkpoint, frozen plan, output file manifest or old execution history is selected here.
        var index = await (from task in db.AcquisitionTasks.AsNoTracking()
            where task.DeletedAt == null
            join run in db.WorkflowRuns.AsNoTracking() on task.WorkflowRunId equals (int?)run.Id into joined
            from run in joined.DefaultIfEmpty()
            select new {task.Id, task.Title, task.ResourceId, task.BuilderId, task.Phase, task.TargetDirectory,
                task.WorkflowRunId, task.Status, task.CompletedAt, task.WaitReason, RunStatus = run == null ? (WorkflowRunStatus?)null : run.Status,
                Step = run == null ? (int?)null : run.CurrentStepIndex}).ToListAsync(ct);
        var rows = index.Select(row => new AcquisitionTask {Id = row.Id, Title = row.Title, ResourceId = row.ResourceId,
            BuilderId = row.BuilderId, Phase = row.Phase, TargetDirectory = row.TargetDirectory,
            WorkflowRunId = row.WorkflowRunId, CompletedAt = row.CompletedAt, WaitReason = row.WaitReason, CurrentStepIndex = row.Step,
            Status = row.RunStatus switch
            {
                WorkflowRunStatus.Pending or WorkflowRunStatus.Running => AcquisitionStatus.Running,
                WorkflowRunStatus.Success => AcquisitionStatus.Completed, WorkflowRunStatus.Cancelled => AcquisitionStatus.Cancelled,
                WorkflowRunStatus.Waiting => AcquisitionStatus.Waiting, WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted => AcquisitionStatus.Failed,
                _ => row.Status
            }}).ToList();
        var activity = downloadActivity == null ? new Dictionary<int, AcquisitionDownloadActivity>() :
            await downloadActivity.ReadAsync(rows.Where(t => t.Status is AcquisitionStatus.Waiting or AcquisitionStatus.Running)
                .Select(t => t.Id).ToArray(), ct);
        foreach (var row in rows) row.DownloadActivity = activity.GetValueOrDefault(row.Id)?.Activity;
        var stageCounts = Stages.ToDictionary(stage => stage, stage => rows.Count(t => t.Stage == stage));
        var totalTasks = stageCounts.Values.Sum();
        stageCounts["all"] = totalTasks;
        var keyword = input.Keyword?.Trim() ?? "";
        var names = keyword.Length == 0 ? new Dictionary<int, string?>() : await acquisitions.GetDisplayNamesAsync(
            rows.Where(t => t.Title == null && t.ResourceId.HasValue).Select(t => t.ResourceId!.Value).Distinct().ToArray(), ct);
        var exactId = keyword.StartsWith('#') && int.TryParse(keyword[1..], out var id) ? id : (int?)null;
        // A task candidate is only a navigation shortcut. Submitting the text must
        // still find titles such as "Collection #123" as well as task #123 itself.
        bool Matches(AcquisitionTask task) => exactId.HasValue && task.Id == exactId || keyword.Length == 0 ||
            task.Id.ToString() == keyword || (task.Title ?? (task.ResourceId is {} resourceId ? names.GetValueOrDefault(resourceId) : null))?
                .Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
        var scoped = rows.Where(t => (input.Stage == "all" ? t.Stage != "cancelled" : t.Stage == input.Stage) && Matches(t)).ToArray();
        if (input.DownloadSource != null)
        {
            var matchingSources = await new AcquisitionDownloadSourceFilter(db)
                .MatchAsync(scoped.Select(t => t.Id).ToArray(), input.DownloadSource, ct);
            scoped = scoped.Where(t => matchingSources.Contains(t.Id)).ToArray();
        }
        // Runtime facets are a bounded projection of the current node. Providers own the
        // capability descriptor and optional execution state; no acquisition status is changed
        // and no execution history or download evidence is loaded for this list facet.
        var runtimeFacets = runtimeProviders == null ? new Dictionary<int, AcquisitionTaskRuntimeFacet>() :
            await ReadRuntimeFacetsAsync(scoped, ct);
        var runtimeCapabilityCounts = runtimeFacets.Values.Where(f => f.CapabilityId != null)
            .GroupBy(f => f.CapabilityId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var runtimeExecutionStateCounts = runtimeFacets.Values.Where(f => f.ExecutionState != null)
            .GroupBy(f => f.ExecutionState!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (capabilityId != null)
            scoped = scoped.Where(t => runtimeFacets.TryGetValue(t.Id, out var facet) && facet.CapabilityId == capabilityId).ToArray();
        if (executionState != null)
            scoped = scoped.Where(t => runtimeFacets.TryGetValue(t.Id, out var facet) && facet.ExecutionState == executionState).ToArray();
        var attention = scoped.Where(t => t.Stage == "attention" || t.DownloadActivity != null).ToArray();
        var attentionIds = attention.Select(t => t.Id).ToArray();
        var attentionRunIds = attention.Where(t => t.WorkflowRunId.HasValue).Select(t => t.WorkflowRunId!.Value).Distinct().ToArray();
        var sourceAttentionIds = attention.Where(t => t.DownloadActivity == null).Select(t => t.Id).ToArray();
        var sourceAttentionRuns = attention.Where(t => t.DownloadActivity == null && t.WorkflowRunId.HasValue)
            .Select(t => t.WorkflowRunId!.Value).Distinct().ToArray();
        var runReasons = await db.WorkflowRuns.AsNoTracking().Where(r => attentionRunIds.Contains(r.Id))
            .Select(r => new {r.Id, r.Status, r.WaitReason, r.ErrorMessage, r.CurrentStepIndex, r.PendingSignalJson,
                // Read only a bounded current processing prompt; never the workset or plan manifest.
                WaitPromptJson = r.Status == WorkflowRunStatus.Waiting &&
                    (r.WaitReason == "cloud.fileProcessing" || r.WaitReason == "artifacts.publication") &&
                    r.WaitPromptJson != null && r.WaitPromptJson.Length <= 1024 * 1024 ? r.WaitPromptJson : null})
            .ToDictionaryAsync(r => r.Id, ct);
        // Node output blocks are already bounded, safe metadata. Only the newest invocation at the current cursor counts.
        var nodes = await (from node in db.WorkflowNodeExecutions.AsNoTracking()
            join run in db.WorkflowRuns.AsNoTracking() on node.WorkflowRunId equals run.Id
            where attentionRunIds.Contains(node.WorkflowRunId) && node.StepIndex == run.CurrentStepIndex &&
                  !db.WorkflowNodeExecutions.Any(later => later.WorkflowRunId == node.WorkflowRunId && later.StepIndex == node.StepIndex &&
                      (later.Attempt > node.Attempt || later.Attempt == node.Attempt && later.Id > node.Id))
            select new {node.WorkflowRunId, node.Kind, node.Status, node.WaitReason, node.OutputsJson, node.StartedAt, node.CompletedAt}).ToListAsync(ct);
        var plans = sourceAttention == null ? new Dictionary<int, string?>() : await db.AcquisitionTasks.AsNoTracking()
            .Where(t => sourceAttentionIds.Contains(t.Id)).Select(t => new {t.Id, t.PlanJson}).ToDictionaryAsync(t => t.Id, t => t.PlanJson, ct);
        var correctedRuns = sourceAttention == null ? null :
            await AcquisitionEffectiveSourceProjection.ReadCorrectedRunsAsync(db, sourceAttentionRuns, ct);
        var preparation = await db.AcquisitionTasks.AsNoTracking().Where(t => attentionIds.Contains(t.Id) && t.WorkflowRunId == null)
            .Select(t => new {t.Id, t.Error, t.BuilderStateJson, t.PlanRevision}).ToDictionaryAsync(t => t.Id, ct);
        var currentSpaceFailures = new HashSet<int>();
        foreach (var task in attention)
        {
            var codes = new List<string>(); string? waitReason = null; string? activityKind = null;
            var unresolvedWarning = false;
            var partCodes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var currentDownloads = activity.GetValueOrDefault(task.Id);
            var acquiredPartIds = currentDownloads?.CompletedPartIds.ToHashSet(StringComparer.Ordinal) ?? new(StringComparer.Ordinal);
            DateTimeOffset? executionAt = null;
            if (task.WorkflowRunId is {} runId && runReasons.TryGetValue(runId, out var run))
            {
                waitReason = run.WaitReason;
                var node = nodes.FirstOrDefault(n => n.WorkflowRunId == runId);
                var nodeOutputs = Read<List<WorkflowOutputBlock>>(node?.OutputsJson) ?? [];
                var failureCode = AcquisitionRuntimeProblems.CurrentFailureCode(run.Status, node?.Status,
                    run.ErrorMessage, nodeOutputs);
                var processingWaitCode = AcquisitionRuntimeProblems.CurrentProcessingWaitCode(run.Status, node?.Kind,
                    node?.Status, run.WaitPromptJson);
                var currentCodes = new List<string>();
                if (failureCode != null) { codes.Add(failureCode); currentCodes.Add(failureCode); waitReason = null; }
                if (processingWaitCode != null) currentCodes.Add(processingWaitCode);
                if (processingWaitCode != null) codes.Add(processingWaitCode);
                if (node != null && node.Status != "succeeded")
                {
                    activityKind = node.Kind;
                    executionAt = AcquisitionRuntimeDownloadReader.Utc(node.CompletedAt ?? node.StartedAt);
                    if (failureCode == null) waitReason = node.WaitReason ?? waitReason;
                    foreach (var output in nodeOutputs)
                    {
                        var separator = output.Key.LastIndexOf(':');
                        if (output.Kind == "artifact" && output.ArtifactId != null && separator > 0 &&
                            output.Key[(separator + 1)..] is "download" or "processed" or "published" or "continuation")
                            acquiredPartIds.Add(output.Key[..separator]);
                        if (output.Kind == "warning" && failureCode == null)
                        {
                            if (separator > 0 && currentDownloads?.RecoveredPartIds.Contains(output.Key[..separator]) == true)
                                continue;
                            unresolvedWarning = true;
                            if (output.Code == null) continue;
                            var extracted = ExecutionProblemClassifier.IsKnown(output.Code) ? new[] {output.Code} : WorkflowSourceRunProblems.Extract(output.Code);
                            codes.AddRange(extracted);
                            currentCodes.AddRange(extracted);
                            if (output.Key.EndsWith(":problem", StringComparison.Ordinal)) partCodes[output.Key[..^8]] = extracted;
                        }
                    }
                    if (failureCode == null)
                    {
                        codes.AddRange(WorkflowSourceRunProblems.Extract(node.WaitReason));
                        currentCodes.AddRange(WorkflowSourceRunProblems.Extract(node.WaitReason));
                    }
                }
                // A successful current attempt may have moved on before the task index catches up.
                if (node?.Status != "succeeded" && failureCode == null && processingWaitCode == null && task.DownloadActivity == null)
                    codes.AddRange(WorkflowSourceRunProblems.Extract(run.ErrorMessage));
                if (task.DownloadActivity != null && codes.Count == 0 && !unresolvedWarning) waitReason = null;
                codes.AddRange(WorkflowSourceRunProblems.Extract(waitReason));
                if (run.PendingSignalJson == null && run.Status is (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted) &&
                    node?.Status is ("waiting" or "failed") && currentCodes.Any(ExecutionProblemClassifier.IsSpaceInsufficient))
                    currentSpaceFailures.Add(task.Id);
            }
            else
            {
                var saved = preparation.GetValueOrDefault(task.Id);
                codes.AddRange(WorkflowSourceRunProblems.Extract(saved?.Error));
                foreach (var issue in Read<AcquisitionPlanBuilderState>(saved?.BuilderStateJson)?.Issues ?? [])
                    if (issue.Blocking && ExecutionProblemClassifier.IsKnown(issue.Code)) codes.Add(issue.Code);
            }
            IReadOnlyList<string> projectedCodes = codes;
            if (sourceAttention != null)
            {
                if (preparation.TryGetValue(task.Id, out var draft))
                    projectedCodes = await sourceAttention.ProjectPreparationAsync(task.Id, draft.PlanRevision, plans.GetValueOrDefault(task.Id),
                        draft.BuilderStateJson, codes, ct);
                if (ReferenceEquals(projectedCodes, codes))
                    projectedCodes = await sourceAttention.ProjectAsync(
                        AcquisitionEffectiveSourceProjection.Apply(plans.GetValueOrDefault(task.Id),
                            task.WorkflowRunId is {} owner ? correctedRuns?.GetValueOrDefault(owner) : null),
                        codes, partCodes, executionAt, ct, acquiredPartIds, activityKind);
            }
            // The classifier's empty-code fallback describes an actual unexplained wait,
            // not a healthy download which is still owned by a suspended workflow.
            task.AttentionTags = task.DownloadActivity != null && projectedCodes.Count == 0 && !unresolvedWarning ? [] :
                ExecutionProblemClassifier.Classify(projectedCodes, waitReason, task.WorkflowRunId == null ? task.WaitReason : null);
        }
        var attentionCounts = ExecutionProblemClassifier.AttentionTags.ToDictionary(tag => tag, tag => scoped.Count(t => t.AttentionTags.Contains(tag)));
        var facts = await new AcquisitionExecutionFactProjection(db, artifacts).ReadAsync(scoped.Select(t => t.Id).ToArray(), ct, activity,
            scoped.Where(t => t.Stage == "ready").Select(t => t.Id).ToArray());
        // A current capacity failure can be known before all local inputs are registered.
        // This task-level scope deliberately cannot satisfy the same-part conjunction.
        foreach (var taskId in currentSpaceFailures) facts[taskId] = facts[taskId].WithTaskSpaceFailure();
        var factCounts = AcquisitionExecutionFactProjection.FactTags.ToDictionary(fact => fact,
            fact => scoped.Count(t => facts[t.Id].Tags.Contains(fact)));
        var candidates = scoped.Where(t => selectedTags.All(tag => t.AttentionTags.Contains(tag)) &&
            facts[t.Id].Matches(selectedFacts)).ToArray();
        var sizes = new AcquisitionSizeEstimateProjection(db);
        // Size order must include all matching tasks before pagination. Default sorting only
        // needs the visible page's estimates.
        var sizeSort = sort is "sizeAsc" or "sizeDesc";
        var candidateIds = candidates.Select(t => t.Id).ToArray();
        var estimates = sizeSort ? await sizes.ReadAsync(candidateIds, ct) : new Dictionary<int, AcquisitionSizeEstimate>();
        var lastProcessed = sort == "lastProcessed" ? await LastProcessedAsync(candidateIds, ct) : null;
        var filtered = sort switch
        {
            "lastProcessed" => candidates.OrderByDescending(t => lastProcessed!.GetValueOrDefault(t.Id, t.CompletedAt?.ToUniversalTime()))
                .ThenByDescending(t => t.Id).ToArray(),
            "sizeAsc" => candidates.OrderBy(t => estimates[t.Id].Bytes == null)
                .ThenBy(t => estimates[t.Id].Bytes).ThenByDescending(t => t.Id).ToArray(),
            "sizeDesc" => candidates.OrderBy(t => estimates[t.Id].Bytes == null)
                .ThenByDescending(t => estimates[t.Id].Bytes).ThenByDescending(t => t.Id).ToArray(),
            _ => candidates.OrderByDescending(t => t.Id).ToArray()
        };
        var page = Math.Min(input.Page, Math.Max(1, (filtered.Length + input.PageSize - 1) / input.PageSize));
        var pageIndex = filtered.Skip((page - 1) * input.PageSize).Take(input.PageSize).ToArray();
        if (!sizeSort) estimates = await sizes.ReadAsync(pageIndex.Select(t => t.Id).ToArray(), ct);
        var details = await acquisitions.GetByIdsAsync(pageIndex.Select(t => t.Id).ToArray(), ct);
        var projected = details.ToDictionary(t => t.Id);
        foreach (var task in pageIndex)
            if (projected.TryGetValue(task.Id, out var detail))
            {
                detail.DownloadActivity = task.DownloadActivity;
                detail.AttentionTags = task.AttentionTags;
            }
        var bulk = filtered.Take(10000).ToArray();
        return new() {Items = pageIndex.Where(t => projected.ContainsKey(t.Id)).Select(t => projected[t.Id]).ToArray(),
            TotalCount = filtered.Length, TotalTasks = totalTasks,
            QueueTaskCount = await AcquisitionQueueCount.ReadAsync(db, ct), Page = page, PageSize = input.PageSize,
            StageCounts = stageCounts, AttentionCounts = attentionCounts, FactCounts = factCounts,
            RuntimeCapabilityCounts = runtimeCapabilityCounts, RuntimeExecutionStateCounts = runtimeExecutionStateCounts,
            RuntimeCapabilities = runtimeFacets.Values.Where(f => f.CapabilityId != null)
                .GroupBy(f => f.CapabilityId!, StringComparer.Ordinal).Select(g => g.First()).ToArray(),
            TaskRuntimeFacets = pageIndex.Where(t => runtimeFacets.ContainsKey(t.Id))
                .ToDictionary(t => t.Id, t => runtimeFacets[t.Id]),
            TaskFacts = pageIndex.Where(t => projected.ContainsKey(t.Id)).ToDictionary(t => t.Id, t => facts[t.Id].Tags),
            FilteredTaskIds = bulk.Select(t => t.Id).ToArray(),
            CheckableTaskIds = bulk.Where(t => t.BuilderId != null).Select(t => t.Id).ToArray(), TaskIdsTruncated = filtered.Length > bulk.Length,
            SizeEstimates = pageIndex.Where(t => projected.ContainsKey(t.Id)).ToDictionary(t => t.Id, t => estimates[t.Id])};
    }

    private async Task<Dictionary<int, AcquisitionTaskRuntimeFacet>> ReadRuntimeFacetsAsync(
        IReadOnlyList<AcquisitionTask> rows, CancellationToken ct)
    {
        if (runtimeProviders == null) return new();
        // A completed run retains its last node for history, but that node is no
        // longer an active capability. Runtime facets belong to resumable or
        // currently executing acquisition work only.
        var bindings = rows.Where(t => t.Stage is "running" or "attention" &&
                t.WorkflowRunId.HasValue && t.CurrentStepIndex.HasValue)
            .Select(t => (TaskId: t.Id, RunId: t.WorkflowRunId!.Value, Step: t.CurrentStepIndex!.Value))
            .ToArray();
        if (bindings.Length == 0) return new();
        var runIds = bindings.Select(b => b.RunId).Distinct().ToArray();
        var nodes = await (from node in db.WorkflowNodeExecutions.AsNoTracking()
            join run in db.WorkflowRuns.AsNoTracking() on node.WorkflowRunId equals run.Id
            where runIds.Contains(node.WorkflowRunId) && node.StepIndex == run.CurrentStepIndex &&
                  !db.WorkflowNodeExecutions.Any(later => later.WorkflowRunId == node.WorkflowRunId &&
                      later.StepIndex == node.StepIndex &&
                      (later.Attempt > node.Attempt || later.Attempt == node.Attempt && later.Id > node.Id))
            select new {node.WorkflowRunId, node.StepIndex, node.Kind, run.Status}).ToListAsync(ct);
        var byRun = nodes.ToDictionary(n => n.WorkflowRunId);
        var result = new Dictionary<int, AcquisitionTaskRuntimeFacet>();
        foreach (var binding in bindings)
        {
            if (!byRun.TryGetValue(binding.RunId, out var node) || string.IsNullOrWhiteSpace(node.Kind)) continue;
            var live = backgroundTasks?.GetTaskViewModel($"workflow.run.{binding.RunId}") is
                {Status: BTaskStatus.Running, Data: WorkflowTaskProgress progress} ? progress : null;
            var snapshot = await runtimeProviders.ReadAsync(new(binding.RunId, binding.Step, node.Kind,
                node.Status, live, []), ct);
            var capability = snapshot.Capability;
            if (capability == null) continue;
            result[binding.TaskId] = new()
            {
                ActivityKind = node.Kind,
                CapabilityId = capability.Id,
                CapabilityName = capability.DisplayName,
                CapabilityNameKey = capability.DisplayNameKey,
                ExecutionState = capability.Status,
                WaitReason = capability.WaitReason,
                ExecutionPolicy = capability.ExecutionPolicy,
                MaximumParallelism = capability.MaximumParallelism
            };
        }
        return result;
    }

    private async Task<Dictionary<int, DateTime?>> LastProcessedAsync(int[] taskIds, CancellationToken ct)
    {
        // A task may be between revisions with no current run. Include its linked runs,
        // reading only indexed timestamps so repeated attempts do not load file or node outputs.
        var matching = db.AcquisitionTasks.Where(t => taskIds.Contains(t.Id));
        var links = db.Set<WorkflowRunSourceDbModel>().Where(s => s.Module == "acquisition" && s.EntityKind == "task");
        var owners = matching.Where(t => t.WorkflowRunId.HasValue)
            .Select(t => new { TaskId = t.Id, RunId = t.WorkflowRunId!.Value })
            .Union(from t in matching join link in links on t.Id.ToString() equals link.EntityId
                select new { TaskId = t.Id, RunId = link.WorkflowRunId });
        var activity = await (from owner in owners
            join run in db.WorkflowRuns on owner.RunId equals run.Id
            where !links.Any(s => s.WorkflowRunId == run.Id && s.EntityId != owner.TaskId.ToString()) &&
                !db.AcquisitionTasks.Any(t => t.WorkflowRunId == run.Id && t.Id != owner.TaskId)
            select new { owner.TaskId, run.StartedAt, run.CompletedAt, run.WaitingSince,
                NodeUtc = db.WorkflowNodeExecutions.Where(n => n.WorkflowRunId == run.Id)
                    .Max(n => (DateTime?)(n.CompletedAt ?? n.StartedAt)) }).ToListAsync(ct);
        // SQLite stores run-level times in server local time and node timestamps in UTC.
        return activity.GroupBy(a => a.TaskId).ToDictionary(g => g.Key, g => g.Max(a => new DateTime?[]
        {
            a.StartedAt.ToUniversalTime(), a.WaitingSince?.ToUniversalTime(), a.CompletedAt?.ToUniversalTime(), a.NodeUtc
        }.Max()));
    }

    private static T? Read<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 1024 * 1024) return null;
        try { return JsonSerializer.Deserialize<T>(json, Json); } catch (JsonException) { return null; }
    }
}
