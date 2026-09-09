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

    /// <summary>
    /// Stopped at a step that needs something from outside — a file, a password, a decision. It
    /// stays here until a signal arrives, however long that takes, and a restart does not disturb
    /// it.
    /// </summary>
    Waiting = 7,
}
