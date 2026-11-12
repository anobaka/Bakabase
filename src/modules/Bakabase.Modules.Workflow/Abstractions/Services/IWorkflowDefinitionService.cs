using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Workflow.Abstractions.Services;

public interface IWorkflowDefinitionService
{
    Task<WorkflowDefinition> CreateAsync(WorkflowDefinitionCreationInputModel input, CancellationToken ct = default);

    Task<WorkflowDefinition> UpdateAsync(int id, WorkflowDefinitionUpdateInputModel input, CancellationToken ct = default);

    Task DeleteAsync(int id);

    Task<WorkflowDefinition?> GetAsync(int id);

    Task<List<WorkflowDefinition>> SearchAsync(WorkflowDefinitionSearchInputModel input);

    Task<SearchResponse<WorkflowRun>> SearchRunsAsync(WorkflowRunSearchInputModel input);

    /// <summary>
    /// Start a manually runnable trigger by hand. Managed triggers must use their source module.
    /// The payload comes from the definition's trigger — see
    /// <see cref="Components.IWorkflowTrigger.BuildManualPayload"/> — so <paramref name="argsJson"/>
    /// is whatever that trigger asks the user for, or null when it asks for nothing.
    ///
    /// Deliberately skips both gates the event path applies: the trigger filter (the user is
    /// naming this definition, not broadcasting an event) and the enabled flag (a definition is
    /// most worth running by hand precisely while it is switched off and being built).
    /// </summary>
    Task<WorkflowRun> RunManuallyAsync(int definitionId, string? argsJson, CancellationToken ct = default);

    /// <summary>Trusted source adapters may attach verified provenance. Public manual-run input never accepts source references.</summary>
    Task<WorkflowRun> RunManuallyWithSourcesAsync(int definitionId, string? argsJson,
        IReadOnlyList<WorkflowSourceRef> sources, CancellationToken ct = default) =>
        RunManuallyAsync(definitionId, argsJson, ct);

    /// <summary>
    /// Starts a managed trigger from its owning application service with an already prepared,
    /// strongly typed payload. This is not a user-facing manual-run endpoint; the caller owns
    /// resource/task authorization. The workflow still validates its configuration and input.
    /// </summary>
    Task<WorkflowRun> RunManagedAsync(int definitionId, object payload, CancellationToken ct = default);

    /// <summary>Trusted source-module entry point for a prepared sequential plan, without creating a public template.</summary>
    Task<WorkflowRun> RunPlanAsync(WorkflowRunPlan plan, object payload,
        IReadOnlyList<WorkflowSourceRef>? sources = null, CancellationToken ct = default) =>
        throw new NotSupportedException("This workflow host does not support task execution plans.");

    Task<WorkflowRun> RunManuallyAsync(int definitionId, string? argsJson, WorkflowInvocationInput invocation,
        CancellationToken ct = default) => invocation.CallerDefaults?.Count > 0 || invocation.Overrides?.Count > 0
        ? throw new NotSupportedException("This workflow host does not support invocation parameters.")
        : RunManuallyAsync(definitionId, argsJson, ct);

    Task<WorkflowRun> RunManuallyWithSourcesAsync(int definitionId, string? argsJson,
        IReadOnlyList<WorkflowSourceRef> sources, WorkflowInvocationInput invocation, CancellationToken ct = default) =>
        invocation.CallerDefaults?.Count > 0 || invocation.Overrides?.Count > 0
            ? throw new NotSupportedException("This workflow host does not support invocation parameters.")
            : RunManuallyWithSourcesAsync(definitionId, argsJson, sources, ct);

    Task<WorkflowRun> RunManagedAsync(int definitionId, object payload, WorkflowInvocationInput invocation,
        CancellationToken ct = default) => invocation.CallerDefaults?.Count > 0 || invocation.Overrides?.Count > 0
        ? throw new NotSupportedException("This workflow host does not support invocation parameters.")
        : RunManagedAsync(definitionId, payload, ct);

    Task<WorkflowRun> RunPlanAsync(WorkflowRunPlan plan, object payload, WorkflowInvocationInput invocation,
        IReadOnlyList<WorkflowSourceRef>? sources = null, CancellationToken ct = default) =>
        RunPlanAsync(WorkflowInvocationResolver.Resolve(plan, invocation), payload, sources, ct);
}
