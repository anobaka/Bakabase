using System.Globalization;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Workflow.Components;

/// <summary>One source of truth for frozen plans and the explicit legacy fallback.</summary>
public static class WorkflowRunPlans
{
    private const int MaximumCharacters = 8 * 1024 * 1024;

    public static WorkflowRunPlan Capture(WorkflowDefinition definition) => new()
    {
        Name = definition.Name, TriggerKind = definition.TriggerKind,
        Parameters = definition.Parameters, ParameterSnapshot = definition.ParameterSnapshot,
        TriggerFilterJson = definition.TriggerFilterJson,
        TemplateDefinitionId = definition.Id > 0 ? definition.Id : null,
        Nodes = definition.Activities.OrderBy(a => a.Order).Select(a => new WorkflowRunPlanNode
        {
            NodeId = a.NodeId ?? "activity:" + a.Id.ToString(CultureInfo.InvariantCulture),
            Kind = a.Kind, ConfigJson = a.ConfigJson, Notes = a.Notes, OnItemError = a.OnItemError,
            ParameterBindings = a.ParameterBindings
        }).ToArray()
    };

    public static string Serialize(WorkflowRunPlan plan)
    {
        Validate(plan);
        var json = JsonSerializer.Serialize(plan, WorkflowJson.Options);
        if (json.Length > MaximumCharacters) throw new ArgumentException("The execution plan is too large.");
        return json;
    }

    public static WorkflowRunPlan? Read(string? json)
    {
        if (json == null) return null;
        if (json.Length > MaximumCharacters) throw new InvalidDataException("The saved execution plan is too large.");
        try
        {
            var plan = JsonSerializer.Deserialize<WorkflowRunPlan>(json, WorkflowJson.Options)
                ?? throw new InvalidDataException("The saved execution plan is empty.");
            Validate(plan);
            return plan;
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            // Never fall back to today's template when an existing snapshot cannot be read.
            throw new InvalidDataException("The saved execution plan is invalid or unsupported.", error);
        }
    }

    public static WorkflowDefinition ToDefinition(WorkflowRunPlan plan)
    {
        Validate(plan);
        var id = plan.TemplateDefinitionId ?? 0;
        return new() {Id = id, Name = plan.Name, TriggerKind = plan.TriggerKind,
            TriggerFilterJson = plan.TriggerFilterJson, Enabled = true,
            Parameters = plan.Parameters, ParameterSnapshot = plan.ParameterSnapshot,
            Activities = plan.Nodes.Select((node, order) => new WorkflowActivity
            {
                WorkflowDefinitionId = id, Order = order, NodeId = node.NodeId, Kind = node.Kind,
                Notes = node.Notes, ConfigJson = node.ConfigJson, OnItemError = node.OnItemError,
                ParameterBindings = node.ParameterBindings
            }).ToList()};
    }

    public static async Task<WorkflowDefinition> LoadAsync<TDbContext>(TDbContext db, WorkflowRunDbModel run,
        CancellationToken ct = default) where TDbContext : DbContext
    {
        if (Read(run.PlanSnapshotJson) is { } plan)
        {
            if (plan.ParameterSnapshot == null && (plan.Parameters.Count > 0 || plan.Nodes.Any(n => n.ParameterBindings.Count > 0)))
                throw new InvalidDataException("This attempt has no resolved invocation parameters. Start a new invocation with the required values.");
            return ToDefinition(plan);
        }
        var definition = await db.Set<WorkflowDefinitionDbModel>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == run.WorkflowDefinitionId, ct)
            ?? throw new InvalidOperationException($"Workflow #{run.WorkflowDefinitionId} not found; this legacy run has no saved execution plan.");
        var activities = await db.Set<WorkflowActivityDbModel>().AsNoTracking()
            .Where(a => a.WorkflowDefinitionId == definition.Id).ToListAsync(ct);
        var legacy = definition.ToDomainModel(activities);
        // Snapshot-less runs predate invocation contracts. Preserve their fixed-config semantics;
        // introducing parameters on a template must not inject today's caller/default values.
        return legacy with { Parameters = [], Activities = legacy.Activities.Select(a => a with { ParameterBindings = [] }).ToList() };
    }

    private static void Validate(WorkflowRunPlan plan)
    {
        if (plan.Version != 1 || string.IsNullOrWhiteSpace(plan.Name) || plan.Name.Length > 512 ||
            string.IsNullOrWhiteSpace(plan.TriggerKind) || plan.TriggerKind.Length > 128 ||
            plan.TemplateDefinitionId is <= 0 || plan.TriggerFilterJson?.Length > 262144 ||
            plan.Nodes == null || plan.Nodes.Count > 256)
            throw new ArgumentException("Provide a supported, bounded workflow execution plan.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in plan.Nodes)
            if (node == null || string.IsNullOrWhiteSpace(node.NodeId) || node.NodeId.Length > 128 ||
                node.NodeId.Any(char.IsControl) || !ids.Add(node.NodeId) || string.IsNullOrWhiteSpace(node.Kind) ||
                node.Kind.Length > 128 || node.ConfigJson == null || node.ConfigJson.Length > 262144 ||
                node.Notes?.Length > 8192 || !Enum.IsDefined(node.OnItemError))
                throw new ArgumentException("Execution nodes require unique stable ids and valid, bounded configuration.");
        WorkflowInvocationResolver.ValidateContract(plan);
    }
}
