namespace Bakabase.Modules.Subscription.Workflow;

/// <summary>
/// Payload published on <see cref="SubscriptionWorkflowKinds.TriggerUpdated"/>.
/// <para>
/// It carries resources rather than the source's own item shapes: by the time this is published
/// every item has become a resource, and everything downstream acts on resources. That is also why
/// every source emits the same item type — the per-provider mapping table it replaced had three
/// entries that all meant "resource" in the end.
/// </para>
/// </summary>
public record SubscriptionUpdatedPayload
{
    public int SubscriptionId { get; init; }
    public string Kind { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>The collection this source fills.</summary>
    public int CollectionId { get; init; }

    /// <summary>The resources that were not members before this check.</summary>
    public IReadOnlyList<SubscriptionResourceSummary> Resources { get; init; } = [];
}

/// <summary>
/// Enough of a resource for a chain to start on, read once by the service rather than per activity.
/// </summary>
public record SubscriptionResourceSummary(int ResourceId, string? Name, string? Path)
{
    public bool HasLocalPath => !string.IsNullOrEmpty(Path);
}
