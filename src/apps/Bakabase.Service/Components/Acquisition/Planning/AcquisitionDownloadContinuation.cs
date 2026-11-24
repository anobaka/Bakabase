using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Persisted execution intent, scoped to one run. Missing legacy intent never authorizes automation.</summary>
public sealed record AcquisitionDownloadContinuation(int WorkflowRunId, string Mode, DateTimeOffset UpdatedAt,
    DateTimeOffset? NextCheckAt = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovedPausedSignalHash { get; init; }
}

public sealed class AcquisitionDownloadContinuationService(BakabaseDbContext db)
{
    public const string Automatic = "automatic", DownloadsOnly = "downloadsOnly", Paused = "paused", Manual = "manual";

    public static AcquisitionDownloadContinuation? Read(AcquisitionTaskDbModel task)
    {
        try
        {
            var state = string.IsNullOrWhiteSpace(task.BuilderStateJson) ? null :
                JsonNode.Parse(task.BuilderStateJson)?["downloadContinuation"]?.Deserialize<AcquisitionDownloadContinuation>(WorkflowJson.Options);
            return state != null && state.WorkflowRunId == task.WorkflowRunId && state.Mode is Automatic or DownloadsOnly or Paused ? state : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    public static string WithState(string? json, AcquisitionDownloadContinuation state)
    {
        var root = string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidOperationException("ACQUISITION_CHECKPOINT_INVALID");
        root["downloadContinuation"] = JsonSerializer.SerializeToNode(state, WorkflowJson.Options);
        return root.ToJsonString(WorkflowJson.Options);
    }

    internal static bool IsPausedDownloadOwner(AcquisitionTaskDbModel task, WorkflowRunDbModel run)
    {
        if (task.DeletedAt != null || task.WorkflowRunId != run.Id || task.PlanRevision < 1 ||
            Read(task) is not {Mode: Paused} state) return false;
        // Startup can interrupt an approved request before its saved answer reached the
        // download node. Fresh confirmation may replace only our own parked answer.
        if (run.PendingSignalJson is {} pending && (run.Status != WorkflowRunStatus.Interrupted ||
            state.ApprovedPausedSignalHash != SignalHash(pending))) return false;
        return run.Status is WorkflowRunStatus.Waiting or WorkflowRunStatus.Interrupted ||
            run.Status == WorkflowRunStatus.Cancelled && string.IsNullOrEmpty(run.ErrorMessage);
    }

    private static string SignalHash(string signal) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signal)));

    /// <summary>The caller holds the run gate. Acquisition commands also hold their owner gate;
    /// downloader stop callbacks use this compare-and-swap without reversing owner/downloader lock order.</summary>
    internal async Task<AcquisitionTaskDbModel> SaveWhileLockedAsync(AcquisitionTaskDbModel task, int runId,
        string mode, CancellationToken ct, DateTimeOffset? nextCheckAt = null, string? approvePausedSignal = null)
    {
        var state = new AcquisitionDownloadContinuation(runId, mode, DateTimeOffset.UtcNow, nextCheckAt)
            {ApprovedPausedSignalHash = approvePausedSignal == null ? null : SignalHash(approvePausedSignal)};
        var json = WithState(task.BuilderStateJson, state);
        var changed = await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.WorkflowRunId == runId &&
                t.DeletedAt == null && t.BuilderStateJson == task.BuilderStateJson)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct);
        if (changed != 1) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
        return task with {BuilderStateJson = json};
    }

    internal async Task<DateTimeOffset> ConsumePausedSignalAsync(int runId, string worksetId, string signal, CancellationToken ct)
    {
        var gate = WorkflowRunMutationGate.For(runId);
        await gate.WaitAsync(ct);
        try
        {
            var task = await ReadRunningOwnerAsync(runId, ct);
            if (task == null || worksetId != FormattableString.Invariant($"acquisition:{task.Id}:{task.PlanRevision}") ||
                Read(task) is not {Mode: Paused, ApprovedPausedSignalHash: { } expected} ||
                !string.Equals(expected, SignalHash(signal), StringComparison.Ordinal))
                throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
            var admitted = await SaveWhileLockedAsync(task, runId, Paused, ct);
            return Read(admitted)!.UpdatedAt;
        }
        finally { gate.Release(); }
    }

    internal async Task RequirePausedAdmissionWhileLockedAsync(int runId, DateTimeOffset admission, CancellationToken ct)
    {
        if (await ReadPausedAdmissionAsync(runId, admission, ct) == null)
            throw new InvalidOperationException("RECOVERY_DOWNLOAD_IDENTITY_CHANGED");
    }

    internal async Task ActivatePausedAdmissionAsync(int runId, DateTimeOffset admission, CancellationToken ct)
    {
        var gate = WorkflowRunMutationGate.For(runId);
        await gate.WaitAsync(ct);
        try
        {
            if (await ReadPausedAdmissionAsync(runId, admission, ct) is {} task)
                await SaveWhileLockedAsync(task, runId, Automatic, ct);
        }
        finally { gate.Release(); }
    }

    private async Task<AcquisitionTaskDbModel?> ReadPausedAdmissionAsync(int runId, DateTimeOffset admission, CancellationToken ct)
    {
        var task = await ReadRunningOwnerAsync(runId, ct);
        return task != null && Read(task) is {Mode: Paused, ApprovedPausedSignalHash: null} state && state.UpdatedAt == admission
            ? task : null;
    }

    private async Task<AcquisitionTaskDbModel?> ReadRunningOwnerAsync(int runId, CancellationToken ct)
    {
        if (!await db.WorkflowRuns.AsNoTracking().AnyAsync(r => r.Id == runId && r.Status == WorkflowRunStatus.Running &&
                r.PendingSignalJson == null, ct)) return null;
        return await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.WorkflowRunId == runId && t.DeletedAt == null, ct);
    }

    public async Task SetBackgroundTaskIntentAsync(string backgroundTaskId, string mode, CancellationToken ct = default)
    {
        const string prefix = "workflow.run.";
        if (!backgroundTaskId.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(backgroundTaskId[prefix.Length..], out var runId)) return;
        var ids = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == runId && t.DeletedAt == null)
            .Select(t => t.Id).ToArrayAsync(ct);
        foreach (var id in ids)
        {
            var ownerGate = AcquisitionTaskGate.For(id); await ownerGate.WaitAsync(ct);
            try
            {
                var runGate = WorkflowRunMutationGate.For(runId); await runGate.WaitAsync(ct);
                try
                {
                    var task = await db.AcquisitionTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id &&
                        t.WorkflowRunId == runId && t.DeletedAt == null, ct);
                    if (task != null) await SaveWhileLockedAsync(task, runId, mode, ct);
                }
                finally {runGate.Release();}
            }
            finally {ownerGate.Release();}
        }
    }
}

public sealed class AcquisitionDownloadContinuationStartupPolicy(BakabaseDbContext db) : IWorkflowRunStartupPolicy
{
    public async Task<bool> CanResumeOnStartupAsync(int runId, CancellationToken ct = default)
    {
        var owners = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == runId).ToArrayAsync(ct);
        return owners.All(t => AcquisitionDownloadContinuationService.Read(t)?.Mode is not
            (AcquisitionDownloadContinuationService.DownloadsOnly or AcquisitionDownloadContinuationService.Paused));
    }
}
