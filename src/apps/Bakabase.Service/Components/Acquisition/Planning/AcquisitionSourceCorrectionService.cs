using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Service.Components.CloudDownloads;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Edits the saved execution source, not the parser output or immutable acquisition plan.
/// The original cursor consumes an append-only correction signal; earlier steps are never restarted.</summary>
public sealed class AcquisitionSourceCorrectionService(BakabaseDbContext db, CloudOperationService operations,
    IWorkflowRunResumer resumer, BTaskManager? runningTasks = null)
{
    private sealed record State(AcquisitionTaskDbModel? Task, WorkflowRunDbModel? Run,
        DownloadWorkset? Workset, string? Kind, string? BlockReason);

    public async Task<AcquisitionSourceCorrections> GetAsync(int taskId, CancellationToken ct = default)
    {
        var state = await ReadAsync(taskId, ct);
        var displayWorkset = state.Workset;
        if (displayWorkset == null && state.Run is {Status: WorkflowRunStatus.Success} completed)
            displayWorkset = (await AcquisitionCompletedSourceProjection.ReadAsync(db, [completed.Id], ct)).GetValueOrDefault(completed.Id);
        var rows = new List<AcquisitionSourceCorrectionRow>();
        foreach (var target in displayWorkset?.Targets ?? [])
        {
            if (target.SelectedSource is not {} source) continue;
            var reason = state.BlockReason ?? await operations.AccessCodeCorrectionBlockReasonAsync(state.Run!.Id, state.Kind!, target, ct);
            rows.Add(new(target.Id, source.Id, CloudSourceIdentity.Key(source), source.Url, source.ProviderId,
                source.AccessCode, reason == null, reason, (source.AccessCodeRevisions ?? [])
                    .Select(r => new AcquisitionSourceCorrectionHistory(r.CorrectionId, r.PreviousAccessCode, r.NewAccessCode, r.AppliedAt)).ToArray(), source));
        }
        var available = rows.Any(s => s.CanCorrect);
        return new(taskId, state.Task?.PlanRevision ?? 0, state.Run?.Id,
            state.Task != null && state.Run != null ? Version(state.Task, state.Run) : null,
            available, available ? null : state.BlockReason ?? rows.FirstOrDefault()?.ReasonCode ?? "SOURCE_CORRECTION_SOURCE_UNSUPPORTED", rows);
    }

    public async Task<AcquisitionSourceCorrectionResult> CorrectAsync(int taskId, AcquisitionSourceCorrectionInput input,
        CancellationToken ct = default, Func<IReadOnlyList<string>, CancellationToken, Task>? guardDirectories = null)
    {
        ValidateInput(input);
        var gate = AcquisitionTaskGate.For(taskId);
        await gate.WaitAsync(ct);
        try
        {
            var state = await ReadAsync(taskId, ct);
            var correction = await ValidateCurrentAsync(state, input, ct);
            if (guardDirectories != null) await guardDirectories(SavedDirectories(state), ct);
            var signal = JsonSerializer.Serialize(new CloudSelectionSignal {SourceCredentialCorrections = [correction]}, WorkflowJson.Options);
            await resumer.ContinueConditionallyAsync(input.ExpectedRunId, state.Run!.Status == WorkflowRunStatus.Waiting,
                signal, async token =>
                {
                    // Runs inside the workflow mutation gate, nested after the owning task gate.
                    var current = await ReadAsync(taskId, token);
                    await ValidateCurrentAsync(current, input, token);
                    if (guardDirectories != null) await guardDirectories(SavedDirectories(current), token);
                }, ct);
            return new(taskId, input.ExpectedRunId, true);
        }
        finally { gate.Release(); }
    }

    private static IReadOnlyList<string> SavedDirectories(State state)
    {
        AcquisitionPlan? plan = null;
        if (state.Task?.PlanJson is {} json)
        {
            try { plan = JsonSerializer.Deserialize<AcquisitionPlan>(json, WorkflowJson.Options); }
            catch (JsonException) { throw new InvalidOperationException("SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED"); }
        }
        var configuredDirectories = WorkflowRunPlans.Read(state.Run?.PlanSnapshotJson)?.Nodes
            .Skip(state.Run?.CurrentStepIndex ?? 0)
            .Select(n => JsonSerializer.Deserialize<CloudWorkflowConfig>(n.ConfigJson, WorkflowJson.Options)?.OutputDirectory)
            ?? [];
        return new[] {state.Task?.TargetDirectory, state.Workset?.OutputDirectory, plan?.OutputDirectory,
                plan?.DeliveryLayout?.RootDirectory, plan?.DeliveryLayout?.TaskDirectory}
            .Concat(plan?.DeliveryLayout?.PartDirectories.Values.AsEnumerable() ?? [])
            .Concat(configuredDirectories)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task<CloudSourceCredentialCorrection> ValidateCurrentAsync(State state,
        AcquisitionSourceCorrectionInput input, CancellationToken ct)
    {
        if (state.Task == null || state.Run == null || state.Task.PlanRevision != input.ExpectedRevision ||
            state.Run.Id != input.ExpectedRunId || Version(state.Task, state.Run) != input.CheckpointVersion)
            throw new InvalidOperationException("SOURCE_CORRECTION_TASK_CHANGED");
        if (state.BlockReason != null) throw new InvalidOperationException(state.BlockReason);
        var target = state.Workset!.Targets.SingleOrDefault(t => t.Id == input.TargetId);
        if (target?.SelectedSource is not {} source || source.Id != input.SourceId ||
            CloudSourceIdentity.Key(source) != input.ExpectedSourceIdentity)
            throw new InvalidOperationException("SOURCE_CORRECTION_TASK_CHANGED");
        var reason = await operations.AccessCodeCorrectionBlockReasonAsync(state.Run.Id, state.Kind!, target, ct);
        if (reason != null) throw new InvalidOperationException(reason);
        if (CloudSourceIdentity.Key(source with {AccessCode = input.NewAccessCode}) == CloudSourceIdentity.Key(source))
            throw new ArgumentException("SOURCE_CORRECTION_CODE_UNCHANGED");
        var correction = new CloudSourceCredentialCorrection {CorrectionId = input.CorrectionId, TargetId = input.TargetId,
            SourceId = input.SourceId, ExpectedSourceIdentity = input.ExpectedSourceIdentity, NewAccessCode = input.NewAccessCode, Basis = "manual"};
        try
        {
            // This method only validates/read-projects revisions. Durable operations are created
            // later by the resumed activity, which repeats the same side-effect checks.
            await operations.ApplyCredentialCorrectionsAsync(state.Run.Id, state.Kind!, state.Workset, [correction], ct);
        }
        catch (ArgumentException) { throw new ArgumentException("SOURCE_CORRECTION_INVALID_INPUT"); }
        catch (InvalidOperationException) { throw new InvalidOperationException("SOURCE_CORRECTION_OPERATION_UNCERTAIN"); }
        return correction;
    }

    private async Task<State> ReadAsync(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        var run = task?.WorkflowRunId is {} runId
            ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct) : null;
        DownloadWorkset? workset = null;
        string? kind = null, checkpointProblem = null;
        try
        {
            if (run != null)
            {
                workset = AcquisitionEffectiveSourceProjection.Read(run.CurrentItemJson);
                if (workset?.Targets == null || workset.Targets.Any(t => t == null || t.Sources == null ||
                        t.Sources.Any(s => s == null))) workset = null;
                var definition = WorkflowRunPlans.Read(run.PlanSnapshotJson);
                var cursor = run.CurrentStepIndex ?? -1;
                var node = definition?.Nodes.ElementAtOrDefault(cursor);
                kind = node?.Kind;
                if (definition == null || node == null || definition.TriggerKind != CloudWorkflow.Trigger ||
                    kind is not ("baidu.transfer" or "download.enqueueSupported" or "baidu.download") ||
                    workset == null || workset.Targets.Count is 0 or > 100 ||
                    workset.Targets.Select(t => t.Id).Distinct(StringComparer.Ordinal).Count() != workset.Targets.Count ||
                    workset.Targets.Any(t => t.Sources == null || t.Sources.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != t.Sources.Count))
                    checkpointProblem = "SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED";
                if (definition != null && cursor >= 0)
                    foreach (var remaining in definition.Nodes.Skip(cursor))
                    {
                        if (remaining.Kind is not ("baidu.transfer" or "download.enqueueSupported" or "baidu.download" or
                            "download.awaitCompletion" or "acquisition.plan.process" or "fs.publishArtifacts"))
                            checkpointProblem = "SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED";
                        using var config = JsonDocument.Parse(remaining.ConfigJson);
                        if (config.RootElement.TryGetProperty("reusePolicy", out var reuse) && reuse.GetString() == "force")
                            checkpointProblem = "SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED";
                    }
            }
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { checkpointProblem = "SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED"; workset = null; }
        var reason = task == null ? "SOURCE_CORRECTION_TASK_NOT_FOUND" :
            task.DeletedAt != null ? "SOURCE_CORRECTION_TASK_DELETED" :
            task.Status == AcquisitionStatus.Cancelled || run?.Status == WorkflowRunStatus.Cancelled ? "SOURCE_CORRECTION_TASK_CANCELLED" :
            run == null ? "SOURCE_CORRECTION_NO_RUN" :
            run.Status is WorkflowRunStatus.Running or WorkflowRunStatus.Pending || runningTasks?.IsPending("workflow.run." + run.Id) == true ? "SOURCE_CORRECTION_TASK_ACTIVE" :
            run.Status == WorkflowRunStatus.Success ? "SOURCE_CORRECTION_ALREADY_COMPLETED" :
            run.PendingSignalJson != null ? "SOURCE_CORRECTION_PENDING_ANSWER" :
            run.Status is not (WorkflowRunStatus.Waiting or WorkflowRunStatus.Failed or WorkflowRunStatus.Interrupted) ? "SOURCE_CORRECTION_CHECKPOINT_UNSUPPORTED" :
            checkpointProblem;
        return new(task, run, workset, kind, reason);
    }

    private static void ValidateInput(AcquisitionSourceCorrectionInput input)
    {
        if (input.ExpectedRevision < 0 || input.ExpectedRunId <= 0 ||
            !Guid.TryParseExact(input.CorrectionId, "N", out _) || input.CheckpointVersion?.Length != 64 ||
            input.ExpectedSourceIdentity?.Length != 64 || string.IsNullOrWhiteSpace(input.TargetId) || input.TargetId.Length > 512 ||
            string.IsNullOrWhiteSpace(input.SourceId) || input.SourceId.Length > 512 || input.NewAccessCode?.Length > 512 ||
            input.NewAccessCode?.Any(char.IsControl) == true)
            throw new ArgumentException("SOURCE_CORRECTION_INVALID_INPUT");
    }
    private static string Version(AcquisitionTaskDbModel task, WorkflowRunDbModel run) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task) + "\n" + JsonSerializer.Serialize(run))));
}
