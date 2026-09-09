using System.Text.Json;
using Bakabase.Modules.Collection.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Modules.Collection.Components.Workflow;

/// <summary>What joining a collection looks like to the workflow engine.</summary>
public record CollectionMembersAddedPayload
{
    public int CollectionId { get; init; }
    public string CollectionName { get; init; } = "";
    public int[] ResourceIds { get; init; } = [];
    public CollectionMembershipOrigin Origin { get; init; }
    public int? SubscriptionId { get; init; }
}

/// <summary>One resource that just joined a collection.</summary>
public record CollectionMemberItem : IHasResourceId
{
    public int ResourceId { get; init; }
    public int CollectionId { get; init; }
    public string CollectionName { get; init; } = "";
    public CollectionMembershipOrigin Origin { get; init; }
    public int? SubscriptionId { get; init; }
}

/// <summary>
/// Fires when resources join a collection, however they got there.
/// <para>
/// This is where the two halves meet: something fills a collection — a person, a subscription, a
/// workflow — and "when something joins this collection that I do not have, go and get it" is then
/// a chain the user builds rather than a feature anyone had to write.
/// </para>
/// <para>Filter shape: <c>{ "collectionIds"?: number[], "origins"?: number[] }</c> — absent matches all.</para>
/// </summary>
public class CollectionMembersAddedTrigger : IWorkflowTrigger
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => CollectionWorkflowKinds.TriggerMembersAdded;
    public string DisplayName => "Added to a collection";
    public Type PayloadType => typeof(CollectionMembersAddedPayload);

    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not CollectionMembersAddedPayload p) return false;
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return true;

        Filter? filter;
        try { filter = JsonSerializer.Deserialize<Filter>(triggerFilterJson, Json); }
        catch (JsonException) { return false; }

        if (filter?.CollectionIds is {Length: > 0} wanted && !wanted.Contains(p.CollectionId))
        {
            return false;
        }

        if (filter?.Origins is {Length: > 0} origins && !origins.Contains((int) p.Origin))
        {
            return false;
        }

        return true;
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not CollectionMembersAddedPayload p) return [];

        // One item per resource: everything downstream — acquire it, enhance it, tell me about it
        // — is about one resource at a time.
        return p.ResourceIds
            .Select(object (id) => new CollectionMemberItem
            {
                ResourceId = id,
                CollectionId = p.CollectionId,
                CollectionName = p.CollectionName,
                Origin = p.Origin,
                SubscriptionId = p.SubscriptionId,
            })
            .ToList();
    }

    public string ResolveOutputItemType(string? triggerFilterJson) =>
        CollectionWorkflowKinds.ItemMember;

    private record Filter
    {
        public int[]? CollectionIds { get; init; }

        /// <summary><see cref="CollectionMembershipOrigin"/> values.</summary>
        public int[]? Origins { get; init; }
    }
}

public class CollectionMemberItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => CollectionWorkflowKinds.ItemMember;
    public string DisplayName => "Collection member";
    public Type ClrType => typeof(CollectionMemberItem);
}
