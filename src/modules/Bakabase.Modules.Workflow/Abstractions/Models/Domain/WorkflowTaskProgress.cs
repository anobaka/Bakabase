namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

public sealed record WorkflowTaskProgress(int WorkflowRunId, string ActivityKind, string Stage,
    int? StepIndex = null, int? StageProgress = null);
