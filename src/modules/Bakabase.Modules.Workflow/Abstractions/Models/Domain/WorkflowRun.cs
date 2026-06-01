using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

public record WorkflowRun
{
    public int Id { get; set; }
    public int WorkflowDefinitionId { get; set; }
    public WorkflowRunStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? PayloadJson { get; set; }
    public string? PayloadSummary { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int FailedItemCount { get; set; }
    public List<WorkflowRunStepStat> StepStats { get; set; } = [];
    public string? ErrorMessage { get; set; }
}
