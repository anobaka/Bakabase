namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

/// <summary>
/// Per-activity funnel stats for a run: how many items entered the step, how many came out,
/// and how many were dropped by a skip-on-error failure at that step. Lets the UI show where
/// items disappeared without per-item tracking.
/// </summary>
public record WorkflowRunStepStat
{
    public int StepIndex { get; init; }
    public string Kind { get; init; } = "";
    public int InputCount { get; init; }
    public int OutputCount { get; init; }
    public int FailedCount { get; init; }
}
