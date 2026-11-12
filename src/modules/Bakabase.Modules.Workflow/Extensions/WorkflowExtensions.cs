using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Components;

namespace Bakabase.Modules.Workflow.Extensions;

public static class WorkflowExtensions
{
    public static WorkflowActivity ToDomainModel(this WorkflowActivityDbModel db) => new()
    {
        ParameterBindings = ReadParameters<WorkflowParameterBinding>(db.ParameterBindingsJson),
        Id = db.Id,
        WorkflowDefinitionId = db.WorkflowDefinitionId,
        Order = db.Order,
        Kind = db.Kind,
        Notes = db.Notes,
        ConfigJson = db.ConfigJson,
        OnItemError = db.OnItemError,
    };

    public static WorkflowDefinition ToDomainModel(
        this WorkflowDefinitionDbModel db,
        IEnumerable<WorkflowActivityDbModel> activities)
    {
        return new WorkflowDefinition
        {
            Parameters = ReadParameters<WorkflowParameterDefinition>(db.ParametersJson),
            Id = db.Id,
            Name = db.Name,
            Description = db.Description,
            DescriptionKey = db.DescriptionKey,
            TriggerKind = db.TriggerKind,
            TriggerFilterJson = db.TriggerFilterJson,
            Enabled = db.Enabled,
            CreatedAt = db.CreatedAt,
            UpdatedAt = db.UpdatedAt,
            LastRunAt = db.LastRunAt,
            LastError = db.LastError,
            IsBuiltin = db.IsBuiltin,
            Activities = activities.OrderBy(a => a.Order).Select(ToDomainModel).ToList(),
        };
    }

    public static WorkflowRun ToDomainModel(this WorkflowRunDbModel db)
    {
        WorkflowRunPlan? plan;
        try { plan = WorkflowRunPlans.Read(db.PlanSnapshotJson); }
        catch (InvalidDataException) { plan = null; } // Unreadable history stays visible; execution fails closed.
        return new()
        {
        Id = db.Id,
        WorkflowDefinitionId = db.WorkflowDefinitionId,
        HasPlanSnapshot = db.PlanSnapshotJson != null,
        ParameterSources = WorkflowInvocationResolver.SafeSources(plan?.ParameterSnapshot),
        PlanName = plan?.Name,
        TriggerKind = plan?.TriggerKind,
        PlanNodes = plan?.Nodes.Select((n, order) => new WorkflowRunPlanNodeView(n.NodeId, n.Kind, order, n.Notes)).ToList() ?? [],
        Status = db.Status,
        StartedAt = db.StartedAt,
        CompletedAt = db.CompletedAt,
        PayloadJson = db.PayloadJson,
        PayloadSummary = db.PayloadSummary,
        InputCount = db.InputCount,
        OutputCount = db.OutputCount,
        OutputItemsJson = db.OutputItemsJson,
        OutputPreviewTruncated = db.OutputPreviewTruncated,
        FailedItemCount = db.FailedItemCount,
        StepStats = ParseStepStats(db.StepStatsJson),
        NodeExecutions = db.NodeExecutions.OrderBy(n => n.Id).Select(ToDomainModel).ToList(),
        ErrorMessage = db.ErrorMessage,
        CurrentStepIndex = db.CurrentStepIndex,
        WaitReason = db.WaitReason,
        WaitPromptJson = db.WaitPromptJson,
        WaitingSince = db.WaitingSince,
        };
    }

    public static List<T> ReadParameters<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        if (json.Length > 8 * 1024 * 1024) throw new InvalidDataException("The workflow parameter contract is too large.");
        try { return JsonSerializer.Deserialize<List<T>>(json, WorkflowJson.Options) ?? throw new InvalidDataException("The workflow parameter contract is empty."); }
        catch (JsonException error) { throw new InvalidDataException("The workflow parameter contract is invalid.", error); }
    }

    private static readonly JsonSerializerOptions StepStatsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static List<WorkflowRunStepStat> ParseStepStats(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<WorkflowRunStepStat>>(json, StepStatsJsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    public static WorkflowNodeExecution ToDomainModel(this WorkflowNodeExecutionDbModel db) => new()
    {
        Id = db.Id, NodeId = db.NodeId, StepIndex = db.StepIndex, Kind = db.Kind, Attempt = db.Attempt,
        Status = db.Status, StartedAt = db.StartedAt, CompletedAt = db.CompletedAt, InputCount = db.InputCount,
        OutputCount = db.OutputCount, FailedCount = db.FailedCount, ResumeKind = db.ResumeKind,
        WaitReason = db.WaitReason, Outputs = WorkflowOutputMetadataCapture.Read(db.OutputsJson),
        OutputsTruncated = db.OutputsTruncated,
        Inputs = WorkflowOutputMetadataCapture.Read(db.InputsJson), InputsRecorded = db.InputsJson != null,
        InputsTruncated = db.InputsTruncated
    };
}
