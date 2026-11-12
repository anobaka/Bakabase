namespace Bakabase.Modules.Workflow.Abstractions.Services;

/// <summary>Preserves a business owner's durable pause across process restarts, without blocking explicit continuation.</summary>
public interface IWorkflowRunStartupPolicy
{
    Task<bool> CanResumeOnStartupAsync(int runId, CancellationToken ct = default);
}
