using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowActivityDescriptorViewModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public WorkflowActivityCategory Category { get; set; }

    /// <summary>Free-form group tag for the picker's section layout (e.g. "exhentai", "ai").</summary>
    public string Group { get; set; } = "";

    /// <summary>Empty = accepts any item type (unless <see cref="AcceptedItemInterface"/> is set).</summary>
    public List<string> AcceptedInputItemTypes { get; set; } = [];

    /// <summary>
    /// Name of the capability contract (e.g. "ITextWorkpiece") when the activity accepts by
    /// interface — matched against the item-type descriptors' <c>implementsInterfaces</c>. This
    /// is the B1 convergence shape (capability map §9·决定 4): the backend ships the contract
    /// facts, the frontend's chain walk stays a generic set operation.
    /// </summary>
    public string? AcceptedItemInterface { get; set; }

    public WorkflowItemTypeBehavior OutputBehavior { get; set; }

    /// <summary>Set only when <see cref="OutputBehavior"/> is Fixed.</summary>
    public string? FixedOutputItemType { get; set; }

    /// <summary>Mirrors <c>IWorkflowActivity.IsDestructive</c> so the editor can apply the same
    /// "no model-generated input directly into a destructive step" rule the backend enforces.</summary>
    public bool IsDestructive { get; set; }
}
