using System.Text.Json;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
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
    private readonly IWorkflowValidationService _validation;
    private readonly BTaskManager _taskManager;
    private readonly WorkflowRunner<TDbContext> _runner;

    public WorkflowDefinitionService(
        TDbContext db,
        IWorkflowTriggerRegistry triggers,
        BTaskManager taskManager,
        WorkflowRunner<TDbContext> runner,
        IWorkflowValidationService validation)
    {
        _db = db;
        _triggers = triggers;
        _validation = validation;
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
            Description = input.Description,
            DescriptionKey = input.DescriptionKey,
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

        // A built-in definition is a seed, not a document: a later release adds a step to it, and
        // that must not silently overwrite someone's edits or silently fail to reach them. Turning
        // one off is still theirs to decide — that is a choice about their setup, not about the seed.
        if (entity.IsBuiltin &&
            (input.Name is not null || input.Description is not null || input.TriggerFilterJson is not null || input.Activities is not null))
        {
            throw new InvalidOperationException(
                $"\"{entity.Name}\" ships with Bakabase and cannot be edited. Copy it and change the copy.");
        }

        // Validate the effective chain before touching tracked fields or cancelling queued runs.
        if (input.Activities is not null || input.TriggerFilterJson is not null)
        {
            var nodes = input.Activities ?? (await Acts.AsNoTracking()
                .Where(a => a.WorkflowDefinitionId == id).OrderBy(a => a.Order).ToListAsync(ct))
                .Select(a => new WorkflowActivityInputModel { Kind = a.Kind, ConfigJson = a.ConfigJson }).ToList();
            ValidateActivities(nodes, entity.TriggerKind, input.TriggerFilterJson ?? entity.TriggerFilterJson);
        }

        if (input.Name is not null) entity.Name = input.Name;
        if (input.Description is not null)
        {
            entity.Description = input.Description;
            entity.DescriptionKey = null;
        }
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
            await ReplaceActivities(entity.Id, input.Activities, ct);
        }

        await _db.SaveChangesAsync(ct);
        return await LoadDomain(entity.Id, ct)
            ?? throw new InvalidOperationException("Definition disappeared after update");
    }

    public async Task DeleteAsync(int id)
    {
        if (await Defs.AnyAsync(d => d.Id == id && d.IsBuiltin))
        {
            throw new InvalidOperationException(
                "This workflow ships with Bakabase and cannot be deleted. Switch it off instead.");
        }

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

        if (!trigger.SupportsManualRun)
            throw new InvalidOperationException(
                $"This workflow must be started from its source module. {trigger.Description}");

        // Built before the row is written so an unusable payload surfaces as a failed request
        // rather than a persisted run that dies the moment it starts.
        var definition = await LoadDomain(definitionId, ct)
            ?? throw new InvalidOperationException($"Workflow #{definitionId} not found");
        // Reject structural and unconditional configuration errors before the trigger does any
        // payload preparation. Only explicitly input-dependent requirements may be reconsidered
        // once the actual payload is known; an ordinary invalid configuration remains fail-fast.
        var preparationCheck = await _validation.ValidateAsync(definition, isExecution: true, ct: ct);
        var preparationErrors = preparationCheck.Diagnostics
            .Where(d => d.Severity == "error" && !d.DependsOnPayload).ToList();
        if (preparationErrors.Count > 0)
            throw new WorkflowValidationException(new WorkflowValidationResult {Diagnostics = preparationErrors});
        var payload = trigger.BuildManualPayload(def.TriggerFilterJson, argsJson);
        return await StartPreparedRunAsync(definition, payload, ct);
    }

    public async Task<WorkflowRun> RunManagedAsync(int definitionId, object payload,
        CancellationToken ct = default)
    {
        var definition = await LoadDomain(definitionId, ct)
            ?? throw new InvalidOperationException($"Workflow #{definitionId} not found");
        if (!_triggers.TryGet(definition.TriggerKind, out var trigger))
            throw new InvalidOperationException($"Unknown trigger kind: {definition.TriggerKind}");
        if (trigger.SupportsManualRun)
            throw new InvalidOperationException("This workflow accepts manual input; use its manual-run entry point.");
        if (!trigger.PayloadType.IsInstanceOfType(payload))
            throw new InvalidOperationException($"The input does not match {trigger.PayloadType.Name}.");
        return await StartPreparedRunAsync(definition, payload, ct);
    }

    private async Task<WorkflowRun> StartPreparedRunAsync(WorkflowDefinition definition, object payload,
        CancellationToken ct)
    {
        var check = await _validation.ValidateAsync(definition, isExecution: true, payload: payload, ct: ct);
        if (!check.IsValid) throw new WorkflowValidationException(check);
        var payloadJson = JsonSerializer.Serialize(payload, WorkflowJson.Options);

        var run = new WorkflowRunDbModel
        {
            WorkflowDefinitionId = definition.Id,
            Status = WorkflowRunStatus.Pending,
            StartedAt = DateTime.Now,
            PayloadJson = payloadJson,
            PayloadSummary = Summarize(payloadJson),
        };
        Runs.Add(run);
        await _db.SaveChangesAsync(ct);

        var runId = run.Id;
        await _taskManager.Enqueue(BTaskBuilder.Create($"workflow.run.{runId}")
            .Named($"Workflow #{definition.Id} run #{runId}")
            .ConflictsWith($"workflow.definition.{definition.Id}")
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
        var result = _validation.ValidateStructure(new WorkflowValidationInputModel
        {
            TriggerKind = triggerKind, TriggerFilterJson = triggerFilterJson, Activities = activities.ToList(),
        });
        if (!result.IsValid)
            throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(d => d.Message)));
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
                Notes = a.Notes,
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
