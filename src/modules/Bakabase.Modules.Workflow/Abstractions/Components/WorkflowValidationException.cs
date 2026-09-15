using Bakabase.Modules.Workflow.Abstractions.Models.View;

namespace Bakabase.Modules.Workflow.Abstractions.Components;

public sealed class WorkflowValidationException(WorkflowValidationResult result)
    : InvalidOperationException(string.Join("; ", result.Diagnostics
        .Where(d => d.Severity == "error").Select(d => d.Message)))
{
    public WorkflowValidationResult Result { get; } = result;
}
