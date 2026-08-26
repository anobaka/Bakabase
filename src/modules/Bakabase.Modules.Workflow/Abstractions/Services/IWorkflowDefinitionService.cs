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
    /// Start a run by hand. The payload comes from the definition's trigger — see
    /// <see cref="Components.IWorkflowTrigger.BuildManualPayload"/> — so <paramref name="argsJson"/>
    /// is whatever that trigger asks the user for, or null when it asks for nothing.
    ///
    /// Deliberately skips both gates the event path applies: the trigger filter (the user is
    /// naming this definition, not broadcasting an event) and the enabled flag (a definition is
    /// most worth running by hand precisely while it is switched off and being built).
    /// </summary>
    Task<WorkflowRun> RunManuallyAsync(int definitionId, string? argsJson, CancellationToken ct = default);
}
