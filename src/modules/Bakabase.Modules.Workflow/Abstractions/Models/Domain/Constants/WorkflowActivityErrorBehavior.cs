namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

/// <summary>
/// Per-activity policy for how a single item's failure during ProcessItemAsync is handled.
/// </summary>
public enum WorkflowActivityErrorBehavior
{
    /// <summary>Abort the run on the first item that throws — default, surfaces problems loudly.</summary>
    Fail = 1,

    /// <summary>Drop the failing item from the chain, increment the run's failed-item counter, keep going.</summary>
    Skip = 2,
}
