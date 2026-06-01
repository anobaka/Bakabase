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
    /// Manually trigger a run with a user-supplied payload (e.g. from the UI "Run now" button
    /// or a test endpoint). Bypasses trigger filtering — runs unconditionally.
    /// </summary>
    Task<WorkflowRun> RunNowAsync(int definitionId, object payload, CancellationToken ct = default);
}
