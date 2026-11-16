using Bakabase.Abstractions.Components.Localization;
using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Components;

/// <inheritdoc />
public class WorkflowRunResumer<TDbContext>(
    TDbContext db,
    IWorkflowValidationService validation,
    IWorkflowTriggerRegistry triggers,
    BTaskManager taskManager,
    WorkflowRunSchedulingPolicyResolver scheduling,
    ILogger<WorkflowRunResumer<TDbContext>> logger,
    IBakabaseLocalizer localizer,
    IEnumerable<IWorkflowRunMutationGuard>? mutationGuards = null) : IWorkflowRunResumer
    where TDbContext : DbContext
{
    public Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default) =>
        MutateAsync(runId, () => ResumeCoreAsync(runId, signalJson, ct), ct);

    private async Task ResumeCoreAsync(int runId, string signalJson, CancellationToken ct)
    {
        var run = await db.Set<WorkflowRunDbModel>().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Workflow run #{runId} does not exist.");
        await db.Entry(run).ReloadAsync(ct);

        if (run.Status != WorkflowRunStatus.Waiting)
        {
            // Answering a run that is not asking would restart a finished chain from its cursor.
            throw new InvalidOperationException(
                $"Workflow run #{runId} is {run.Status}, not waiting for anything.");
        }

        var definition = await ValidateRemaining(run, ct);

        // The signal travels on the row: the runner is re-entered through a background task that
        // carries nothing but the run id.
        run.PendingSignalJson = signalJson;
        run.Status = WorkflowRunStatus.Pending;
        await db.SaveChangesAsync(ct);

        await Enqueue(run, definition, ct);

        logger.LogInformation("Workflow run {RunId} resumed from step {Step}", runId, run.CurrentStepIndex);
    }

    public Task RequeueAsync(int runId, CancellationToken ct = default) =>
        MutateAsync(runId, () => RequeueCoreAsync(runId, ct), ct);

    public Task ContinueConditionallyAsync(int runId, bool waiting, string signalJson,
        Func<CancellationToken, Task> validate, CancellationToken ct = default) => MutateAsync(runId, async () =>
    {
        await validate(ct);
        if (waiting) await ResumeCoreAsync(runId, signalJson, ct);
        else await RequeueCoreAsync(runId, ct, signalJson);
    }, ct);

    private async Task RequeueCoreAsync(int runId, CancellationToken ct, string? continuationSignal = null)
    {
        var run = await db.Set<WorkflowRunDbModel>().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Workflow run #{runId} does not exist.");
        await db.Entry(run).ReloadAsync(ct);

        if (run.Status is WorkflowRunStatus.Pending or WorkflowRunStatus.Running
            or WorkflowRunStatus.Waiting or WorkflowRunStatus.Success)
        {
            throw new InvalidOperationException(
                $"Workflow run #{runId} is {run.Status} — only a run that stopped can be retried.");
        }

        var definition = await ValidateRemaining(run, ct);

        run.Status = WorkflowRunStatus.Pending;
        run.CompletedAt = null;
        run.ErrorMessage = null;
        if (continuationSignal != null) run.PendingSignalJson = continuationSignal;
        await db.SaveChangesAsync(ct);

        await Enqueue(run, definition, ct);

        logger.LogInformation("Workflow run {RunId} requeued from step {Step}", runId, run.CurrentStepIndex);
    }

    public Task UpdateWaitAsync(int runId, string reason, string? promptJson,
        CancellationToken ct = default) =>
        MutateAsync(runId, () => UpdateWaitCoreAsync(runId, reason, promptJson, ct), ct);

    public Task ReprocessWaitingAsync(int runId, CancellationToken ct = default) =>
        MutateAsync(runId, () => ReprocessWaitingCoreAsync(runId, ct), ct);

    private async Task ReprocessWaitingCoreAsync(int runId, CancellationToken ct)
    {
        var run = await db.Set<WorkflowRunDbModel>().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Workflow run #{runId} does not exist.");
        await db.Entry(run).ReloadAsync(ct);
        if (run.Status != WorkflowRunStatus.Waiting)
            throw new InvalidOperationException($"Workflow run #{runId} is {run.Status}, not waiting for anything.");
        var definition = await ValidateRemaining(run, ct);
        run.PendingSignalJson = null;
        run.Status = WorkflowRunStatus.Pending;
        run.ErrorMessage = null;
        await db.SaveChangesAsync(ct);
        await Enqueue(run, definition, ct);
        logger.LogInformation("Workflow run {RunId} reprocessed waiting activity at step {Step}", runId, run.CurrentStepIndex);
    }

    private async Task UpdateWaitCoreAsync(int runId, string reason, string? promptJson, CancellationToken ct)
    {
        var run = await db.Set<WorkflowRunDbModel>().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Workflow run #{runId} does not exist.");
        await db.Entry(run).ReloadAsync(ct);

        if (run.Status != WorkflowRunStatus.Waiting)
        {
            throw new InvalidOperationException(
                $"Workflow run #{runId} is {run.Status}, not waiting for anything.");
        }

        run.WaitReason = reason;
        run.WaitPromptJson = promptJson;
        // WaitingSince is deliberately left alone: the run has been waiting since it started
        // waiting, and restarting that clock would hide how long someone has been stuck.
        await db.SaveChangesAsync(ct);
    }

    private async Task MutateAsync(int runId, Func<Task> mutate, CancellationToken ct)
    {
        var gate = WorkflowRunMutationGate.For(runId);
        await gate.WaitAsync(ct);
        try
        {
            foreach (var guard in mutationGuards ?? []) await guard.EnsureCanMutateAsync(runId, ct);
            await mutate();
        }
        finally { gate.Release(); }
    }

    private async Task<WorkflowDefinition> ValidateRemaining(WorkflowRunDbModel run, CancellationToken ct)
    {
        var definition = await WorkflowRunPlans.LoadAsync(db, run, ct);
        object? payload = null;
        if (triggers.TryGet(definition.TriggerKind, out var trigger) && !string.IsNullOrEmpty(run.PayloadJson))
        {
            try { payload = JsonSerializer.Deserialize(run.PayloadJson, trigger.PayloadType, WorkflowJson.Options); }
            catch (JsonException ex) { throw new InvalidOperationException($"Run payload is invalid: {ex.Message}", ex); }
        }
        var check = await validation.ValidateAsync(definition, true, payload, ct, run.CurrentStepIndex ?? 0);
        if (!check.IsValid) throw new WorkflowValidationException(check);
        return definition;
    }

    /// <summary>Keep the original task identity and source scheduling policy when returning to the queue.</summary>
    private async Task Enqueue(WorkflowRunDbModel run, WorkflowDefinition definition, CancellationToken ct)
    {
        if (taskManager.IsShuttingDown) return;
        var runId = run.Id;
        var defId = definition.Id;
        var sources = await db.Set<WorkflowRunSourceDbModel>().AsNoTracking()
            .Where(s => s.WorkflowRunId == runId).ToListAsync(ct);
        await taskManager.Enqueue(scheduling.Configure(BTaskBuilder.Create($"workflow.run.{runId}")
            .Named(() => localizer["BTask_Name_WorkflowRun", defId, runId])
            .ReplaceIfExists()
            .Run(async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<WorkflowRunner<TDbContext>>().ExecuteAsync(runId, args);
            }), defId, definition.TriggerKind, runId, sources.Select(s => s.ToSourceRef()).ToArray(),
                run.PlanSnapshotJson, run.CurrentStepIndex));
    }
}
