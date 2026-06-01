using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Default <see cref="IWorkflowEventBus"/>. On Publish:
/// 1) load enabled definitions matching the trigger kind,
/// 2) evaluate each one's trigger filter against the payload,
/// 3) create a WorkflowRun row per matching definition,
/// 4) enqueue one BTask per run that hands it off to the runner.
/// </summary>
public class WorkflowEventBus<TDbContext> : IWorkflowEventBus where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly IWorkflowTriggerRegistry _triggers;
    private readonly BTaskManager _taskManager;
    private readonly WorkflowRunner<TDbContext> _runner;
    private readonly ILogger<WorkflowEventBus<TDbContext>> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public WorkflowEventBus(
        TDbContext db,
        IWorkflowTriggerRegistry triggers,
        BTaskManager taskManager,
        WorkflowRunner<TDbContext> runner,
        ILogger<WorkflowEventBus<TDbContext>> logger)
    {
        _db = db;
        _triggers = triggers;
        _taskManager = taskManager;
        _runner = runner;
        _logger = logger;
    }

    public async Task PublishAsync<T>(string triggerKind, T payload, CancellationToken ct = default)
    {
        if (payload is null)
        {
            _logger.LogDebug("PublishAsync({TriggerKind}) skipped — payload is null", triggerKind);
            return;
        }

        if (!_triggers.TryGet(triggerKind, out var trigger))
        {
            _logger.LogWarning("PublishAsync({TriggerKind}) — no trigger registered", triggerKind);
            return;
        }

        var defs = await _db.Set<WorkflowDefinitionDbModel>()
            .Where(d => d.Enabled && d.TriggerKind == triggerKind)
            .ToListAsync(ct);
        if (defs.Count == 0) return;

        var payloadJson = JsonSerializer.Serialize<object>(payload, JsonOptions);
        var payloadSummary = SummarizePayload(payload);
        var matched = new List<WorkflowRunDbModel>();

        foreach (var def in defs)
        {
            bool matches;
            try { matches = trigger.Matches(payload, def.TriggerFilterJson); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Trigger {Kind} filter evaluation failed for workflow #{DefId}, skipping",
                    triggerKind, def.Id);
                continue;
            }
            if (!matches) continue;

            var run = new WorkflowRunDbModel
            {
                WorkflowDefinitionId = def.Id,
                Status = WorkflowRunStatus.Pending,
                StartedAt = DateTime.Now,
                PayloadJson = payloadJson,
                PayloadSummary = payloadSummary,
            };
            _db.Set<WorkflowRunDbModel>().Add(run);
            matched.Add(run);
        }

        if (matched.Count == 0) return;
        await _db.SaveChangesAsync(ct);

        // Enqueue BTasks AFTER SaveChanges so we have stable run.Ids.
        foreach (var run in matched)
        {
            var runId = run.Id;
            var defId = run.WorkflowDefinitionId;
            await _taskManager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}")
                .Named($"Workflow #{defId} run #{runId}")
                .ConflictsWith($"workflow.definition.{defId}")
                .Run(args => _runner.ExecuteAsync(runId, args)));
        }
    }

    private static string? SummarizePayload(object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize<object>(payload, JsonOptions);
            return json.Length > 200 ? json[..200] + "…" : json;
        }
        catch { return null; }
    }
}
