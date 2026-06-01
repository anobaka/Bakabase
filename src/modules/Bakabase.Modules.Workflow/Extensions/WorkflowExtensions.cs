using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Workflow.Extensions;

public static class WorkflowExtensions
{
    public static WorkflowActivity ToDomainModel(this WorkflowActivityDbModel db) => new()
    {
        Id = db.Id,
        WorkflowDefinitionId = db.WorkflowDefinitionId,
        Order = db.Order,
        Kind = db.Kind,
        ConfigJson = db.ConfigJson,
        OnItemError = db.OnItemError,
    };

    public static WorkflowDefinition ToDomainModel(
        this WorkflowDefinitionDbModel db,
        IEnumerable<WorkflowActivityDbModel> activities)
    {
        return new WorkflowDefinition
        {
            Id = db.Id,
            Name = db.Name,
            TriggerKind = db.TriggerKind,
            TriggerFilterJson = db.TriggerFilterJson,
            Enabled = db.Enabled,
            CreatedAt = db.CreatedAt,
            UpdatedAt = db.UpdatedAt,
            LastRunAt = db.LastRunAt,
            LastError = db.LastError,
            Activities = activities.OrderBy(a => a.Order).Select(ToDomainModel).ToList(),
        };
    }

    public static WorkflowRun ToDomainModel(this WorkflowRunDbModel db) => new()
    {
        Id = db.Id,
        WorkflowDefinitionId = db.WorkflowDefinitionId,
        Status = db.Status,
        StartedAt = db.StartedAt,
        CompletedAt = db.CompletedAt,
        PayloadJson = db.PayloadJson,
        PayloadSummary = db.PayloadSummary,
        InputCount = db.InputCount,
        OutputCount = db.OutputCount,
        FailedItemCount = db.FailedItemCount,
        StepStats = ParseStepStats(db.StepStatsJson),
        ErrorMessage = db.ErrorMessage,
    };

    private static readonly JsonSerializerOptions StepStatsJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static List<WorkflowRunStepStat> ParseStepStats(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try { return JsonSerializer.Deserialize<List<WorkflowRunStepStat>>(json, StepStatsJsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }
}
