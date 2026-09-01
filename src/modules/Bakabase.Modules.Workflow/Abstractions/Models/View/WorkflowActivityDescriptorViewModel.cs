using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowActivityDescriptorViewModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public WorkflowActivityCategory Category { get; set; }

    /// <summary>Free-form group tag for the picker's section layout (e.g. "exhentai", "ai").</summary>
    public string Group { get; set; } = "";

    /// <summary>Empty = accepts any item type.</summary>
    public List<string> AcceptedInputItemTypes { get; set; } = [];

    public WorkflowItemTypeBehavior OutputBehavior { get; set; }

    /// <summary>Set only when <see cref="OutputBehavior"/> is Fixed.</summary>
    public string? FixedOutputItemType { get; set; }

    /// <summary>Mirrors <c>IWorkflowActivity.IsDestructive</c> so the editor can apply the same
    /// "no model-generated input directly into a destructive step" rule the backend enforces.</summary>
    public bool IsDestructive { get; set; }
}
