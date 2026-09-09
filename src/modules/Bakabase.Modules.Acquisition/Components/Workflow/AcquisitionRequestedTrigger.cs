using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Acquisition.Components.Workflow;

/// <summary>
/// What starting an acquisition looks like to the workflow engine.
/// </summary>
public record AcquisitionRequestedPayload
{
    public int TaskId { get; init; }
    public int ResourceId { get; init; }
    public int? CollectionId { get; init; }
    public AcquisitionLeadKind LeadKind { get; init; }
    public string LeadValue { get; init; } = "";
    public string? Title { get; init; }

    /// <summary>Where this run may write. Set by the service, which owns the layout.</summary>
    public string WorkingDirectory { get; init; } = "";

    /// <summary>The name the placement step will use unless a transform rewrites it first.</summary>
    public string WorkingName { get; init; } = "";
}

/// <summary>
/// A recipe's entry point. Unlike every other trigger it listens to nothing: a recipe runs because
/// someone asked for a specific resource, so <see cref="Matches"/> is always false and the only way
/// in is a manual run started by <c>IAcquisitionService</c>.
/// <para>
/// Being a trigger anyway is what makes a recipe an ordinary workflow — editable in the same canvas,
/// visible in the same run history, mixable with the text and notification activities.
/// </para>
/// </summary>
public class AcquisitionRequestedTrigger : IWorkflowTrigger
{
    public string Kind => AcquisitionWorkflowKinds.TriggerRequested;
    public string DisplayName => "Acquisition requested";
    public Type PayloadType => typeof(AcquisitionRequestedPayload);

    /// <summary>
    /// Never. A recipe is not fanned out to by an event bus — it is asked for. Returning true here
    /// would run every recipe the user has for every acquisition they start.
    /// </summary>
    public bool Matches(object payload, string? triggerFilterJson) => false;

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not AcquisitionRequestedPayload p) return [];

        // Exactly one, always: the single-item run is what lets a recipe stop and wait.
        return
        [
            new AcquisitionWorkItem
            {
                ResourceId = p.ResourceId,
                LeadKind = p.LeadKind,
                LeadValue = p.LeadValue,
                CollectionId = p.CollectionId,
                Title = p.Title,
                WorkingDirectory = p.WorkingDirectory,
                WorkingName = p.WorkingName,
            }
        ];
    }

    public string ResolveOutputItemType(string? triggerFilterJson) =>
        AcquisitionWorkflowKinds.ItemAcquisition;

    public bool RequiresManualPayload => true;
}

public class AcquisitionItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => AcquisitionWorkflowKinds.ItemAcquisition;
    public string DisplayName => "Acquisition";
    public Type ClrType => typeof(AcquisitionWorkItem);
}
