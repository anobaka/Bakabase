using System.Text.Json;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Services;

public class AcquisitionService<TDbContext>(
    TDbContext db,
    IResourceService resources,
    IReservedPropertyValueService reservedValues,
    IWorkflowDefinitionService workflows,
    IWorkflowRunResumer resumer,
    IWorkflowEventBus eventBus,
    IBOptions<AcquisitionOptions> options,
    AppService appService,
    ILogger<AcquisitionService<TDbContext>> logger) : IAcquisitionService, IAcquisitionQueue
    where TDbContext : DbContext
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DbSet<AcquisitionTaskDbModel> Tasks => db.Set<AcquisitionTaskDbModel>();
    private DbSet<WorkflowRunDbModel> Runs => db.Set<WorkflowRunDbModel>();
    private DbSet<WorkflowDefinitionDbModel> Defs => db.Set<WorkflowDefinitionDbModel>();

    /// <summary>A task that has not reached an end and therefore still counts against the limit.</summary>
    private static readonly AcquisitionStatus[] Live =
        [AcquisitionStatus.Pending, AcquisitionStatus.Running, AcquisitionStatus.Waiting];

    public async Task<AcquisitionTask> CreateAsync(int resourceId, AcquisitionLeadKind leadKind,
        string leadValue, int? leadId = null, int? recipeDefinitionId = null, int? collectionId = null,
        CancellationToken ct = default)
    {
        var resource = (await resources.GetAll(r => r.Id == resourceId)).FirstOrDefault()
                       ?? throw new InvalidOperationException($"Resource #{resourceId} does not exist.");

        var name = await ResolveName(resourceId) ?? $"Resource #{resourceId}";

        if (resource.HasLocalPath)
        {
            throw new InvalidOperationException(
                $"\"{name}\" already has local files — there is nothing to get.");
        }

        var existing = await Tasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.ResourceId == resourceId && Live.Contains(t.Status), ct);

        if (existing != null)
        {
            throw new InvalidOperationException(
                $"\"{name}\" is already being acquired (task #{existing.Id}).");
        }

        var recipe = await ResolveRecipe(recipeDefinitionId, leadKind, ct);

        var now = DateTime.Now;
        var task = new AcquisitionTaskDbModel
        {
            ResourceId = resourceId,
            CollectionId = collectionId,
            LeadKind = leadKind,
            LeadValue = leadValue,
            AcquisitionLeadId = leadId,
            RecipeDefinitionId = recipe.Id,
            Status = AcquisitionStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        // The common case is that there is room, so the user sees it start rather than queue.
        if (await CountLiveRunning(ct) < Concurrency)
        {
            await StartRun(task, await ResolveName(resourceId), ct);
        }

        await Publish(task, ct);

        return await Decorate(task.ToDomainModel(), ct);
    }

    public async Task<List<AcquisitionTask>> SearchAsync(AcquisitionStatus? status = null,
        int? resourceId = null, CancellationToken ct = default)
    {
        var query = Tasks.AsNoTracking().AsQueryable();

        if (status is { } s) query = query.Where(t => t.Status == s);
        if (resourceId is { } rid) query = query.Where(t => t.ResourceId == rid);

        var rows = await query.OrderByDescending(t => t.Id).ToListAsync(ct);

        return await DecorateMany(rows.Select(r => r.ToDomainModel()).ToList(), ct);
    }

    public async Task<AcquisitionTask?> GetAsync(int taskId, CancellationToken ct = default)
    {
        var row = await Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct);

        return row == null ? null : await Decorate(row.ToDomainModel(), ct);
    }

    public async Task ResumeAsync(int taskId, string signalJson, CancellationToken ct = default)
    {
        var task = await Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
                   ?? throw new InvalidOperationException($"Acquisition #{taskId} does not exist.");

        if (task.WorkflowRunId is not { } runId)
        {
            throw new InvalidOperationException(
                $"Acquisition #{taskId} has not started yet, so there is nothing waiting.");
        }

        // A task the user has just answered goes ahead of the queue: they are standing there, and
        // the run has already done most of its work.
        await resumer.ResumeAsync(runId, signalJson, ct);

        task.Status = AcquisitionStatus.Running;
        task.WaitReason = null;
        task.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
        await Publish(task, ct);
    }

    public async Task<AcquisitionTask> RetryAsync(int taskId, CancellationToken ct = default)
    {
        var task = await Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
                   ?? throw new InvalidOperationException($"Acquisition #{taskId} does not exist.");

        if (Live.Contains(task.Status))
        {
            throw new InvalidOperationException(
                $"Acquisition #{taskId} is {task.Status} — it has not stopped, so there is nothing to retry.");
        }

        task.Error = null;
        task.CompletedAt = null;
        task.Status = AcquisitionStatus.Pending;
        task.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);

        if (task.WorkflowRunId is { } runId)
        {
            // The run kept its cursor, so this re-runs the step that stopped rather than the chain.
            await resumer.RequeueAsync(runId, ct);
        }
        else
        {
            await StartRun(task, await ResolveName(task.ResourceId), ct);
        }

        await Publish(task, ct);

        return await Decorate(task.ToDomainModel(), ct);
    }

    public async Task CancelAsync(int taskId, CancellationToken ct = default)
    {
        var task = await Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
                   ?? throw new InvalidOperationException($"Acquisition #{taskId} does not exist.");

        if (task.WorkflowRunId is { } runId)
        {
            // Cancelling the run is what actually stops the work; a run already executing finishes
            // its current step and the runner's Pending-only guard stops the next one.
            await Runs.Where(r => r.Id == runId && r.Status != WorkflowRunStatus.Success)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, _ => WorkflowRunStatus.Cancelled)
                    .SetProperty(r => r.CompletedAt, _ => DateTime.Now)
                    .SetProperty(r => r.ErrorMessage, _ => "Cancelled: the acquisition was cancelled"), ct);
        }

        task.Status = AcquisitionStatus.Cancelled;
        task.CompletedAt = DateTime.Now;
        task.UpdatedAt = task.CompletedAt.Value;
        await db.SaveChangesAsync(ct);
        await Publish(task, ct);
    }

    public async Task SyncFromRunAsync(int taskId, long runId, AcquisitionStatus status,
        AcquisitionWaitReason? waitReason, string? targetDirectory,
        IReadOnlyList<PurchaseRecord>? purchases, string? error, CancellationToken ct = default)
    {
        var task = await Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);

        if (task == null) return;

        task.WorkflowRunId = (int) runId;
        task.Status = status;
        task.WaitReason = waitReason;
        task.UpdatedAt = DateTime.Now;

        if (!string.IsNullOrEmpty(targetDirectory)) task.TargetDirectory = targetDirectory;
        if (purchases is {Count: > 0}) task.PurchaseRecordJson = JsonSerializer.Serialize(purchases, Json);
        if (error != null) task.Error = error;

        if (status is AcquisitionStatus.Completed or AcquisitionStatus.Failed or AcquisitionStatus.Cancelled)
        {
            task.CompletedAt = task.UpdatedAt;
        }

        await db.SaveChangesAsync(ct);
        await Publish(task, ct);
    }

    public async Task<List<AcquisitionRecipeSummary>> GetRecipesAsync(CancellationToken ct = default)
    {
        var defs = await workflows.SearchAsync(new Workflow.Abstractions.Models.Input.WorkflowDefinitionSearchInputModel
        {
            TriggerKind = AcquisitionWorkflowKinds.TriggerRequested
        });

        return defs
            .Select(d => new AcquisitionRecipeSummary(d.Id, d.Name, d.IsBuiltin,
                d.Activities.OrderBy(a => a.Order).Select(a => a.Kind).ToList()))
            .ToList();
    }

    // ------- the queue -------

    private int Concurrency => Math.Max(1, options.Value.Concurrency);

    /// <summary>
    /// Started work only. A task waiting on a person holds no slot: it may wait for days, and
    /// letting it block the queue would make the limit mean "how many things can be half-done",
    /// which is not a resource anyone is protecting.
    /// </summary>
    private Task<int> CountLiveRunning(CancellationToken ct) =>
        Tasks.CountAsync(t => t.Status == AcquisitionStatus.Running, ct);

    /// <summary>
    /// Starts queued tasks while there is room. Called by the queue task, so a task that could not
    /// start when it was created is not forgotten, and a Pending task that outlived a restart is
    /// picked up.
    /// </summary>
    public async Task PumpAsync(CancellationToken ct = default)
    {
        var room = Concurrency - await CountLiveRunning(ct);

        if (room <= 0) return;

        var queued = await Tasks
            .Where(t => t.Status == AcquisitionStatus.Pending && t.WorkflowRunId == null)
            .OrderBy(t => t.Id)
            .Take(room)
            .ToListAsync(ct);

        foreach (var task in queued)
        {
            var resource = (await resources.GetAll(r => r.Id == task.ResourceId)).FirstOrDefault();

            if (resource == null)
            {
                task.Status = AcquisitionStatus.Failed;
                task.Error = "The resource this was getting no longer exists.";
                task.CompletedAt = DateTime.Now;
                task.UpdatedAt = task.CompletedAt.Value;
                await db.SaveChangesAsync(ct);

                continue;
            }

            await StartRun(task, await ResolveName(task.ResourceId), ct);
            await Publish(task, ct);
        }
    }

    /// <summary>
    /// Brings task rows back in line with the runs that actually do the work. The run is the truth;
    /// this row is an index over it, and an index that can only be written by the step adapter would
    /// never learn that the last step finished — nothing runs after it.
    /// </summary>
    public async Task ReconcileAsync(CancellationToken ct = default)
    {
        var live = await Tasks.Where(t => Live.Contains(t.Status) && t.WorkflowRunId != null).ToListAsync(ct);

        if (live.Count == 0) return;

        var runIds = live.Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = await Runs.AsNoTracking().Where(r => runIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        var changed = false;

        foreach (var task in live)
        {
            if (!runs.TryGetValue(task.WorkflowRunId!.Value, out var run)) continue;

            var status = FromRunStatus(run.Status);

            if (status == task.Status) continue;

            task.Status = status;
            task.UpdatedAt = DateTime.Now;
            if (run.ErrorMessage != null) task.Error = run.ErrorMessage;
            if (status is AcquisitionStatus.Completed or AcquisitionStatus.Failed
                or AcquisitionStatus.Cancelled)
            {
                task.CompletedAt = run.CompletedAt ?? task.UpdatedAt;
            }

            changed = true;
        }

        if (changed) await db.SaveChangesAsync(ct);
    }

    private static AcquisitionStatus FromRunStatus(WorkflowRunStatus status) => status switch
    {
        WorkflowRunStatus.Pending => AcquisitionStatus.Pending,
        WorkflowRunStatus.Running => AcquisitionStatus.Running,
        WorkflowRunStatus.Waiting => AcquisitionStatus.Waiting,
        WorkflowRunStatus.Success => AcquisitionStatus.Completed,
        WorkflowRunStatus.Failed => AcquisitionStatus.Failed,
        WorkflowRunStatus.Cancelled => AcquisitionStatus.Cancelled,
        // A run interrupted by a restart has not been decided yet; the rehydrator either requeues
        // it or writes it off, and the next reconcile sees whichever happened.
        WorkflowRunStatus.Interrupted => AcquisitionStatus.Failed,
        _ => AcquisitionStatus.Failed
    };

    // ------- helpers -------

    private async Task<WorkflowDefinitionDbModel> ResolveRecipe(int? explicitId, AcquisitionLeadKind leadKind,
        CancellationToken ct)
    {
        if (explicitId is { } id)
        {
            return await Defs.FirstOrDefaultAsync(d => d.Id == id, ct)
                   ?? throw new InvalidOperationException($"Recipe #{id} does not exist.");
        }

        var name = options.Value.RecipeByLeadKind.GetValueOrDefault(leadKind)
                   ?? BuiltinAcquisitionRecipes.DefaultRecipeNameFor(leadKind);

        var byName = await Defs.FirstOrDefaultAsync(
            d => d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested && d.Name == name, ct);

        if (byName != null) return byName;

        // Recipes are only seeded once every step they name is implemented, so a missing one is a
        // half-built release rather than a user error — say which, and say it plainly.
        throw new InvalidOperationException(
            $"There is no recipe named \"{name}\" yet. It needs steps this version does not have; " +
            "pick another recipe, or make one from the steps that are available.");
    }

    private async Task StartRun(AcquisitionTaskDbModel task, string? title, CancellationToken ct)
    {
        var workingDirectory = Path.Combine(appService.AppDataDirectory, "acquisition", task.Id.ToString());

        var payload = new AcquisitionRequestedPayload
        {
            TaskId = task.Id,
            ResourceId = task.ResourceId,
            CollectionId = task.CollectionId,
            LeadKind = task.LeadKind,
            LeadValue = task.LeadValue ?? "",
            Title = title,
            WorkingDirectory = workingDirectory,
            WorkingName = title ?? $"acquisition-{task.Id}",
        };

        var run = await workflows.RunManuallyAsync(task.RecipeDefinitionId,
            JsonSerializer.Serialize(payload, Json), ct);

        task.WorkflowRunId = run.Id;
        task.Status = AcquisitionStatus.Running;
        task.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("[Acquisition] Task {TaskId} started as workflow run {RunId}", task.Id, run.Id);
    }

    /// <summary>
    /// What the user calls the thing being got. It comes from the reserved Name property rather than
    /// the resource's display name, which is derived from a file path a resource without files
    /// does not have.
    /// </summary>
    private async Task<string?> ResolveName(int resourceId) =>
        (await ResolveNames([resourceId])).GetValueOrDefault(resourceId);

    private async Task<Dictionary<int, string?>> ResolveNames(int[] resourceIds) =>
        (await reservedValues.GetAll(v => resourceIds.Contains(v.ResourceId)))
        .Where(v => !string.IsNullOrEmpty(v.Name))
        .GroupBy(v => v.ResourceId)
        .ToDictionary(g => g.Key, g => (string?) g.First().Name);

    private async Task<AcquisitionTask> Decorate(AcquisitionTask task, CancellationToken ct) =>
        (await DecorateMany([task], ct))[0];

    /// <summary>
    /// Fills in what the acquisitions page shows but the task row does not store: the name of the
    /// thing being got, the recipe's name, and how far the run has come.
    /// </summary>
    private async Task<List<AcquisitionTask>> DecorateMany(List<AcquisitionTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return tasks;

        var resourceIds = tasks.Select(t => t.ResourceId).Distinct().ToArray();
        var names = await ResolveNames(resourceIds);

        var defIds = tasks.Select(t => t.RecipeDefinitionId).Distinct().ToList();
        var defNames = await Defs.AsNoTracking().Where(d => defIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => d.Name, ct);

        var runIds = tasks.Where(t => t.WorkflowRunId != null).Select(t => t.WorkflowRunId!.Value).ToList();
        var runs = runIds.Count == 0
            ? []
            : await Runs.AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        foreach (var task in tasks)
        {
            task.ResourceName = names.GetValueOrDefault(task.ResourceId);
            task.RecipeName = defNames.GetValueOrDefault(task.RecipeDefinitionId);

            if (task.WorkflowRunId is { } runId && runs.TryGetValue(runId, out var run))
            {
                task.CurrentStepIndex = run.CurrentStepIndex;
                task.WaitPromptJson = run.WaitPromptJson;
                task.WaitingSince = run.WaitingSince;
            }
        }

        return tasks;
    }

    private async Task Publish(AcquisitionTaskDbModel task, CancellationToken ct)
    {
        try
        {
            await eventBus.PublishAsync(AcquisitionWorkflowKinds.TriggerStatusChanged,
                new AcquisitionStatusChangedPayload
                {
                    AcquisitionTaskId = task.Id,
                    RunId = task.WorkflowRunId,
                    Status = task.Status,
                    ResourceId = task.ResourceId,
                    CollectionId = task.CollectionId,
                    LeadKind = task.LeadKind,
                    WaitReason = task.WaitReason,
                    TargetDirectory = task.TargetDirectory,
                    Error = task.Error,
                }, ct);
        }
        catch (Exception ex)
        {
            // Telling other workflows about it is a courtesy; failing to must not fail the acquisition.
            logger.LogWarning(ex, "[Acquisition] Could not publish the status of task {TaskId}", task.Id);
        }
    }
}
