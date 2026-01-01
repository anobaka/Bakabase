namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Defines how a task should behave when its dependency fails
/// </summary>
public enum BTaskDependencyFailurePolicy
{
    /// <summary>
    /// Continue waiting for the dependency to succeed (default behavior)
    /// </summary>
    Wait = 1,

    /// <summary>
    /// Skip the failed dependency and proceed if all non-failed dependencies are complete
    /// </summary>
    Skip = 2,

    /// <summary>
    /// Mark this task as failed when any dependency fails
    /// </summary>
    Fail = 3
}
