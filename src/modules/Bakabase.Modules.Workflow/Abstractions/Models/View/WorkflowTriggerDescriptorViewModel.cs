namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowTriggerDescriptorViewModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}
