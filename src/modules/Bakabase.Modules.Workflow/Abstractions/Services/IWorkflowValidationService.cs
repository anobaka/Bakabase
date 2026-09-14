using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;

namespace Bakabase.Modules.Workflow.Abstractions.Services;

public interface IWorkflowValidationService
{
    /// <summary>Pure type-chain checks used by persistence; environment readiness may be incomplete in a draft.</summary>
    WorkflowValidationResult ValidateStructure(WorkflowValidationInputModel input);

    /// <summary>Structure plus node configuration/environment checks. Never executes nodes.
    /// For a resumed run, startNodeIndex skips completed nodes' configuration checks while
    /// retaining validation of the full type chain.</summary>
    Task<WorkflowValidationResult> ValidateAsync(WorkflowValidationInputModel input,
        bool isExecution = false, object? payload = null, CancellationToken ct = default, int startNodeIndex = 0);

    Task<WorkflowValidationResult> ValidateAsync(WorkflowDefinition definition,
        bool isExecution = false, object? payload = null, CancellationToken ct = default, int startNodeIndex = 0);
}
