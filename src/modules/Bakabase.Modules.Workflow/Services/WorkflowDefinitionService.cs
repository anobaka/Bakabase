using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Workflow.Services;

public class WorkflowDefinitionService<TDbContext> : IWorkflowDefinitionService
    where TDbContext : DbContext
{
    private readonly TDbContext _db;
    private readonly IWorkflowTriggerRegistry _triggers;
    private readonly IWorkflowActivityRegistry _activities;
    private readonly IWorkflowItemTypeRegistry _itemTypes;
    private readonly BTaskManager _taskManager;
    private readonly WorkflowRunner<TDbContext> _runner;

    public WorkflowDefinitionService(
        TDbContext db,
        IWorkflowTriggerRegistry triggers,
        IWorkflowActivityRegistry activities,
        IWorkflowItemTypeRegistry itemTypes,
        BTaskManager taskManager,
        WorkflowRunner<TDbContext> runner)
    {
        _db = db;
        _triggers = triggers;
        _activities = activities;
        _itemTypes = itemTypes;
        _taskManager = taskManager;
        _runner = runner;
    }

    private DbSet<WorkflowDefinitionDbModel> Defs => _db.Set<WorkflowDefinitionDbModel>();
    private DbSet<WorkflowActivityDbModel>   Acts => _db.Set<WorkflowActivityDbModel>();
    private DbSet<WorkflowRunDbModel>        Runs => _db.Set<WorkflowRunDbModel>();

    public async Task<WorkflowDefinition> CreateAsync(WorkflowDefinitionCreationInputModel input, CancellationToken ct = default)
    {
        ValidateTrigger(input.TriggerKind);
        ValidateActivities(input.Activities, input.TriggerKind, input.TriggerFilterJson);

        var entity = new WorkflowDefinitionDbModel
        {
            Name = input.Name,
            TriggerKind = input.TriggerKind,
            TriggerFilterJson = input.TriggerFilterJson,
            Enabled = input.Enabled,
            CreatedAt = DateTime.Now,
        };
        Defs.Add(entity);
        await _db.SaveChangesAsync(ct);

        await ReplaceActivities(entity.Id, input.Activities, ct);
        return await LoadDomain(entity.Id, ct)
            ?? throw new InvalidOperationException("Definition disappeared after create");
    }

    public async Task<WorkflowDefinition> UpdateAsync(int id, WorkflowDefinitionUpdateInputModel input, CancellationToken ct = default)
    {
        var entity = await Defs.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new InvalidOperationException($"Workflow #{id} not found");

        if (input.Name is not null) entity.Name = input.Name;
        if (input.TriggerFilterJson is not null) entity.TriggerFilterJson = input.TriggerFilterJson;
        if (input.Enabled is { } enabled)
        {
            entity.Enabled = enabled;
            if (!enabled)
            {
                // Disabling means "stop this" to the user, so already-queued runs go with it —
                // the runner's Pending-only guard makes their stale BTasks no-ops
                // (capability map §5·发现 9). A run already Running is left to finish.
                await Runs.Where(r => r.WorkflowDefinitionId == id && r.Status == WorkflowRunStatus.Pending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, _ => WorkflowRunStatus.Cancelled)
                        .SetProperty(r => r.CompletedAt, _ => DateTime.Now)
                        .SetProperty(r => r.ErrorMessage, _ => "Cancelled: the workflow was disabled"), ct);
            }
        }

        entity.UpdatedAt = DateTime.Now;

        if (input.Activities is not null)
        {
            // Validate against the effective filter — the just-applied one if the update
            // changed it, otherwise the stored one (entity was mutated above).
            ValidateActivities(input.Activities, entity.TriggerKind, entity.TriggerFilterJson);
            await ReplaceActivities(entity.Id, input.Activities, ct);
        }

        await _db.SaveChangesAsync(ct);
        return await LoadDomain(entity.Id, ct)
            ?? throw new InvalidOperationException("Definition disappeared after update");
    }

    public async Task DeleteAsync(int id)
    {
        // A run mid-execution keeps producing side effects after its rows vanish, and its final
        // save then targets deleted data — refuse instead of racing it (capability map §5·发现 9).
        // Pending rows don't block: deleting them makes their stale BTasks no-ops.
        if (await Runs.AnyAsync(r => r.WorkflowDefinitionId == id && r.Status == WorkflowRunStatus.Running))
        {
            throw new InvalidOperationException(
                "A run of this workflow is still executing — wait for it to finish before deleting.");
        }

        await Acts.Where(a => a.WorkflowDefinitionId == id).ExecuteDeleteAsync();
        await Runs.Where(r => r.WorkflowDefinitionId == id).ExecuteDeleteAsync();
        await Defs.Where(d => d.Id == id).ExecuteDeleteAsync();
    }

    public async Task<WorkflowDefinition?> GetAsync(int id) => await LoadDomain(id, default);

    public async Task<List<WorkflowDefinition>> SearchAsync(WorkflowDefinitionSearchInputModel input)
    {
        var query = Defs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(input.TriggerKind))
            query = query.Where(d => d.TriggerKind == input.TriggerKind);
        if (input.EnabledOnly == true) query = query.Where(d => d.Enabled);

        var defs = await query.OrderByDescending(d => d.Id).ToListAsync();
        if (defs.Count == 0) return [];

        var ids = defs.Select(d => d.Id).ToList();
        var allActs = await Acts.AsNoTracking().Where(a => ids.Contains(a.WorkflowDefinitionId)).ToListAsync();
        var actsByDef = allActs.GroupBy(a => a.WorkflowDefinitionId).ToDictionary(g => g.Key, g => g.ToList());

        return defs.Select(d => d.ToDomainModel(actsByDef.GetValueOrDefault(d.Id, []))).ToList();
    }

    public async Task<SearchResponse<WorkflowRun>> SearchRunsAsync(WorkflowRunSearchInputModel input)
    {
        var query = Runs.AsNoTracking();
        if (input.WorkflowDefinitionId is { } defId)
            query = query.Where(r => r.WorkflowDefinitionId == defId);

        var pageIndex = Math.Max(1, input.PageIndex);
        var pageSize = Math.Clamp(input.PageSize, 1, 200);

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new SearchResponse<WorkflowRun>(
            rows.Select(r => r.ToDomainModel()),
            total, pageIndex, pageSize);
    }

    public async Task<WorkflowRun> RunManuallyAsync(int definitionId, string? argsJson,
        CancellationToken ct = default)
    {
        var def = await Defs.FirstOrDefaultAsync(d => d.Id == definitionId, ct)
                  ?? throw new InvalidOperationException($"Workflow #{definitionId} not found");

        if (!_triggers.TryGet(def.TriggerKind, out var trigger))
        {
            throw new InvalidOperationException($"Unknown trigger kind: {def.TriggerKind}");
        }

        // Built before the row is written so an unusable payload surfaces as a failed request
        // rather than a persisted run that dies the moment it starts.
        var payload = trigger.BuildManualPayload(def.TriggerFilterJson, argsJson);
        var payloadJson = JsonSerializer.Serialize(payload, WorkflowJson.Options);

        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = def.Id,
            Status = WorkflowRunStatus.Pending,
            StartedAt = DateTime.Now,
            PayloadJson = payloadJson,
            PayloadSummary = Summarize(payloadJson),
        };
        Runs.Add(run);
        await _db.SaveChangesAsync(ct);

        var runId = run.Id;
        await _taskManager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}")
            .Named($"Workflow #{def.Id} run #{runId}")
            .ConflictsWith($"workflow.definition.{def.Id}")
            .Run(args => _runner.ExecuteAsync(runId, args)));

        return run.ToDomainModel();
    }

    private static string Summarize(string payloadJson) =>
        payloadJson.Length > 200 ? payloadJson[..200] + "…" : payloadJson;

    // ------- helpers -------

    private void ValidateTrigger(string triggerKind)
    {
        if (!_triggers.TryGet(triggerKind, out _))
            throw new InvalidOperationException($"Unknown trigger kind: {triggerKind}");
    }

    private void ValidateActivities(
        IReadOnlyList<WorkflowActivityInputModel> activities, string triggerKind, string? triggerFilterJson)
    {
        if (!_triggers.TryGet(triggerKind, out var trigger))
            throw new InvalidOperationException($"Unknown trigger kind: {triggerKind}");

        // Walk the chain tracking the item type. Each activity must accept the type produced
        // by everything before it; transforms then change the type for what follows.
        var currentType = trigger.ResolveOutputItemType(triggerFilterJson);

        for (var i = 0; i < activities.Count; i++)
        {
            var a = activities[i];
            if (!_activities.TryGet(a.Kind, out var impl))
                throw new InvalidOperationException($"Unknown activity kind: {a.Kind}");

            var contract = impl.AcceptedItemInterface;
            if (contract is not null && impl.OutputBehavior != WorkflowItemTypeBehavior.Passthrough)
                throw new InvalidOperationException(
                    $"Activity {a.Kind} declares a capability contract ({contract.Name}) but is not " +
                    "Passthrough — a contract-accepting activity works on whatever type arrives and " +
                    "cannot change it. The activity's declaration is broken.");

            // Accepted when the tag is listed, OR the tag's CLR shape implements the activity's
            // capability contract (capability map E3). Both surfaces empty = accepts any type.
            var accepted = impl.AcceptedInputItemTypes;
            var acceptsByContract = contract is not null &&
                                    _itemTypes.Get(currentType)?.ClrType is { } clr &&
                                    contract.IsAssignableFrom(clr);
            if ((accepted.Count > 0 || contract is not null) &&
                !accepted.Contains(currentType) && !acceptsByContract)
                throw new InvalidOperationException(
                    $"Activity {a.Kind} (index {i}) accepts [{string.Join(", ", accepted)}]" +
                    (contract is null ? "" : $" or any type implementing {contract.Name}") +
                    $" but the item type at that position is \"{currentType}\". " +
                    "Insert a transform that produces a compatible type before it.");

            if (impl.IsDestructive && i > 0 &&
                _activities.TryGet(activities[i - 1].Kind, out var prev) &&
                prev.OutputBehavior == WorkflowItemTypeBehavior.AdaptToNext)
                throw new InvalidOperationException(
                    $"Activity {a.Kind} (index {i}) is destructive and cannot directly consume " +
                    "model-generated items — put a validating step between them.");

            currentType = impl.OutputBehavior switch
            {
                WorkflowItemTypeBehavior.Passthrough => currentType,
                WorkflowItemTypeBehavior.Fixed => impl.FixedOutputItemType
                    ?? throw new InvalidOperationException(
                        $"Activity {impl.Kind} declares Fixed output but no FixedOutputItemType"),
                WorkflowItemTypeBehavior.AdaptToNext => impl.ResolveAdaptedOutputType(
                        a.ConfigJson, PeekNextSingleAcceptedType(i, activities))
                    ?? throw new InvalidOperationException(
                        $"Activity {impl.Kind} (index {i}) needs a target item type — " +
                        "configure one explicitly, or follow it with an activity that accepts a single type"),
                _ => throw new InvalidOperationException(
                    $"Unhandled OutputBehavior {impl.OutputBehavior} on {impl.Kind}"),
            };
        }
    }

    /// <summary>Peek the next activity's single accepted type, if exactly one — else null.</summary>
    private string? PeekNextSingleAcceptedType(int index, IReadOnlyList<WorkflowActivityInputModel> activities)
    {
        if (index + 1 >= activities.Count) return null;
        if (!_activities.TryGet(activities[index + 1].Kind, out var next)) return null;
        return next.AcceptedInputItemTypes.Count == 1 ? next.AcceptedInputItemTypes[0] : null;
    }

    private async Task ReplaceActivities(int defId, IReadOnlyList<WorkflowActivityInputModel> activities, CancellationToken ct)
    {
        await Acts.Where(a => a.WorkflowDefinitionId == defId).ExecuteDeleteAsync(ct);
        for (var i = 0; i < activities.Count; i++)
        {
            var a = activities[i];
            Acts.Add(new WorkflowActivityDbModel
            {
                WorkflowDefinitionId = defId,
                Order = i,
                Kind = a.Kind,
                ConfigJson = string.IsNullOrEmpty(a.ConfigJson) ? "{}" : a.ConfigJson,
                OnItemError = a.OnItemError,
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<WorkflowDefinition?> LoadDomain(int id, CancellationToken ct)
    {
        var entity = await Defs.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity is null) return null;
        var acts = await Acts.AsNoTracking()
            .Where(a => a.WorkflowDefinitionId == id)
            .ToListAsync(ct);
        return entity.ToDomainModel(acts);
    }
}
