using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.Workflow.Resources;

namespace Bakabase.Service.Components.Workflow.Triggers;

/// <summary>
/// Fired when a subscription brings in members its collection did not have.
/// <para>
/// Every source emits the same thing — a resource — because by the time this fires each item has
/// already become one. The per-kind item-type table this used to keep was three places to update
/// whenever a provider was added, and all three said "resource" in the end.
/// </para>
/// Filter shape: <code>{ "subscriptionIds"?: number[], "kinds"?: string[] }</code>
/// Both narrow (AND): a payload matches when it is in <c>subscriptionIds</c> (or that is empty)
/// AND its kind is in <c>kinds</c> (or that is empty).
/// </summary>
public class SubscriptionUpdatedTrigger : IWorkflowTrigger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Kind { get; } = SubscriptionWorkflowKinds.TriggerUpdated;
    public string DisplayName => "Subscription updated";
    public Type PayloadType => typeof(SubscriptionUpdatedPayload);

    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not SubscriptionUpdatedPayload p) return false;
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return true;

        Filter? filter;
        try { filter = JsonSerializer.Deserialize<Filter>(triggerFilterJson, JsonOptions); }
        catch (JsonException) { return false; }
        if (filter is null) return true;

        var idOk = filter.SubscriptionIds is not {Length: > 0} ||
                   filter.SubscriptionIds.Contains(p.SubscriptionId);
        var kindOk = filter.Kinds is not {Length: > 0} ||
                     filter.Kinds.Contains(p.Kind, StringComparer.Ordinal);

        return idOk && kindOk;
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not SubscriptionUpdatedPayload p) return [];

        return p.Resources
            .Select(object (r) => new ResourceWorkflowItem
            {
                Id = r.ResourceId,
                Name = r.Name,
                Path = r.Path,
                HasLocalPath = r.HasLocalPath,
            })
            .ToList();
    }

    public string ResolveOutputItemType(string? triggerFilterJson) => WorkflowItemTypes.Resource;

    private record Filter
    {
        public int[]? SubscriptionIds { get; init; }
        public string[]? Kinds { get; init; }
    }
}
