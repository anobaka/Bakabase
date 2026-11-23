using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Services;
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
///   <see cref="WorkflowRunStatus.Pending"/> rows using their frozen plan. Legacy rows still
///   require an enabled definition, so an event arriving before shutdown isn't dropped.
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
    private readonly IEnumerable<IWorkflowRunMutationGuard> _mutationGuards;
    private readonly IEnumerable<IWorkflowRunStartupPolicy> _startupPolicies;

    public WorkflowRunRehydrator(
        TDbContext db,
        BTaskManager taskManager,
        WorkflowRunner<TDbContext> runner,
        ILogger<WorkflowRunRehydrator<TDbContext>> logger,
        WorkflowRunSchedulingPolicyResolver scheduling,
        IBakabaseLocalizer localizer,
        IEnumerable<IWorkflowRunMutationGuard>? mutationGuards = null,
        IEnumerable<IWorkflowRunStartupPolicy>? startupPolicies = null)
    {
        _db = db;
        _taskManager = taskManager;
        _runner = runner;
        _logger = logger;
        _scheduling = scheduling;
        _localizer = localizer;
        _mutationGuards = mutationGuards ?? [];
        _startupPolicies = startupPolicies ?? [];
    }

    public async Task MarkInterruptedRunsAsync(CancellationToken ct = default)
    {
        // Old cancellation paths could leave an accepted answer on a stopped cursor.
        // Retain the frozen prompt, input and history, but never requeue this revoked answer.
        await _db.Set<WorkflowRunDbModel>().Where(r => r.Status == WorkflowRunStatus.Cancelled &&
                r.CompletedAt != null && r.PendingSignalJson != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.PendingSignalJson, (string?)null), ct);
        // Attempts are history, not restart checkpoints. A process loss ends the old attempt;
        // re-entering the cursor creates another attempt rather than editing this one.
        await _db.Set<WorkflowNodeExecutionDbModel>().Where(n => n.Status == "running")
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.Status, "interrupted")
                .SetProperty(n => n.CompletedAt, (DateTime?)DateTime.UtcNow), ct);
        // Some sources only process a queue explicitly requested during the current session.
        // Preserve their checkpoints, but require another user action after a restart.
        var definitions = await _db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .Select(d => new {d.Id, d.TriggerKind}).ToListAsync(ct);
        var definitionKinds = definitions.ToDictionary(d => d.Id, d => d.TriggerKind);
        var active = await _db.Set<WorkflowRunDbModel>().AsNoTracking()
            .Where(r => r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running).ToListAsync(ct);
        var manualRestartIds = active.Where(r =>
        {
            try
            {
                var kind = WorkflowRunPlans.Read(r.PlanSnapshotJson)?.TriggerKind ?? definitionKinds.GetValueOrDefault(r.WorkflowDefinitionId);
                return kind != null && !_scheduling.ResumeOnStartup(kind);
            }
            catch (InvalidDataException) { return true; }
        }).Select(r => r.Id).ToList();
        foreach (var run in active.Where(r => !manualRestartIds.Contains(r.Id)))
            foreach (var policy in _startupPolicies)
                if (!await policy.CanResumeOnStartupAsync(run.Id, ct)) {manualRestartIds.Add(run.Id); break;}
        if (manualRestartIds.Count > 0)
            await _db.Set<WorkflowRunDbModel>()
                .Where(r => manualRestartIds.Contains(r.Id) &&
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
        // Snapshots are independent execution requests. Explicitly disabling a template
        // already cancels its pending rows; legacy rows still require an enabled definition.
        var rows = await _db.Set<WorkflowRunDbModel>()
            .Where(r => r.Status == WorkflowRunStatus.Pending)
            .Include(r => r.Sources)
            .ToListAsync(ct);
        var definitions = await _db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .Where(d => d.Enabled).ToDictionaryAsync(d => d.Id, d => d.TriggerKind, ct);

        if (rows.Count == 0) return;

        foreach (var run in rows)
        {
            if (_taskManager.IsShuttingDown) return;
            var gate = WorkflowRunMutationGate.For(run.Id);
            await gate.WaitAsync(ct);
            try
            {
                await _db.Entry(run).ReloadAsync(ct);
                if (run.Status != WorkflowRunStatus.Pending) continue;
                var startupAllowed = true;
                foreach (var policy in _startupPolicies)
                    if (!await policy.CanResumeOnStartupAsync(run.Id, ct)) {startupAllowed = false; break;}
                if (!startupAllowed)
                {
                    run.Status = WorkflowRunStatus.Interrupted;
                    run.CompletedAt = DateTime.Now;
                    await _db.SaveChangesAsync(ct);
                    continue;
                }
                try
                {
                    foreach (var guard in _mutationGuards) await guard.EnsureCanMutateAsync(run.Id, ct);
                }
                catch (InvalidOperationException)
                {
                    // Keep the owner's history intact, without admitting removed work to the queue.
                    _logger.LogInformation("Owner policy prevents restoring workflow run {RunId}", run.Id);
                    continue;
                }
            string? triggerKind;
            try { triggerKind = WorkflowRunPlans.Read(run.PlanSnapshotJson)?.TriggerKind ?? definitions.GetValueOrDefault(run.WorkflowDefinitionId); }
            catch (InvalidDataException) { continue; }
            if (triggerKind == null || !_scheduling.ResumeOnStartup(triggerKind)) continue;
            var runId = run.Id;
            var defId = run.WorkflowDefinitionId;
            await _taskManager.Enqueue(_scheduling.Configure(BTaskBuilder.Create($"workflow.run.{runId}")
                .Named(() => _localizer["BTask_Name_WorkflowRun", defId, runId])
                .Run(args => _runner.ExecuteAsync(runId, args)), defId, triggerKind, runId,
                run.Sources.Select(s => s.ToSourceRef()).ToArray(), run.PlanSnapshotJson, run.CurrentStepIndex));
            }
            finally { gate.Release(); }
        }
        _logger.LogInformation("Re-enqueued {Count} pending workflow runs on startup", rows.Count);
    }
}
