using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Called from the host after DB migration runs. Two responsibilities:
///
/// - <see cref="MarkInterruptedRunsAsync"/>: flip rows stuck in
///   <see cref="WorkflowRunStatus.Running"/> to <see cref="WorkflowRunStatus.Interrupted"/>
///   (activities aren't guaranteed idempotent — we can't safely auto-resume). A run that kept a
///   step cursor is the exception: it goes back to <see cref="WorkflowRunStatus.Pending"/> and
///   restarts at that step. Runs in <see cref="WorkflowRunStatus.Waiting"/> are left alone —
///   waiting for a person to do something is not a state a restart should disturb.
/// - <see cref="ReEnqueuePendingRunsAsync"/>: re-enqueue
///   <see cref="WorkflowRunStatus.Pending"/> rows whose definition still exists and is
///   enabled, so an event that arrived just before shutdown isn't dropped.
///
/// Call sites should invoke both in order: Interrupted first (it's a single SQL UPDATE),
/// then Pending re-enqueue.
/// </summary>
public class WorkflowRunRehydrator<TDbContext> where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly BTaskManager _taskManager;
    private readonly WorkflowRunner<TDbContext> _runner;
    private readonly ILogger<WorkflowRunRehydrator<TDbContext>> _logger;

    public WorkflowRunRehydrator(
        TDbContext db,
        BTaskManager taskManager,
        WorkflowRunner<TDbContext> runner,
        ILogger<WorkflowRunRehydrator<TDbContext>> logger)
    {
        _db = db;
        _taskManager = taskManager;
        _runner = runner;
        _logger = logger;
    }

    public async Task MarkInterruptedRunsAsync(CancellationToken ct = default)
    {
        // A run that was persisting a cursor knows exactly where it got to, and only the step at
        // that cursor is ever re-run — so it goes back in the queue rather than being written off.
        var resumable = await _db.Set<WorkflowRunDbModel>()
            .Where(r => r.Status == WorkflowRunStatus.Running && r.CurrentStepIndex != null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, _ => WorkflowRunStatus.Pending), ct);

        if (resumable > 0)
        {
            _logger.LogInformation(
                "Returned {Count} workflow runs to the queue on startup; each restarts at its cursor",
                resumable);
        }

        // Everything else was mid-chain with no record of where: activities are not guaranteed
        // idempotent, so re-running from the top could do the same work twice.
        var count = await _db.Set<WorkflowRunDbModel>()
            .Where(r => r.Status == WorkflowRunStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, _ => WorkflowRunStatus.Interrupted)
                .SetProperty(r => r.CompletedAt, _ => DateTime.Now)
                .SetProperty(r => r.ErrorMessage, _ => "Interrupted by process restart"), ct);

        if (count > 0)
            _logger.LogInformation("Marked {Count} workflow runs as Interrupted on startup", count);
    }

    public async Task ReEnqueuePendingRunsAsync(CancellationToken ct = default)
    {
        // Only re-enqueue runs whose definition still exists AND is enabled — a disabled
        // definition's pending runs would feel surprising to resume silently.
        var rows = await _db.Set<WorkflowRunDbModel>()
            .Where(r => r.Status == WorkflowRunStatus.Pending)
            .Join(
                _db.Set<WorkflowDefinitionDbModel>().Where(d => d.Enabled),
                r => r.WorkflowDefinitionId,
                d => d.Id,
                (r, d) => r)
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        foreach (var run in rows)
        {
            var runId = run.Id;
            var defId = run.WorkflowDefinitionId;
            await _taskManager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}")
                .Named($"Workflow #{defId} run #{runId}")
                .ConflictsWith($"workflow.definition.{defId}")
                .Run(args => _runner.ExecuteAsync(runId, args)));
        }
        _logger.LogInformation("Re-enqueued {Count} pending workflow runs on startup", rows.Count);
    }
}
