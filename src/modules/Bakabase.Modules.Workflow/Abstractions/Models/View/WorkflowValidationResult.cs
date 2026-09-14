using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowValidationDiagnostic : WorkflowValidationIssue
{
    public string? NodeId { get; init; }
    /// <summary>Zero-based chain position. Null for workflow/trigger diagnostics.</summary>
    public int? NodeIndex { get; init; }
    public string? Kind { get; init; }
}

public record WorkflowValidationResult
{
    public bool IsValid => Diagnostics.All(d => d.Severity != "error");
    public List<WorkflowValidationDiagnostic> Diagnostics { get; init; } = [];
}
