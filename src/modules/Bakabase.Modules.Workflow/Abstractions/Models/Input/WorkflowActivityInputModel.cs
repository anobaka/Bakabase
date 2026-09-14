using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Input;

/// <summary>One configured step in a create/update payload.</summary>
public record WorkflowActivityInputModel
{
    /// <summary>Transient editor identity for diagnostics; not persisted.</summary>
    public string? NodeId { get; set; }
    public string Kind { get; set; } = null!;
    public string? Notes { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public WorkflowActivityErrorBehavior OnItemError { get; set; } = WorkflowActivityErrorBehavior.Fail;
}
