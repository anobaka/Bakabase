using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Serializes validation and submission, not the ensuing workflow. No purchases,
/// new sources or forced generations are authorized by this queue. A separately confirmed
/// reserve choice is applied only to its captured run at dispatch.</summary>
public sealed partial class AcquisitionContinuationService(BakabaseDbContext db, AcquisitionRecoveryService recovery,
    AcquisitionContinuationPreviewStore previews, AcquisitionPlanService plans, IMediaPathGuard paths,
    IArtifactOperationStore artifacts, BTaskManager? tasks = null)
{
    private static readonly SemaphoreSlim Execution = new(1, 1);
    public async Task<AcquisitionContinuationPreview> PreviewAsync(AcquisitionContinuationPreviewInput input,
        bool restricted, CancellationToken ct = default)
    {
        var ids = Ids(input.TaskIds);
        if (input.Scope is not ("workflow" or "downloads" or "localFiles")) throw new ArgumentException("RECOVERY_SCOPE_INVALID");
        if (input.Order is not ("selection" or "sizeAsc" or "sizeDesc")) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
        if (input.Order != "selection")
        {
            var sizes = await new AcquisitionSizeEstimateProjection(db).ReadAsync(ids, ct);
            ids = ids.OrderBy(id => sizes.GetValueOrDefault(id)?.Bytes == null)
                .ThenBy(id => (input.Order == "sizeDesc" ? -1 : 1) * (sizes.GetValueOrDefault(id)?.Bytes ?? 0)).ToArray();
        }
        var rows = new List<AcquisitionContinuationPreviewRow>();
        var evidence = new Dictionary<int, AcquisitionContinuationPreviewStore.Evidence>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var (task, run) = await Current(id, ct);
            var reason = Blocked(task, run);
            if (task != null && reason == null)
            {
                try
                {
                    var settings = Settings(task, input.Scope);
                    var directories = await CapturePaths(task, run, settings, ct);
                    await Guard(directories, restricted, ct);
                    evidence[id] = new(AcquisitionContinuationEntry.ContentVersion(task, run), settings, directories, restricted);
                }
                catch (UnauthorizedAccessException) {reason = "CONTINUATION_PATH_FORBIDDEN";}
                catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or IOException)
                {reason = "RECOVERY_CHECKPOINT_UNSUPPORTED";}
            }
            rows.Add(new(id, task?.Title, run?.Id, task?.PlanRevision ?? 0, reason == null, reason ?? "CONTINUATION_READY", CanOverrideStorage: reason == null && AcquisitionContinuationReserve.Supports(run)));
        }
        var preview = new AcquisitionContinuationPreview(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow.AddMinutes(10),
            input.Scope, input.Order, rows);
        ct.ThrowIfCancellationRequested(); previews.Add(new(preview, evidence));
        return preview;
    }

    public async Task<AcquisitionContinuation> ApplyAsync(AcquisitionContinuationApplyInput input,
        bool restricted, CancellationToken ct = default)
    {
        var ids = Ids(input.TaskIds).Order().ToArray();
        var admission = AcquisitionLocalRecoveryBatchPreviewStore.Gate;
        await admission.WaitAsync(ct);
        var held = new List<SemaphoreSlim>();
        try
        {
            var entry = previews.Get(input.PreviewToken);
            if (ids.Any(id => !entry.Evidence.ContainsKey(id))) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
            if (entry.BatchId != null)
            {
                if (!ids.SequenceEqual(entry.Selected!) || input.UseEmergencySpaceReserve != entry.UseEmergencySpaceReserve) throw new ArgumentException("RECOVERY_SELECTION_INVALID");
                return await GetAsync(entry.BatchId, ct);
            }
            foreach (var id in ids) {var gate = AcquisitionTaskGate.For(id); await gate.WaitAsync(ct); held.Add(gate);}
            previews.Get(input.PreviewToken);
            var current = new Dictionary<int, AcquisitionTaskDbModel>();
            foreach (var id in ids)
            {
                var (task, run) = await Current(id, ct); var e = entry.Evidence[id];
                if (task == null || Blocked(task, run) != null || AcquisitionContinuationEntry.ContentVersion(task, run) != e.Version ||
                    Settings(task, entry.Preview.Scope) != e.Settings) throw new ArgumentException("ACQUISITION_TASK_CHANGED");
                await Guard(e.Paths, restricted || e.Restricted, ct); current[id] = task;
            }
            var batch = Guid.NewGuid().ToString("N"); var now = DateTimeOffset.UtcNow; var index = 0;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            foreach (var row in entry.Preview.Rows.Where(r => ids.Contains(r.TaskId)))
            {
                var e = entry.Evidence[row.TaskId]; var task = current[row.TaskId];
                await Save(task, new() {BatchId = batch, PreviewToken = input.PreviewToken, Scope = entry.Preview.Scope, Version = e.Version, Settings = e.Settings,
                    Paths = e.Paths, Restricted = restricted || e.Restricted, WorkflowRunId = task.WorkflowRunId,
                    PlanRevision = task.PlanRevision, CreatedAt = now, UpdatedAt = now, Order = index++,
                    UseEmergencySpaceReserve = row.CanOverrideStorage ? input.UseEmergencySpaceReserve : null}, ct);
            }
            await transaction.CommitAsync(ct);
            entry.BatchId = batch; entry.Selected = ids; entry.UseEmergencySpaceReserve = input.UseEmergencySpaceReserve;
            return await GetAsync(batch, ct);
        }
        finally {foreach (var gate in held.AsEnumerable().Reverse()) gate.Release(); admission.Release();}
    }

    public async Task<IReadOnlyList<AcquisitionContinuation>> ListAsync(CancellationToken ct = default) =>
        History(await Rows(ct)).GroupBy(t => AcquisitionContinuationEntry.Read(t)!.BatchId).Select(g => Project(g.ToArray()))
            .OrderByDescending(g => g.Status is "checking" or "queued").ThenByDescending(g => g.CreatedAt).Take(30).ToArray();
    public async Task<AcquisitionContinuation> GetAsync(string id, CancellationToken ct = default)
    {
        RequireId(id); var found = History(await Rows(ct)).Where(t => AcquisitionContinuationEntry.Read(t)!.BatchId == id).ToArray();
        if (found.Length == 0) throw new ArgumentException("CONTINUATION_NOT_FOUND");
        return Project(found);
    }
    public async Task<AcquisitionContinuation> GetByPreviewAsync(string token, CancellationToken ct = default)
    {
        RequireId(token);
        var found = History(await Rows(ct)).Where(t => AcquisitionContinuationEntry.Read(t)!.PreviewToken == token).ToArray();
        if (found.Length == 0) throw new ArgumentException("CONTINUATION_NOT_FOUND");
        return Project(found);
    }
    public async Task<AcquisitionContinuation> CancelAsync(string id, CancellationToken ct = default)
    {
        RequireId(id); await AcquisitionLocalRecoveryBatchPreviewStore.Gate.WaitAsync(ct);
        try
        {
            foreach (var row in (await Rows(ct)).Where(t => AcquisitionContinuationEntry.Read(t) is {Status: "queued"} state && state.BatchId == id).OrderBy(t => t.Id))
            {
                var gate = AcquisitionTaskGate.For(row.Id); if (!await gate.WaitAsync(0, ct)) continue;
                try
                {
                    var (current, _) = await Current(row.Id, ct); var state = current == null ? null : AcquisitionContinuationEntry.Read(current);
                    if (state?.BatchId == id && state.Status == "queued") await Save(current!, state with {Status = "cancelled"}, ct);
                }
                finally {gate.Release();}
            }
        }
        finally {AcquisitionLocalRecoveryBatchPreviewStore.Gate.Release();}
        return await GetAsync(id, ct);
    }

    // Called once on process startup. A request interrupted after its dispatch fence is never
    // submitted a second time merely because its response/result was not persisted.
    internal async Task RecoverInterruptedAsync(CancellationToken ct)
    {
        foreach (var row in await Rows(ct))
            if (AcquisitionContinuationEntry.Read(row) is {Status: "checking"} state)
                await Save(row, state with {Status = "attention", ReasonCode = "RECOVERY_OUTCOME_UNKNOWN"}, ct);
    }

    internal async Task TickAsync(CancellationToken ct)
    {
        if (!await Execution.WaitAsync(0, ct)) return;
        try {await TickCoreAsync(ct);}
        finally {Execution.Release();}
    }
    private async Task TickCoreAsync(CancellationToken ct)
    {
        if (tasks?.IsShuttingDown == true || !await AcquisitionLocalRecoveryBatchPreviewStore.Gate.WaitAsync(0, ct)) return;
        AcquisitionTaskDbModel? selected = null; AcquisitionContinuationEntry? dispatch = null;
        try
        {
            var rows = await Rows(ct);
            foreach (var interrupted in rows.Where(t => AcquisitionContinuationEntry.Read(t)!.Status == "checking"))
                await Save(interrupted, AcquisitionContinuationEntry.Read(interrupted)! with
                    {Status = "attention", ReasonCode = "RECOVERY_OUTCOME_UNKNOWN"}, ct);
            selected = rows.Where(t => AcquisitionContinuationEntry.Read(t)!.Status == "queued")
                .OrderBy(t => AcquisitionContinuationEntry.Read(t)!.CreatedAt).ThenBy(t => AcquisitionContinuationEntry.Read(t)!.Order).FirstOrDefault();
            if (selected == null) return;
            var gate = AcquisitionTaskGate.For(selected.Id); if (!await gate.WaitAsync(0, ct)) return;
            try
            {
                var (current, run) = await Current(selected.Id, ct); dispatch = current == null ? null : AcquisitionContinuationEntry.Read(current);
                if (current == null || dispatch?.Status != "queued") {dispatch = null; return;}
                string? preparationFailure = null;
                var changed = false;
                // These checks precede the durable dispatch fence. A malformed related
                // download receipt or unavailable path belongs to this row, not an endless
                // retry of the queue head. Persistence/dispatch ambiguity is handled below.
                try
                {
                    changed = AcquisitionContinuationEntry.ContentVersion(current, run) != dispatch.Version ||
                        Settings(current, dispatch.Scope) != dispatch.Settings || current.DeletedAt != null ||
                        AcquisitionLocalRecoveryQueueEntry.Read(current) is {IsOpen: true};
                    if (!changed)
                    {
                        var currentPaths = await CapturePaths(current, run, dispatch.Settings, ct);
                        changed = !currentPaths.SequenceEqual(dispatch.Paths);
                        if (!changed) await Guard(currentPaths, dispatch.Restricted, ct);
                    }
                }
                catch (UnauthorizedAccessException)
                {preparationFailure = "CONTINUATION_PATH_FORBIDDEN";}
                catch (Exception e) when (e is JsonException or ArgumentException or IOException)
                {preparationFailure = "RECOVERY_CHECKPOINT_UNSUPPORTED";}
                catch (InvalidOperationException e) when (e.Message.StartsWith("RECOVERY_", StringComparison.Ordinal) ||
                    e.Message.StartsWith("PLAN_", StringComparison.Ordinal) || e.Message == "ACQUISITION_CHECKPOINT_INVALID")
                {preparationFailure = SafeCode(e.Message);}
                if (preparationFailure != null)
                {await Save(current, dispatch with {Status = "attention", ReasonCode = preparationFailure}, ct); dispatch = null; return;}
                if (changed)
                {await Save(current, dispatch with {Status = "skipped", ReasonCode = "ACQUISITION_TASK_CHANGED"}, ct); dispatch = null; return;}
                await Save(current, dispatch with {Status = "checking"}, ct);
            }
            finally {gate.Release();}
        }
        finally {AcquisitionLocalRecoveryBatchPreviewStore.Gate.Release();}
        if (selected == null || dispatch == null) return;
        var status = "attention"; string? reason = "RECOVERY_OUTCOME_UNKNOWN";
        try
        {
            async Task GuardDirectory(string? path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                await Guard([path], dispatch.Restricted, ct);
            }
            var result = await recovery.ContinueDeferredAsync(selected.Id, dispatch.Scope, dispatch.Version, ct, GuardDirectory, dispatch.Settings, dispatch.UseEmergencySpaceReserve);
            status = result.ContinuedTaskIds.Contains(selected.Id) ? "submitted" : "attention";
            reason = status == "submitted" ? null : result.Skipped.FirstOrDefault()?.ReasonCode ?? result.LocalFiles?.FirstOrDefault()?.ReasonCode ?? "RECOVERY_MANUAL_REVIEW";
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {reason = SafeCode(e.Message);}
        // On shutdown leave checking intact for explicit indeterminate recovery at startup.
        ct.ThrowIfCancellationRequested();
        var finishGate = AcquisitionTaskGate.For(selected.Id); await finishGate.WaitAsync(ct);
        try
        {
            var (current, _) = await Current(selected.Id, ct); var state = current == null ? null : AcquisitionContinuationEntry.Read(current);
            if (state?.BatchId == dispatch.BatchId && state.Status == "checking")
                await Save(current!, state with {Status = status, ReasonCode = reason, WorkflowRunId = current!.WorkflowRunId}, ct);
        }
        finally {finishGate.Release();}
    }

    private string? Blocked(AcquisitionTaskDbModel? task, WorkflowRunDbModel? run) => task switch
    {
        null => "ACQUISITION_TASK_NOT_FOUND",
        {DeletedAt: not null} => "ACQUISITION_TASK_DELETED",
        {Status: AcquisitionStatus.Completed or AcquisitionStatus.Cancelled} => "RECOVERY_ALREADY_COMPLETED",
        _ when AcquisitionLocalRecoveryQueueEntry.Read(task) is {IsOpen: true} || AcquisitionContinuationEntry.Read(task) is {IsOpen: true} => "LOCAL_BATCH_ALREADY_QUEUED",
        _ when run?.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running || run?.PendingSignalJson != null ||
               run != null && tasks?.IsPending("workflow.run." + run.Id) == true => "ACQUISITION_TASK_ACTIVE",
        _ when task.BuilderId == null || task.PlanJson == null => "RECOVERY_NO_RUN",
        _ => null,
    };
    private string Settings(AcquisitionTaskDbModel task, string scope) => scope == "localFiles"
        ? JsonSerializer.Serialize(plans.CaptureLocalFilesStorage(task), WorkflowJson.Options) : "{}";
    internal async Task Guard(IEnumerable<string> values, bool restricted, CancellationToken ct)
    {
        if (!restricted) return;
        foreach (var path in values)
            if (!await paths.IsServableAsync(path, ct)) throw new UnauthorizedAccessException("CONTINUATION_PATH_FORBIDDEN");
    }
    private async Task<(AcquisitionTaskDbModel?, WorkflowRunDbModel?)> Current(int id, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        return (task, task?.WorkflowRunId is { } run ? await db.WorkflowRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == run, ct) : null);
    }
    private async Task Save(AcquisitionTaskDbModel task, AcquisitionContinuationEntry state, CancellationToken ct)
    {
        var json = AcquisitionContinuationEntry.Write(task.BuilderStateJson, state with {UpdatedAt = DateTimeOffset.UtcNow});
        if (await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.BuilderStateJson == task.BuilderStateJson &&
                t.WorkflowRunId == task.WorkflowRunId && t.PlanRevision == task.PlanRevision)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct) != 1) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
    }
    private async Task<AcquisitionTaskDbModel[]> Rows(CancellationToken ct) =>
        (await db.AcquisitionTasks.AsNoTracking().Where(t => t.BuilderStateJson != null && t.BuilderStateJson.Contains("\"continuationRequest\""))
            .ToArrayAsync(ct)).Where(t => AcquisitionContinuationEntry.Read(t) != null).ToArray();
    private static IEnumerable<AcquisitionTaskDbModel> History(IEnumerable<AcquisitionTaskDbModel> rows)
    {
        foreach (var task in rows)
        {
            yield return task;
            if (JsonNode.Parse(task.BuilderStateJson!)?["continuationRequestHistory"] is not JsonArray history) continue;
            foreach (var old in history)
                if (old?.Deserialize<AcquisitionContinuationEntry>(WorkflowJson.Options) is { } state)
                    yield return task with {BuilderStateJson = AcquisitionContinuationEntry.Write("{}", state)};
        }
    }
    private static AcquisitionContinuation Project(AcquisitionTaskDbModel[] rows)
    {
        var values = rows.Select(t => (Task: t, State: AcquisitionContinuationEntry.Read(t)!)).OrderBy(v => v.State.Order).ToArray();
        var status = values.Any(v => v.State.Status == "checking") ? "checking" : values.Any(v => v.State.Status == "queued") ? "queued" :
            values.All(v => v.State.Status == "cancelled") ? "cancelled" : "finished";
        return new(values[0].State.BatchId, values[0].State.Scope, values[0].State.CreatedAt, values.Max(v => v.State.UpdatedAt), status,
            values.Select(v => new AcquisitionContinuationRow(v.Task.Id, v.Task.Title, v.State.Order, v.State.Status, v.State.WorkflowRunId, v.State.ReasonCode)).ToArray());
    }
    private static int[] Ids(IReadOnlyList<int>? ids) => ids == null || ids.Count is 0 or > 1000 || ids.Any(i => i <= 0)
        ? throw new ArgumentException("RECOVERY_SELECTION_INVALID") : ids.Distinct().ToArray();
    private static void RequireId(string id) {if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("CONTINUATION_NOT_FOUND");}
    private static string SafeCode(string value) => value.Length <= 100 && value.All(c => c is >= 'A' and <= 'Z' or '_')
        ? value : "RECOVERY_OUTCOME_UNKNOWN";
}

public sealed class AcquisitionContinuationWorker(IServiceScopeFactory scopes, ILogger<AcquisitionContinuationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialized = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<AcquisitionContinuationService>();
                if (!initialized) {await service.RecoverInterruptedAsync(stoppingToken); initialized = true;}
                await service.TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
            catch (Exception) {logger.LogWarning("Acquisition continuation request is waiting for review; no request was repeated.");}
            try {await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);}
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {return;}
        }
    }
}
