using Bakabase.Abstractions.Components.Localization;
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
///   Source policies may instead require a new request after restart: pending and running
///   rows become Interrupted while keeping their saved cursor and item.
/// - <see cref="ReEnqueuePendingRunsAsync"/>: re-enqueue
///   <see cref="WorkflowRunStatus.Pending"/> rows whose definition still exists and is
///   enabled, so an event that arrived just before shutdown isn't dropped.
///
/// Call sites should invoke both in order: Interrupted first,
/// then Pending re-enqueue.
/// </summary>
public class WorkflowRunRehydrator<TDbContext> where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly BTaskManager _taskManager;
    private readonly WorkflowRunner<TDbContext> _runner;
    private readonly ILogger<WorkflowRunRehydrator<TDbContext>> _logger;
    private readonly WorkflowRunSchedulingPolicyResolver _scheduling;
    private readonly IBakabaseLocalizer _localizer;

    public WorkflowRunRehydrator(
        TDbContext db,
        BTaskManager taskManager,
        WorkflowRunner<TDbContext> runner,
        ILogger<WorkflowRunRehydrator<TDbContext>> logger,
        WorkflowRunSchedulingPolicyResolver scheduling,
        IBakabaseLocalizer localizer)
    {
        _db = db;
        _taskManager = taskManager;
        _runner = runner;
        _logger = logger;
        _scheduling = scheduling;
        _localizer = localizer;
    }

    public async Task MarkInterruptedRunsAsync(CancellationToken ct = default)
    {
        // Some sources only process a queue explicitly requested during the current session.
        // Preserve their checkpoints, but require another user action after a restart.
        var definitions = await _db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .Select(d => new {d.Id, d.TriggerKind}).ToListAsync(ct);
        var manualRestartIds = definitions.Where(d => !_scheduling.ResumeOnStartup(d.TriggerKind))
            .Select(d => d.Id).ToList();
        if (manualRestartIds.Count > 0)
            await _db.Set<WorkflowRunDbModel>()
                .Where(r => manualRestartIds.Contains(r.WorkflowDefinitionId) &&
                    (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, _ => WorkflowRunStatus.Interrupted)
                    .SetProperty(r => r.CompletedAt, _ => DateTime.Now)
                    .SetProperty(r => r.ErrorMessage, _ => "Interrupted by process restart; start again to continue from the saved checkpoint."), ct);

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
        if (_taskManager.IsShuttingDown) return;
        // Only re-enqueue runs whose definition still exists AND is enabled — a disabled
        // definition's pending runs would feel surprising to resume silently.
        var rows = await _db.Set<WorkflowRunDbModel>()
            .Where(r => r.Status == WorkflowRunStatus.Pending)
            .Join(
                _db.Set<WorkflowDefinitionDbModel>().Where(d => d.Enabled),
                r => r.WorkflowDefinitionId,
                d => d.Id,
                (r, d) => new {Run = r, d.TriggerKind})
            .ToListAsync(ct);

        if (rows.Count == 0) return;

        foreach (var entry in rows)
        {
            if (_taskManager.IsShuttingDown) return;
            if (!_scheduling.ResumeOnStartup(entry.TriggerKind)) continue;
            var run = entry.Run;
            var runId = run.Id;
            var defId = run.WorkflowDefinitionId;
            await _taskManager.Enqueue(_scheduling.Configure(BTaskBuilder.Create($"workflow.run.{runId}")
                .Named(() => _localizer["BTask_Name_WorkflowRun", defId, runId])
                .Run(args => _runner.ExecuteAsync(runId, args)), defId, entry.TriggerKind));
        }
        _logger.LogInformation("Re-enqueued {Count} pending workflow runs on startup", rows.Count);
    }
}
