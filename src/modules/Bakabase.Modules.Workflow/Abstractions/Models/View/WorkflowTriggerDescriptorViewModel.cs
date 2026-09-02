namespace Bakabase.Modules.Workflow.Abstractions.Models.View;

public record WorkflowTriggerDescriptorViewModel
{
    public string Kind { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    /// <summary>
    /// Whether starting a run by hand needs the user to supply a payload. Drives which panel the
    /// UI shows behind "run now": an editor for the payload, or nothing at all.
    /// </summary>
    public bool RequiresManualPayload { get; set; }

    /// <summary>
    /// Shape of the payload this trigger publishes, reflected the same way item types are, so the
    /// manual-run editor can tell the user what to type instead of leaving them to guess.
    /// Empty when <see cref="RequiresManualPayload"/> is false.
    /// </summary>
    public List<WorkflowItemTypeFieldViewModel> PayloadFields { get; set; } = [];
}
