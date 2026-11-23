using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Planning;

public sealed record AcquisitionAdaptiveMigrationInput(IReadOnlyList<int>? TaskIds = null,
    int MaxIterations = 3);

public sealed record AcquisitionAdaptiveMigrationRow(int TaskId, string? Title, int? WorkflowRunId,
    WorkflowRunStatus? RunStatus, int? CurrentStepIndex, bool CanMigrate, bool AlreadyEnabled,
    string ReasonCode, string? Reason = null);

public sealed record AcquisitionAdaptiveMigrationPreview(string Token, DateTimeOffset ExpiresAt,
    IReadOnlyList<AcquisitionAdaptiveMigrationRow> Rows)
{
    public int CanMigrateCount => Rows.Count(x => x.CanMigrate);
}

public sealed record AcquisitionAdaptiveMigrationResult(IReadOnlyList<int> MigratedTaskIds,
    IReadOnlyList<AcquisitionAdaptiveMigrationRow> Skipped);

/// <summary>
/// Upgrades the frozen execution snapshot of stopped acquisition runs without changing node
/// order. This is intentionally separate from plan editing: adding nodes to a live run would
/// invalidate its cursor and checkpoint identities.
/// </summary>
public sealed class AcquisitionAdaptiveMigrationService(BakabaseDbContext db,
    IWorkflowRunResumer resumer, AcquisitionPlanService? plans = null,
    IServiceScopeFactory? scopeFactory = null)
{
    private const int PreviewConcurrency = 4;

    private static readonly HashSet<string> ArtifactAssessmentReasons = new(StringComparer.Ordinal)
    {
        "FILE_DELIVERY_RESULT_UNCERTAIN", "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED",
        "ProcessingPathConflict", "ExtractionPlanUnknown", "ArchiveInputRequired",
        "ProcessingInputNotMatched", "RepeatedContent", "ExtractionLayoutConflict",
    };
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt,
        IReadOnlyList<AcquisitionAdaptiveMigrationRow> Rows, Dictionary<int, string> Pins, int MaxIterations,
        Dictionary<int, AcquisitionPlanReplanInput> LocalInputs)> Previews = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record CheckedResult(AcquisitionAdaptiveMigrationRow Row, string? Pin,
        AcquisitionPlanReplanInput? LocalInput);

    public async Task<AcquisitionAdaptiveMigrationPreview> PreviewAsync(
        AcquisitionAdaptiveMigrationInput input, CancellationToken ct = default)
    {
        var maxIterations = Math.Clamp(input.MaxIterations, 1, 5);
        var ids = input.TaskIds?.Distinct().ToArray();
        if (ids is {Length: 0 or > 500} || ids?.Any(id => id <= 0) == true)
            throw new ArgumentException("ADAPTIVE_MIGRATION_SELECTION_INVALID");
        var query = db.Set<AcquisitionTaskDbModel>().AsNoTracking().Where(t => t.DeletedAt == null);
        if (ids != null) query = query.Where(t => ids.Contains(t.Id));
        var tasks = await query.OrderBy(t => t.Id).Take(500).ToListAsync(ct);
        var results = new CheckedResult[tasks.Count];
        var next = -1;
        var workers = Enumerable.Range(0, scopeFactory == null ? 1 : Math.Min(PreviewConcurrency, tasks.Count))
            .Select(_ => PreviewWorkerAsync()).ToArray();
        await Task.WhenAll(workers);

        var rows = results.Select(x => x.Row).ToList();
        var pins = new Dictionary<int, string>();
        var localInputs = new Dictionary<int, AcquisitionPlanReplanInput>();
        foreach (var result in results)
        {
            if (result.Row.CanMigrate && result.Pin != null) pins[result.Row.TaskId] = result.Pin;
            if (result.LocalInput != null) localInputs[result.Row.TaskId] = result.LocalInput;
        }

        async Task PreviewWorkerAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= tasks.Count) return;
                var task = tasks[index];
                if (scopeFactory == null)
                {
                    results[index] = await PreviewTaskAsync(task, maxIterations, ct);
                    continue;
                }

                await using var scope = scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<AcquisitionAdaptiveMigrationService>();
                results[index] = await worker.PreviewTaskAsync(task, maxIterations, ct);
            }
        }

        var token = Guid.NewGuid().ToString("N");
        var expires = DateTimeOffset.UtcNow.AddMinutes(10);
        Previews[token] = (expires, rows, pins, maxIterations, localInputs);
        foreach (var expired in Previews.Where(x => x.Value.ExpiresAt <= DateTimeOffset.UtcNow).Select(x => x.Key).ToArray())
            Previews.TryRemove(expired, out _);
        return new(token, expires, rows);
    }

    private async Task<CheckedResult> PreviewTaskAsync(AcquisitionTaskDbModel task, int maxIterations,
        CancellationToken ct)
    {
        var run = task.WorkflowRunId is { } runId
            ? await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct)
            : null;
        return await InspectInputsAsync(task, run, maxIterations, ct);
    }

    private async Task<CheckedResult> InspectInputsAsync(AcquisitionTaskDbModel task, WorkflowRunDbModel? run,
        int maxIterations, CancellationToken ct)
    {
        var row = Inspect(task, run);
        AcquisitionPlanReplanInput? localInput = null;
        // A local attempt uses the same directory eligibility as the single-task form.
        // It does not depend on the old activity's reason or an existing AI node.
        var owner = run?.Id.ToString(CultureInfo.InvariantCulture);
        if (plans != null && run != null && task.BuilderId != null &&
            run.Status is WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted &&
            await (from binding in db.ArtifactOperationBindings
                   join operation in db.ArtifactOperations on binding.OperationId equals operation.Id
                   where binding.OwnerKind == "workflow" && binding.OwnerId == owner && operation.OutputArtifactId != null
                   select binding.OperationId).AnyAsync(ct))
        {
            try
            {
                var detail = await plans.GetAsync(task.Id, ct);
                if (detail != null && CurrentDirectoryInput(detail, maxIterations) is { } input)
                {
                    localInput = input;
                    row = row with {CanMigrate = true, ReasonCode = "ADAPTIVE_MIGRATION_READY", Reason = null};
                }
                else if (detail?.ReplanArtifacts.Count > 0)
                    row = row with {CanMigrate = false, Reason = detail.ReplanArtifacts.Any(a => a.CanReadCurrentDirectory == true)
                        ? "PLAN_CURRENT_DIRECTORY_SELECTION_REQUIRED" : "PLAN_CURRENT_DIRECTORY_UNAVAILABLE"};
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                row = row with {CanMigrate = false, Reason = "PLAN_CURRENT_DIRECTORY_UNAVAILABLE"};
            }
        }
        if (plans != null && run == null && task.WorkflowRunId == null && task.Phase == "ready")
        {
            try
            {
                if (await plans.CanAnalyzePreparedFilesAsync(task, ct))
                    row = row with {CanMigrate = true, ReasonCode = "ADAPTIVE_MIGRATION_PREPARED_LOCAL_READY"};
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            { row = row with {Reason = "PLAN_REPLAN_EVIDENCE_UNAVAILABLE"}; }
        }
        return new(row, row.CanMigrate ? PreviewPin(task, run) : null, localInput);
    }

    public async Task<AcquisitionAdaptiveMigrationResult> ApplyAsync(string token,
        IReadOnlyList<int>? taskIds = null, CancellationToken ct = default)
    {
        if (!Previews.TryRemove(token, out var preview) || preview.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new ArgumentException("ADAPTIVE_MIGRATION_PREVIEW_EXPIRED");
        var applied = await ApplyReviewedAsync(preview.Rows, preview.Pins, preview.MaxIterations, preview.LocalInputs, taskIds, ct);
        return new(applied.Executions.Keys.ToArray(), applied.Skipped);
    }

    internal async Task<(string Fingerprint, bool UseCurrentStorage)?> PreviewForBatchAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel? run, int maxIterations, CancellationToken ct)
    {
        var result = await InspectForBatchAsync(task, run, maxIterations, ct);
        return result == null ? null : (result.Value.Fingerprint, result.Value.Checked.LocalInput?.UseCurrentStorageDefaults == true);
    }

    private async Task<(CheckedResult Checked, string Fingerprint)?> InspectForBatchAsync(AcquisitionTaskDbModel task,
        WorkflowRunDbModel? run, int maxIterations, CancellationToken ct)
    {
        if (maxIterations is < 1 or > 5) throw new ArgumentException("RECOVERY_ANALYSIS_OPTIONS_INVALID");
        if (task.DeletedAt != null || task.WorkflowRunId != run?.Id ||
            task.Status is AcquisitionStatus.Cancelled or AcquisitionStatus.Completed) return null;
        var check = await InspectInputsAsync(task, run, maxIterations, ct);
        if (!check.Row.CanMigrate || check.Pin == null) return null;
        var storage = check.LocalInput?.UseCurrentStorageDefaults == true ? plans!.CaptureLocalFilesStorage(task) : null;
        var fingerprint = AcquisitionLocalFilesRecoveryReceipt.Hash(new
        {
            TaskVersion = AcquisitionLocalRecoveryQueueEntry.ActionInputVersion(task, run),
            check.LocalInput, Storage = storage, MaxIterations = maxIterations
        });
        return (check, fingerprint);
    }

    internal async Task<(int WorkflowRunId, int PlanRevision)> ApplyForBatchAsync(int taskId, string fingerprint,
        int maxIterations, CancellationToken ct, Func<string?, Task>? guardDirectory = null)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == taskId && t.DeletedAt == null, ct);
        var run = task?.WorkflowRunId is { } runId
            ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        var checkedInput = task == null ? null : await InspectForBatchAsync(task, run, maxIterations, ct);
        if (checkedInput == null || checkedInput.Value.Fingerprint != fingerprint)
            throw new InvalidOperationException("ADAPTIVE_MIGRATION_CHANGED");
        var check = checkedInput.Value.Checked;
        var inputs = new Dictionary<int, AcquisitionPlanReplanInput>();
        if (check.LocalInput != null) inputs.Add(taskId, check.LocalInput);
        var result = await ApplyReviewedAsync([check.Row], new Dictionary<int, string> {[taskId] = check.Pin!},
            maxIterations, inputs, [taskId], ct, guardDirectory);
        if (result.Executions.TryGetValue(taskId, out var started)) return started;
        var skipped = result.Skipped.FirstOrDefault();
        throw new InvalidOperationException(skipped?.Reason ?? skipped?.ReasonCode ?? "ADAPTIVE_MIGRATION_FAILED");
    }

    private sealed record AppliedAnalysis(Dictionary<int, (int WorkflowRunId, int PlanRevision)> Executions,
        IReadOnlyList<AcquisitionAdaptiveMigrationRow> Skipped);

    private async Task<AppliedAnalysis> ApplyReviewedAsync(IReadOnlyList<AcquisitionAdaptiveMigrationRow> rows,
        IReadOnlyDictionary<int, string> pins, int maxIterations, IReadOnlyDictionary<int, AcquisitionPlanReplanInput> localInputs,
        IReadOnlyList<int>? taskIds, CancellationToken ct, Func<string?, Task>? guardDirectory = null)
    {
        var selected = (taskIds ?? rows.Where(x => x.CanMigrate).Select(x => x.TaskId).ToArray())
            .Distinct().ToHashSet();
        if (selected.Count == 0 || selected.Any(id => !pins.ContainsKey(id)))
            throw new ArgumentException("ADAPTIVE_MIGRATION_SELECTION_INVALID");

        var executions = new Dictionary<int, (int, int)>();
        var skipped = new List<AcquisitionAdaptiveMigrationRow>();
        foreach (var id in selected.Order())
        {
            ct.ThrowIfCancellationRequested();
            if (localInputs.TryGetValue(id, out var input))
            {
                // Replan and Run own their task/run gates and optimistic revisions. Never
                // call them under the legacy migration gate or mutate the previous snapshot.
                try
                {
                    var currentTask = await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
                        .SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct);
                    var currentRun = currentTask?.WorkflowRunId is { } runId
                        ? await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
                    if (plans == null || currentTask == null || currentRun == null ||
                        pins[id] != PreviewPin(currentTask, currentRun))
                        throw new InvalidOperationException("ADAPTIVE_MIGRATION_CHANGED");
                    var revised = await plans.ReplanAsync(id, input, ct, guardDirectory);
                    var started = await plans.RunAsync(id, revised.Task.PlanRevision, ct);
                    if (started.Task.WorkflowRunId == null || started.Task.WorkflowRunId == input.ExpectedRunId ||
                        started.Task.PlanRevision != revised.Task.PlanRevision)
                        throw new InvalidOperationException(started.Issues.FirstOrDefault(i => i.Blocking)?.Code ??
                            "ADAPTIVE_MIGRATION_PREPARED_NOT_STARTED");
                    executions.Add(id, (started.Task.WorkflowRunId.Value, started.Task.PlanRevision));
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
                {
                    skipped.Add(rows.Single(r => r.TaskId == id) with
                        {CanMigrate = false, ReasonCode = "ADAPTIVE_MIGRATION_FAILED", Reason = e.Message});
                }
                continue;
            }
            var gate = AcquisitionTaskGate.For(id);
            await gate.WaitAsync(ct);
            try
            {
                var reviewed = rows.Single(r => r.TaskId == id);
                if (reviewed.WorkflowRunId == null)
                {
                    var prepared = await db.Set<AcquisitionTaskDbModel>().AsNoTracking()
                        .SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct);
                    if (plans == null || prepared == null || prepared.WorkflowRunId != null ||
                        pins[id] != PreviewPin(prepared, null))
                    { skipped.Add(reviewed with {CanMigrate = false, ReasonCode = "ADAPTIVE_MIGRATION_CHANGED"}); continue; }
                    var started = await plans.RunPreparedAnalysisWhileLockedAsync(prepared, maxIterations, ct);
                    if (started.Task.WorkflowRunId == null)
                        throw new InvalidOperationException(started.Issues.FirstOrDefault(i => i.Blocking)?.Code ??
                            "ADAPTIVE_MIGRATION_PREPARED_NOT_STARTED");
                    executions.Add(id, (started.Task.WorkflowRunId.Value, started.Task.PlanRevision));
                    continue;
                }
                var task = await db.Set<AcquisitionTaskDbModel>().SingleOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct);
                var run = task?.WorkflowRunId is { } runId
                    ? await db.Set<WorkflowRunDbModel>().SingleOrDefaultAsync(r => r.Id == runId, ct)
                    : null;
                var current = task == null ? null : Inspect(task, run);
                if (current == null || !current.CanMigrate)
                {
                    skipped.Add(current ?? rows.Single(r => r.TaskId == id) with
                        {CanMigrate = false, ReasonCode = "ADAPTIVE_MIGRATION_CHANGED"});
                    continue;
                }
                if (pins[id] != PreviewPin(task!, run!))
                {
                    skipped.Add(current with {CanMigrate = false, ReasonCode = "ADAPTIVE_MIGRATION_CHANGED"});
                    continue;
                }
                var plan = WorkflowRunPlans.Read(run!.PlanSnapshotJson)
                    ?? throw new InvalidOperationException("ADAPTIVE_MIGRATION_PLAN_MISSING");
                var nodes = plan.Nodes.ToArray();
                var cursor = run.CurrentStepIndex!.Value;
                var reviewedNode = nodes[cursor];
                var config = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(reviewedNode.ConfigJson,
                    WorkflowJson.Options) ?? throw new InvalidOperationException("ADAPTIVE_MIGRATION_CONFIG_INVALID");
                var upgraded = config with
                {
                    EnableAdaptiveAssessment = true,
                    MaxAdaptiveIterations = maxIterations,
                    AdaptiveContext = string.IsNullOrWhiteSpace(config.AdaptiveContext) ? task!.Title : config.AdaptiveContext
                };
                nodes[cursor] = reviewedNode with {ConfigJson = JsonSerializer.Serialize(upgraded, WorkflowJson.Options)};
                run.PlanSnapshotJson = WorkflowRunPlans.Serialize(plan with {Nodes = nodes});
                run.PendingSignalJson = null;
                run.ErrorMessage = null;
                task.Status = AcquisitionStatus.Pending;
                task.Phase = "executing";
                task.Error = null;
                task.WaitReason = null;
                task.CompletedAt = null;
                task.UpdatedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
                if (run.Status == WorkflowRunStatus.Waiting)
                    await resumer.ReprocessWaitingAsync(run.Id, ct);
                else if (run.Status is WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted)
                    await resumer.RequeueAsync(run.Id, ct);
                executions.Add(id, (run.Id, task.PlanRevision));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                var task = await db.Set<AcquisitionTaskDbModel>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
                var run = task?.WorkflowRunId is { } runId
                    ? await db.Set<WorkflowRunDbModel>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct)
                    : null;
                if (task != null) skipped.Add(Inspect(task, run) with
                    {CanMigrate = false, ReasonCode = "ADAPTIVE_MIGRATION_FAILED", Reason = e.Message});
            }
            finally { gate.Release(); }
        }
        return new(executions, skipped);
    }

    internal static AcquisitionPlanReplanInput? CurrentDirectoryInput(AcquisitionPlanTaskDetail detail, int maxIterations)
    {
        if (detail.Task.WorkflowRunId is not { } runId || detail.Plan is not { } plan || plan.Parts.Count == 0)
            return null;
        var items = new List<AcquisitionPlanProcessingReplacement>();
        foreach (var part in plan.Parts)
        {
            var candidates = detail.ReplanArtifacts.Where(a => a.PartId == part.Id && a.CanReadCurrentDirectory == true).ToArray();
            var stage = detail.StageDirectories?.Parts.GetValueOrDefault(part.Id);
            var processing = stage?.Processing;
            var hasProcessingResult = processing != null && (processing.Evidence == "processingRecord" ||
                detail.ReplanArtifacts.Any(a => a.PartId == part.Id && ArtifactManifestFiles.PathComparer.Equals(a.RootDirectory, processing.Directory)));
            var preferred = hasProcessingResult ? processing!.Directory : stage?.Download?.Directory;
            if (preferred != null)
                candidates = candidates.Where(a => ArtifactManifestFiles.PathComparer.Equals(a.RootDirectory, preferred)).ToArray();
            // Multiple historical snapshots of the same current directory are equivalent
            // inputs for a new capture. Different directories require the user's choice.
            if (candidates.Select(a => a.RootDirectory).Distinct(ArtifactManifestFiles.PathComparer).Count() != 1)
                return null;
            var candidate = candidates.OrderBy(a => a.ArtifactId, StringComparer.Ordinal).First();
            items.Add(new(part.Id, new() {Requirement = "unknown"}, [candidate.ArtifactId], candidate.ArtifactId)
                {InputMode = "currentDirectory", UseAdaptiveAssessment = true});
        }
        return new(detail.Task.PlanRevision, runId, "analysis.currentDirectory", items,
            EnableAdaptiveAssessment: true, MaxAdaptiveIterations: maxIterations, UseCurrentStorageDefaults: true);
    }

    private static string PreviewPin(AcquisitionTaskDbModel task, WorkflowRunDbModel? run) => run == null
        ? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {task.Id, task.WorkflowRunId, task.Status,
            task.Phase, task.PlanRevision, task.PlanJson, task.SourceRefsJson, task.BuilderStateJson}))) :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            TaskId = task.Id, task.PlanRevision, TaskStatus = task.Status, RunId = run.Id, run.Status, run.CurrentStepIndex, run.WaitingSince,
            run.WaitReason, run.WaitPromptJson, run.PlanSnapshotJson, run.PendingSignalJson, run.CurrentItemJson
        }))));

    internal static AcquisitionAdaptiveMigrationRow Inspect(AcquisitionTaskDbModel task, WorkflowRunDbModel? run)
    {
        if (run == null)
            return new(task.Id, task.Title, null, null, null, false, false, "ADAPTIVE_MIGRATION_RUN_MISSING");
        if (run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running)
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_RUN_ACTIVE");
        if (run.Status is WorkflowRunStatus.Success)
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_RUN_COMPLETED");
        WorkflowRunPlan? plan;
        try { plan = WorkflowRunPlans.Read(run.PlanSnapshotJson); }
        catch (Exception e) when (e is InvalidDataException or ArgumentException) { plan = null; }
        if (plan == null)
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_PLAN_INVALID");
        if (run.WaitReason == "cloud.fileProcessing" && run.WaitPromptJson is {Length: > 0 and <= 1048576} wait)
        {
            try
            {
                using var prompt = JsonDocument.Parse(wait);
                if (prompt.RootElement.ValueKind == JsonValueKind.Object &&
                    prompt.RootElement.TryGetProperty("reason", out var reason) &&
                    reason.ValueKind == JsonValueKind.String && reason.GetString() == "PasswordUnknown")
                    return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                        "ADAPTIVE_MIGRATION_PASSWORD_REQUIRED");
            }
            catch (JsonException) { }
        }
        var assessment = plan.Nodes.Where(x => x.Kind == AssessFileArtifactDeliveryActivity.ActivityKind).ToArray();
        if (assessment.Length == 0)
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_NO_ASSESSMENT_NODE");
        // The adaptive continuation is only safe when the stopped run has recorded the
        // concrete artifact being reviewed. A cursor at the assessment node alone is not
        // enough: legacy runs may have reached the node before registering any local files.
        if (run.WaitReason != "cloud.fileProcessing" || string.IsNullOrWhiteSpace(run.WaitPromptJson) ||
            run.WaitPromptJson.Length > 1024 * 1024)
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_ARTIFACT_MISSING");
        try
        {
            using var prompt = JsonDocument.Parse(run.WaitPromptJson);
            var root = prompt.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            var reason = root.TryGetProperty("reason", out var reasonValue) && reasonValue.ValueKind == JsonValueKind.String
                ? reasonValue.GetString() : null;
            var artifactId = root.TryGetProperty("artifactId", out var artifactValue) && artifactValue.ValueKind == JsonValueKind.String
                ? artifactValue.GetString() : null;
            if (string.IsNullOrWhiteSpace(artifactId) || string.IsNullOrWhiteSpace(reason) ||
                !ArtifactAssessmentReasons.Contains(reason))
                return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                    "ADAPTIVE_MIGRATION_ARTIFACT_MISSING");
        }
        catch (JsonException)
        {
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_ARTIFACT_MISSING");
        }
        // A stopped run is only safe to re-enter when its persisted cursor already points at
        // the delivery-assessment activity. Requeueing a run whose cursor is still on a source,
        // transfer, or download node could repeat network work. Legacy batch runs without a
        // cursor remain a manual-recovery case until their checkpoint is reconciled explicitly.
        if (run.CurrentStepIndex is not { } cursor || cursor < 0 || cursor >= plan.Nodes.Count ||
            plan.Nodes[cursor].Kind != AssessFileArtifactDeliveryActivity.ActivityKind)
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, false,
                "ADAPTIVE_MIGRATION_CURSOR_NOT_DELIVERY_ASSESSMENT",
                "当前检查点尚未进入下载后文件评估节点，继续可能重复下载或转存。");
        bool enabled;
        try
        {
            var config = JsonSerializer.Deserialize<FileArtifactDeliveryAssessmentConfig>(plan.Nodes[cursor].ConfigJson, WorkflowJson.Options);
            enabled = config?.EnableAdaptiveAssessment == true;
        }
        catch (JsonException)
        {
            return new(task.Id, task.Title, run.Id, run.Status, cursor, false, false, "ADAPTIVE_MIGRATION_PLAN_INVALID");
        }
        if (run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted))
            return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, false, enabled,
                "ADAPTIVE_MIGRATION_RUN_UNSUPPORTED");
        return new(task.Id, task.Title, run.Id, run.Status, run.CurrentStepIndex, true, enabled,
            enabled ? "ADAPTIVE_MIGRATION_ALREADY_ENABLED" : "ADAPTIVE_MIGRATION_READY");
    }
}
