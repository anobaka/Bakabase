namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

public enum WorkflowRunStatus
{
    Pending = 1,
    Running = 2,
    Success = 3,
    Failed = 4,
    Cancelled = 5,
    /// <summary>App restarted while this run was in-flight; the runner can't safely resume mid-chain.</summary>
    Interrupted = 6,
}
