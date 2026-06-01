using Bakabase.Modules.Subscription.Abstractions.Models.Domain;

namespace Bakabase.Modules.Subscription.Workflow;

/// <summary>
/// Payload published on <see cref="SubscriptionWorkflowKinds.TriggerUpdated"/>.
/// </summary>
public record SubscriptionUpdatedPayload
{
    public int SubscriptionId { get; init; }
    public string Kind { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public IReadOnlyList<SubscriptionItem> NewItems { get; init; } = [];
    public IReadOnlyList<SubscriptionItem> UpdatedItems { get; init; } = [];
}
