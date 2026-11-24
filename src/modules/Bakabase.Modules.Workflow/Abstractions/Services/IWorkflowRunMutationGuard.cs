namespace Bakabase.Modules.Workflow.Abstractions.Services;

/// <summary>
/// Lets an owning module prevent a historical run from being resumed or changed after its
/// business task was removed. Called while holding the run mutation gate. Implementations
/// must only inspect ownership and must not acquire a business-task gate or mutate state.
/// </summary>
public interface IWorkflowRunMutationGuard
{
    /// <summary>Validate a specialized answer against the current persisted wait before it is queued.
    /// Runs under the same mutation gate; a rejected answer leaves the wait intact.</summary>
    void ValidateAnswer(Models.Domain.WorkflowActivity? activity, string? waitReason, string? prompt, string signalJson) { }

    /// <summary>Read-only snapshot of definite ownership refusals for presenting actions. Execution still takes the run gate.</summary>
    Task<IReadOnlyDictionary<int, string>> ReadMutationRefusalsAsync(IReadOnlyCollection<int> runIds,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, string>>(new Dictionary<int, string>());

    Task EnsureCanMutateAsync(int runId, CancellationToken ct = default);
}
