using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Components;

/// <inheritdoc />
public class WorkflowRunResumer<TDbContext>(
    TDbContext db,
    BTaskManager taskManager,
    WorkflowRunner<TDbContext> runner,
    ILogger<WorkflowRunResumer<TDbContext>> logger) : IWorkflowRunResumer
    where TDbContext : DbContext
{
    public async Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default)
    {
        var run = await db.Set<WorkflowRunDbModel>().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new InvalidOperationException($"Workflow run #{runId} does not exist.");

        if (run.Status != WorkflowRunStatus.Waiting)
        {
            // Answering a run that is not asking would restart a finished chain from its cursor.
            throw new InvalidOperationException(
                $"Workflow run #{runId} is {run.Status}, not waiting for anything.");
        }

        // The signal travels on the row: the runner is re-entered through a background task that
        // carries nothing but the run id.
        run.PendingSignalJson = signalJson;
        run.Status = WorkflowRunStatus.Pending;
        await db.SaveChangesAsync(ct);

        var defId = run.WorkflowDefinitionId;

        // Same task id and conflict key as the original enqueue, so a resumed run is still one run
        // of that definition and cannot race another.
        await taskManager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}")
            .Named($"Workflow #{defId} run #{runId}")
            .ConflictsWith($"workflow.definition.{defId}")
            .ReplaceIfExists()
            .Run(args => runner.ExecuteAsync(runId, args)));

        logger.LogInformation("Workflow run {RunId} resumed from step {Step}", runId, run.CurrentStepIndex);
    }
}
