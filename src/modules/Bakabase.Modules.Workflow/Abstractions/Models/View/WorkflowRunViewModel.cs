using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowRunViewModel
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowRunStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PayloadSummary { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int FailedItemCount { get; set; }
    public List<WorkflowRunStepStat> StepStats { get; set; } = [];
    public string? ErrorMessage { get; set; }

    public static WorkflowRunViewModel From(WorkflowRun r) => new()
    {
        Id = r.Id,
        WorkflowDefinitionId = r.WorkflowDefinitionId,
        Status = r.Status,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt,
        PayloadSummary = r.PayloadSummary,
        InputCount = r.InputCount,
        OutputCount = r.OutputCount,
        FailedItemCount = r.FailedItemCount,
        StepStats = r.StepStats,
        ErrorMessage = r.ErrorMessage,
    };
}
