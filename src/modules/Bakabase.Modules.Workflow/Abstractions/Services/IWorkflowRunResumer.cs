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
}
