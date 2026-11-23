using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Planning;

internal sealed record AcquisitionDeliveryRetirement(int Revision, int PreviousRunId, string PartId, string PreviousArtifactId,
    int? RunId = null, string? CurrentArtifactId = null, string? Status = null, int Attempts = 0);

/// <summary>Only future completed revisions enqueue work. Restart restores their explicit, unfinished intents.</summary>
public sealed class AcquisitionDeliveryRetirementQueue(IBOptions<AcquisitionOptions> options)
{
    private readonly ConcurrentDictionary<(int TaskId, int Revision), byte> _scheduled = new();
    internal bool Enabled => !options.Value.KeepSupersededDeliveryFiles;
    internal void Enqueue(int taskId, int revision) => _scheduled.TryAdd((taskId, revision), 0);
    internal (int TaskId, int Revision)[] Snapshot() => _scheduled.Keys.ToArray();
    internal void Complete((int TaskId, int Revision) key) => _scheduled.TryRemove(key, out _);
}

internal sealed class AcquisitionDeliveryRetirementWorker(IServiceScopeFactory scopes, AcquisitionDeliveryRetirementQueue queue,
    ILogger<AcquisitionDeliveryRetirementWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restored = false;
        var pendingCleanupCursor = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!restored)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await RestoreAsync(scope.ServiceProvider.GetRequiredService<BakabaseDbContext>(), stoppingToken);
                    restored = true;
                }
                foreach (var key in queue.Snapshot())
                {
                    try
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        if (await ProcessAsync(scope.ServiceProvider, key.TaskId, key.Revision, stoppingToken))
                            queue.Complete(key);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    { logger.LogWarning(ex, "Cannot process delivery retirement for acquisition {TaskId}, revision {Revision}.", key.TaskId, key.Revision); }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(ex, "Cannot restore pending acquisition delivery retirements."); }
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                pendingCleanupCursor = await scope.ServiceProvider.GetRequiredService<AcquisitionTaskDeletionCleanupService>()
                    .FinishPendingSmbBatchAsync(pendingCleanupCursor, stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            { logger.LogWarning(error, "Cannot finish pending SMB directory cleanup."); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    internal async Task RestoreAsync(BakabaseDbContext db, CancellationToken ct)
    {
        var rows = await db.AcquisitionTasks.AsNoTracking()
            .Where(t => t.DeletedAt == null && t.BuilderStateJson != null && t.BuilderStateJson.Contains("\"pendingDeliveryRetirements\":["))
            .Select(t => new {t.Id, t.BuilderStateJson}).ToListAsync(ct);
        foreach (var row in rows)
        foreach (var intent in Read(row.BuilderStateJson).PendingDeliveryRetirements ?? [])
            queue.Enqueue(row.Id, intent.Revision);
    }

    private static AcquisitionPlanBuilderState Read(string? value) => value == null ? new() :
        JsonSerializer.Deserialize<AcquisitionPlanBuilderState>(value, WorkflowJson.Options) ?? new();

    internal static async Task<bool> ProcessAsync(IServiceProvider services, int taskId, int revision, CancellationToken ct)
    {
        var db = services.GetRequiredService<BakabaseDbContext>();
        var task = await db.AcquisitionTasks.AsNoTracking().Where(t => t.Id == taskId && t.DeletedAt == null)
            .Select(t => new {t.PlanRevision, t.Status, t.WorkflowRunId, t.BuilderStateJson}).SingleOrDefaultAsync(ct);
        if (task == null) return true;
        var intents = Read(task.BuilderStateJson).PendingDeliveryRetirements?.Where(i => i.Revision == revision).ToArray() ?? [];
        if (intents.Length == 0) return true;
        if (task.PlanRevision == revision && (task.Status != AcquisitionStatus.Completed || task.WorkflowRunId == null ||
            !await db.Set<WorkflowRunDbModel>().AsNoTracking().AnyAsync(r => r.Id == task.WorkflowRunId && r.Status == WorkflowRunStatus.Success, ct)))
            return false;

        var done = true;
        foreach (var intent in intents)
        {
            var result = intent with {Status = "revision_changed"};
            var retirement = services.GetRequiredService<AcquisitionSupersededDeliveryService>();
            if (task.PlanRevision == revision && task.WorkflowRunId is { } run)
            {
                var owner = run.ToString(CultureInfo.InvariantCulture);
                var slot = intent.PartId + ":artifact.publish";
                var current = await (from binding in db.Set<ArtifactOperationBindingDbModel>()
                    join operation in db.Set<ArtifactOperationDbModel>() on binding.OperationId equals operation.Id
                    where binding.OwnerKind == "workflow" && binding.OwnerId == owner && binding.Slot == slot
                    select operation.OutputArtifactId).SingleOrDefaultAsync(ct);
                var replacementChanged = intent.RunId != null && (intent.RunId != run || intent.CurrentArtifactId != current);
                result = replacementChanged ? intent with {Status = "replacement_changed"} :
                    intent with {RunId = run, CurrentArtifactId = current, Status = "publication_missing", Attempts = intent.Attempts + 1};
                if (!replacementChanged && current != null && current != intent.PreviousArtifactId)
                {
                    // The service owns the acquisition and run gates, and rechecks the live retention setting under them.
                    var outcome = await retirement.RetireIfEnabledAsync(taskId,
                        new(revision, run, intent.PreviousArtifactId, current), ct);
                    result = result with {Status = outcome.Status};
                }
            }
            else if (await retirement.StartedInputAsync(taskId, revision, intent.PreviousArtifactId, ct) is { } started)
            {
                var outcome = await retirement.RetireIfEnabledAsync(taskId, started, ct);
                result = intent with {RunId = started.ExpectedRunId, CurrentArtifactId = started.CurrentArtifactId,
                    Status = outcome.Status, Attempts = intent.Attempts + 1};
            }
            // Once files may have moved, the durable receipt must remain restartable even
            // through a longer outage or a later task revision. Never discard that recovery intent.
            var retry = result.Status != "retired" &&
                (await retirement.StartedInputAsync(taskId, revision, intent.PreviousArtifactId, ct) != null ||
                 result.Attempts < 3 && result.Status is "busy" or "retained_or_pending");
            if (!await RecordAsync(db, taskId, intent, result, retry, ct) || retry) done = false;
        }
        return done;
    }

    private static async Task<bool> RecordAsync(BakabaseDbContext db, int taskId, AcquisitionDeliveryRetirement intent,
        AcquisitionDeliveryRetirement result, bool retry, CancellationToken ct)
    {
        var gate = AcquisitionTaskGate.For(taskId);
        await gate.WaitAsync(ct);
        try
        {
            var tasks = db.AcquisitionTasks.Where(t => t.Id == taskId && t.DeletedAt == null);
            var original = await tasks.Select(t => t.BuilderStateJson).SingleOrDefaultAsync(ct);
            var state = Read(original);
            var pending = state.PendingDeliveryRetirements?.ToList() ?? [];
            if (!pending.Remove(intent)) return false;
            if (retry) pending.Add(result);
            var updated = state with {PendingDeliveryRetirements = pending.Count == 0 ? null : pending,
                DeliveryRetirementResults = retry ? state.DeliveryRetirementResults : [..state.DeliveryRetirementResults ?? [], result]};
            var json = JsonSerializer.Serialize(updated, WorkflowJson.Options);
            return await tasks.Where(t => t.BuilderStateJson == original).ExecuteUpdateAsync(set => set.SetProperty(t => t.BuilderStateJson, json), ct) == 1;
        }
        finally {gate.Release();}
    }
}
