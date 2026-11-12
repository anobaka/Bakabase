namespace Bakabase.Modules.Workflow.Abstractions.Services;

/// <summary>
/// Answers a run that is waiting. The signal is whatever the suspension asked for — the file that
/// was found, the password that was typed, the link that was chosen.
/// </summary>
public interface IWorkflowRunResumer
{
    /// <exception cref="InvalidOperationException">
    /// There is no such run, or it is not waiting for anything.
    /// </exception>
    Task ResumeAsync(int runId, string signalJson, CancellationToken ct = default);

    /// <summary>
    /// Runs a stopped run again from its cursor — the "try that again" of a failed, cancelled or
    /// interrupted run. Only the step at the cursor is re-run, so a chain that got most of the way
    /// does not start over.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// There is no such run, or it has not stopped (a run still going does not need requeuing, and
    /// a successful one has nothing left to do).
    /// </exception>
    Task RequeueAsync(int runId, CancellationToken ct = default);

    /// <summary>Checks a caller's immutable preview under the run mutation gate immediately before
    /// continuing the original cursor. The callback must not acquire the owner's task gate.</summary>
    Task ContinueConditionallyAsync(int runId, bool waiting, string signalJson,
        Func<CancellationToken, Task> validate, CancellationToken ct = default) =>
        throw new NotSupportedException("Conditional continuation is not supported by this workflow host.");

    /// <summary>
    /// Changes what a waiting run says it is waiting for, without resuming it. For when the
    /// question got harder while nobody was looking — two files turned up in the inbox and the
    /// run that was waiting for one of them now needs a person to say which.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no such run, or it is not waiting.</exception>
    Task UpdateWaitAsync(int runId, string reason, string? promptJson, CancellationToken ct = default);

    /// <summary>
    /// Re-enters a waiting activity without supplying a human answer. This is used when the
    /// persisted activity configuration was safely upgraded in place (for example, enabling a
    /// bounded post-download assessment) and the current input must be inspected again.
    /// </summary>
    Task ReprocessWaitingAsync(int runId, CancellationToken ct = default) =>
        throw new NotSupportedException("Waiting activities cannot be reprocessed by this workflow host.");
}
