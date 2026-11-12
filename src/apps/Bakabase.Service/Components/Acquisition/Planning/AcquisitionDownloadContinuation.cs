using System;
using System.Linq;
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
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Persisted execution intent, scoped to one run. Missing legacy intent never authorizes automation.</summary>
public sealed record AcquisitionDownloadContinuation(int WorkflowRunId, string Mode, DateTimeOffset UpdatedAt,
    DateTimeOffset? NextCheckAt = null);

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

    /// <summary>The caller holds the run gate. Acquisition commands also hold their owner gate;
    /// downloader stop callbacks use this compare-and-swap without reversing owner/downloader lock order.</summary>
    internal async Task<AcquisitionTaskDbModel> SaveWhileLockedAsync(AcquisitionTaskDbModel task, int runId,
        string mode, CancellationToken ct, DateTimeOffset? nextCheckAt = null)
    {
        var state = new AcquisitionDownloadContinuation(runId, mode, DateTimeOffset.UtcNow, nextCheckAt);
        var json = WithState(task.BuilderStateJson, state);
        var changed = await db.AcquisitionTasks.Where(t => t.Id == task.Id && t.WorkflowRunId == runId &&
                t.DeletedAt == null && t.BuilderStateJson == task.BuilderStateJson)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.BuilderStateJson, json), ct);
        if (changed != 1) throw new InvalidOperationException("ACQUISITION_TASK_CHANGED");
        return task with {BuilderStateJson = json};
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
