using System.Text.Json;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Acquisition.Components.Workflow;

public record AcquisitionStatusChangedPayload
{
    public int AcquisitionTaskId { get; init; }
    public int? RunId { get; init; }
    public AcquisitionStatus Status { get; init; }
    public int ResourceId { get; init; }
    public int? CollectionId { get; init; }
    public AcquisitionLeadKind LeadKind { get; init; }
    public AcquisitionWaitReason? WaitReason { get; init; }
    public string? TargetDirectory { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Fires whenever an acquisition changes state. This is the outer half of the arrangement: recipes
/// are workflows that acquire, and this trigger is how the user's own workflows react to one —
/// "tell me when something I am waiting for needs me", "when a Circle X release lands, run the
/// enhancers".
/// <para>Filter shape: <c>{ "statuses"?: number[] }</c> — absent matches every change.</para>
/// </summary>
public class AcquisitionStatusChangedTrigger : IWorkflowTrigger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionWorkflowKinds.TriggerStatusChanged;
    public string DisplayName => "Acquisition status changed";
    public Type PayloadType => typeof(AcquisitionStatusChangedPayload);

    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not AcquisitionStatusChangedPayload p) return false;
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return true;

        Filter? filter;
        try { filter = JsonSerializer.Deserialize<Filter>(triggerFilterJson, Json); }
        catch (JsonException) { return false; }

        if (filter?.Statuses is not {Length: > 0} wanted) return true;

        return wanted.Contains((int) p.Status);
    }

    public IReadOnlyList<object> ExtractItems(object payload) =>
        payload is AcquisitionStatusChangedPayload p ? [p] : [];

    public string ResolveOutputItemType(string? triggerFilterJson) =>
        AcquisitionWorkflowKinds.ItemStatusChange;

    private record Filter
    {
        /// <summary><see cref="AcquisitionStatus"/> values.</summary>
        public int[]? Statuses { get; init; }
    }
}

public class AcquisitionStatusChangeItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => AcquisitionWorkflowKinds.ItemStatusChange;
    public string DisplayName => "Acquisition status change";
    public Type ClrType => typeof(AcquisitionStatusChangedPayload);
}
