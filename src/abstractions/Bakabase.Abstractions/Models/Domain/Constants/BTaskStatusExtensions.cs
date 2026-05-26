namespace Bakabase.Abstractions.Models.Domain.Constants;

/// <summary>
/// Centralizes the "what is this task currently doing?" predicates.
/// Each predicate is named for the question it answers — sites that combine
/// the same set of statuses ad-hoc tend to drift apart when a new state
/// (like the transitional <see cref="BTaskStatus.Pausing"/> /
/// <see cref="BTaskStatus.Resuming"/>) is introduced.
/// </summary>
public static class BTaskStatusExtensions
{
    /// <summary>
    /// The task body is attached and running, paused, or in any
    /// transitional state. Used for conflict detection, "manager is busy"
    /// indicators, and shutdown checks.
    /// </summary>
    public static bool IsActive(this BTaskStatus s) => s is
        BTaskStatus.Running or BTaskStatus.Paused
        or BTaskStatus.Pausing or BTaskStatus.Resuming
        or BTaskStatus.Cancelling;

    /// <summary>
    /// Task is active OR scheduled to run (NotStarted with auto-start
    /// pending). Used to detect "would re-enqueueing this be redundant?".
    /// </summary>
    public static bool IsActiveOrPending(this BTaskStatus s) =>
        s == BTaskStatus.NotStarted || s.IsActive();

    /// <summary>
    /// <c>Stop()</c> is meaningful — the body is still attached and can
    /// observe cancellation. <see cref="BTaskStatus.Cancelling"/> is
    /// excluded because we're already there. Also doubles as "remaining
    /// time is meaningful" since the same set covers in-flight tasks.
    /// </summary>
    public static bool CanBeStopped(this BTaskStatus s) => s is
        BTaskStatus.Running or BTaskStatus.Paused
        or BTaskStatus.Pausing or BTaskStatus.Resuming;

    /// <summary>
    /// The task is making forward progress (or about to) and the
    /// stopwatch is running. <see cref="BTaskStatus.Paused"/> is excluded
    /// because OnPause stops the stopwatch and the body is blocked.
    /// Used by the heartbeat to push live elapsed/remaining updates, and
    /// by <c>Start()</c> to detect when starting is a no-op.
    /// </summary>
    public static bool IsAdvancing(this BTaskStatus s) => s is
        BTaskStatus.Running or BTaskStatus.Pausing or BTaskStatus.Resuming
        or BTaskStatus.Cancelling;

    /// <summary>
    /// Task has reached a terminal lifecycle state — the body is gone.
    /// </summary>
    public static bool IsFinished(this BTaskStatus s) => s is
        BTaskStatus.Completed or BTaskStatus.Error or BTaskStatus.Cancelled;
}
