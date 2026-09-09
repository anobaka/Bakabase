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
}
