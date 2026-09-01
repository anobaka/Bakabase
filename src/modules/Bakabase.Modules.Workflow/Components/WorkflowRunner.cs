using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>
/// Executes a single <see cref="WorkflowRunDbModel"/> end-to-end: hydrate payload,
/// build context, iterate activities, persist outcome.
/// </summary>
public class WorkflowRunner<TDbContext> where TDbContext : DbContext
{
    /// <summary>
    /// Scope factory rather than an injected IServiceProvider.
    /// </summary>
    /// <remarks>
    /// This type is scoped, so an injected IServiceProvider is the scope that resolved it — not the
    /// root, despite what a name like "rootServices" would suggest. Every caller enqueues a BTask
    /// whose body calls <see cref="ExecuteAsync"/> later, by which time that scope is long disposed,
    /// so creating a scope from it threw ObjectDisposedException and the run failed before it began.
    /// IServiceScopeFactory always creates scopes from the root container and stays usable after the
    /// resolving scope is gone.
    /// </remarks>
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly IWorkflowTriggerRegistry _triggers;
    private readonly IWorkflowActivityRegistry _activities;
    private readonly IWorkflowItemTypeRegistry _itemTypes;
    private readonly ILogger<WorkflowRunner<TDbContext>> _logger;

    public WorkflowRunner(
        IServiceScopeFactory scopeFactory,
        IWorkflowTriggerRegistry triggers,
        IWorkflowActivityRegistry activities,
        IWorkflowItemTypeRegistry itemTypes,
        ILogger<WorkflowRunner<TDbContext>> logger)
    {
        _scopeFactory = scopeFactory;
        _triggers = triggers;
        _activities = activities;
        _itemTypes = itemTypes;
        _logger = logger;
    }

    public async Task ExecuteAsync(int runId, BTaskArgs btaskArgs)
    {
        var ct = btaskArgs.CancellationToken;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var run = await db.Set<WorkflowRunDbModel>().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            _logger.LogWarning("WorkflowRun {RunId} not found — skipping", runId);
            return;
        }

        if (run.Status != WorkflowRunStatus.Pending)
        {
            // The row was cancelled (e.g. its definition got disabled) or already executed —
            // the BTask that carried it is stale, and executing anyway is exactly the
            // "disable didn't stop it" surprise this guard removes (capability map §5·发现 9).
            _logger.LogInformation("WorkflowRun {RunId} is {Status}, not Pending — skipping", runId, run.Status);
            return;
        }

        var definition = await db.Set<WorkflowDefinitionDbModel>()
            .FirstOrDefaultAsync(d => d.Id == run.WorkflowDefinitionId, ct);
        if (definition is null)
        {
            await FailRun(db, run, "Workflow definition deleted before run could start", ct);
            return;
        }

        if (!_triggers.TryGet(definition.TriggerKind, out var trigger))
        {
            await FailRun(db, run, $"Unknown trigger kind: {definition.TriggerKind}", ct);
            return;
        }

        object payload;
        try
        {
            payload = string.IsNullOrEmpty(run.PayloadJson)
                ? throw new InvalidOperationException("Run has no payload")
                : JsonSerializer.Deserialize(run.PayloadJson, trigger.PayloadType, WorkflowJson.Options)
                  ?? throw new InvalidOperationException("Payload deserialized to null");
        }
        catch (Exception ex)
        {
            await FailRun(db, run, $"Payload deserialization failed: {ex.Message}", ct);
            return;
        }

        var activityRows = await db.Set<WorkflowActivityDbModel>()
            .Where(a => a.WorkflowDefinitionId == definition.Id)
            .OrderBy(a => a.Order)
            .ToListAsync(ct);

        var items = trigger.ExtractItems(payload).ToList();
        run.InputCount = items.Count;
        run.Status = WorkflowRunStatus.Running;
        await db.SaveChangesAsync(ct);

        var totalSteps = Math.Max(1, activityRows.Count);
        var failedTotal = 0;
        var stepStats = new List<WorkflowRunStepStat>(activityRows.Count);

        try
        {
            for (var stepIndex = 0; stepIndex < activityRows.Count; stepIndex++)
            {
                await btaskArgs.YieldAsync();
                var activityRow = activityRows[stepIndex];

                if (!_activities.TryGet(activityRow.Kind, out var impl))
                    throw new InvalidOperationException($"Unknown activity kind: {activityRow.Kind}");

                // For AdaptToNext activities (the AI transform), resolve the target item type
                // ONCE per step from the activity's config + the next step's accepted type,
                // and pass it to the activity through ctx so it can shape its output.
                string? targetItemType = null;
                if (impl.OutputBehavior == WorkflowItemTypeBehavior.AdaptToNext)
                {
                    var nextHint = PeekNextSingleAcceptedType(stepIndex, activityRows);
                    targetItemType = impl.ResolveAdaptedOutputType(activityRow.ConfigJson, nextHint);
                    if (targetItemType is null)
                        throw new InvalidOperationException(
                            $"Activity {activityRow.Kind} (index {stepIndex}) couldn't resolve a target " +
                            "item type — should have been caught at save time");
                }

                // Type tags are validated at save time, but runtime items are plain objects —
                // this closes the gap (capability map §5·发现 7): when a step declares accepted
                // tags, every item must actually BE one of those CLR shapes, and a mismatch
                // fails the run instead of being silently passed along by a defensive activity.
                var acceptedClrTypes = impl.AcceptedInputItemTypes
                    .Select(tag => _itemTypes.Get(tag)?.ClrType)
                    .Where(t => t != null)
                    .Cast<Type>()
                    .ToList();

                var stepInput = items.Count;
                var stepFailed = 0;
                var nextItems = new List<object>(items.Count);
                foreach (var item in items)
                {
                    await btaskArgs.YieldAsync();

                    if (acceptedClrTypes.Count > 0 && !acceptedClrTypes.Any(t => t.IsInstanceOfType(item)))
                    {
                        throw new InvalidOperationException(
                            $"Step {stepIndex + 1} ({activityRow.Kind}) received a {item.GetType().Name}, " +
                            $"which is not any of its accepted item shapes — the chain's typing is broken.");
                    }

                    var itemCtx = new WorkflowExecutionContext
                    {
                        RunId = run.Id,
                        WorkflowDefinitionId = definition.Id,
                        TriggerKind = definition.TriggerKind,
                        Payload = payload,
                        ActivityConfigJson = activityRow.ConfigJson,
                        TargetItemType = targetItemType,
                        Services = scope.ServiceProvider,
                        Logger = _logger,
                    };

                    WorkflowItemOutcome outcome;
                    try
                    {
                        outcome = await impl.ProcessItemAsync(itemCtx, item, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // A broken config hits every item identically; skipping would silently
                        // run the step on defaults. Always fail the run for it.
                        if (activityRow.OnItemError == WorkflowActivityErrorBehavior.Skip &&
                            ex is not WorkflowActivityConfigException)
                        {
                            _logger.LogWarning(ex,
                                "Workflow run {RunId} activity {Kind} item dropped (skip-on-error)",
                                run.Id, activityRow.Kind);
                            failedTotal++;
                            stepFailed++;
                            continue;
                        }
                        // Fail policy: record the step's partial stats so the funnel still
                        // shows where it died, then bubble to the outer catch.
                        stepStats.Add(new WorkflowRunStepStat
                        {
                            StepIndex = stepIndex,
                            Kind = activityRow.Kind,
                            InputCount = stepInput,
                            OutputCount = nextItems.Count,
                            FailedCount = stepFailed + 1,
                        });
                        throw;
                    }

                    if (outcome.Keep) nextItems.Add(outcome.Replacement ?? item);
                }

                items = nextItems;
                stepStats.Add(new WorkflowRunStepStat
                {
                    StepIndex = stepIndex,
                    Kind = activityRow.Kind,
                    InputCount = stepInput,
                    OutputCount = items.Count,
                    FailedCount = stepFailed,
                });

                await btaskArgs.UpdateTask(t =>
                {
                    t.Percentage = (stepIndex + 1) * 100 / totalSteps;
                    t.Process = $"{stepIndex + 1}/{activityRows.Count} · {items.Count} items";
                });
            }

            run.OutputCount = items.Count;
            run.FailedItemCount = failedTotal;
            run.StepStatsJson = JsonSerializer.Serialize(stepStats, WorkflowJson.Options);
            run.Status = WorkflowRunStatus.Success;
            run.CompletedAt = DateTime.Now;
            definition.LastRunAt = run.CompletedAt;
            definition.LastError = null;
        }
        catch (OperationCanceledException)
        {
            run.Status = WorkflowRunStatus.Cancelled;
            run.CompletedAt = DateTime.Now;
            run.FailedItemCount = failedTotal;
            run.StepStatsJson = JsonSerializer.Serialize(stepStats, WorkflowJson.Options);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow run {RunId} failed", run.Id);
            run.Status = WorkflowRunStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.FailedItemCount = failedTotal;
            run.StepStatsJson = JsonSerializer.Serialize(stepStats, WorkflowJson.Options);
            run.CompletedAt = DateTime.Now;
            definition.LastError = ex.Message;
            definition.LastRunAt = run.CompletedAt;
        }
        finally
        {
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task FailRun(TDbContext db, WorkflowRunDbModel run, string message, CancellationToken ct)
    {
        run.Status = WorkflowRunStatus.Failed;
        run.ErrorMessage = message;
        run.CompletedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    private string? PeekNextSingleAcceptedType(int index, IReadOnlyList<WorkflowActivityDbModel> activities)
    {
        if (index + 1 >= activities.Count) return null;
        if (!_activities.TryGet(activities[index + 1].Kind, out var next)) return null;
        return next.AcceptedInputItemTypes.Count == 1 ? next.AcceptedInputItemTypes[0] : null;
    }
}
