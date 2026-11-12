namespace Bakabase.Modules.Workflow.Abstractions.Services;

/// <summary>
/// Lets an owning module prevent a historical run from being resumed or changed after its
/// business task was removed. Called while holding the run mutation gate. Implementations
/// must only inspect ownership and must not acquire a business-task gate or mutate state.
/// </summary>
public interface IWorkflowRunMutationGuard
{
    Task EnsureCanMutateAsync(int runId, CancellationToken ct = default);
}
