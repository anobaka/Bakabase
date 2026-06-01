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
///   (activities aren't guaranteed idempotent — we can't safely auto-resume).
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
