using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Triggers;

/// <summary>
/// Trigger fired by <c>SubscriptionService.RunCheckAsync</c> when a subscription has new
/// or updated items. Filter shape:
/// <code>{ "subscriptionIds"?: number[], "kinds"?: string[] }</code>
/// Both fields narrow (AND): a payload matches when it's in <c>subscriptionIds</c> (or it's
/// empty) AND its kind is in <c>kinds</c> (or it's empty). The AND semantics make typed flow
/// sound — pinning <c>kinds</c> to a single source guarantees the emitted item type.
/// </summary>
public class SubscriptionUpdatedTrigger : IWorkflowTrigger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Subscription kind → emitted workflow item type. Mirrored by the frontend trigger UI's
    // resolveOutputItemType. Keep in sync with the subscription providers' Kind values.
    private static readonly Dictionary<string, string> KindToItemType = new(StringComparer.Ordinal)
    {
        ["exhentai.search"] = WorkflowItemTypes.ExHentaiGallery,
        ["exhentai.gallery"] = WorkflowItemTypes.ExHentaiGallery,
        ["pixiv.followLatest"] = WorkflowItemTypes.PixivIllust,
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

        var idOk = filter.SubscriptionIds is not { Length: > 0 } ||
                   filter.SubscriptionIds.Contains(p.SubscriptionId);
        var kindOk = filter.Kinds is not { Length: > 0 } ||
                     filter.Kinds.Contains(p.Kind, StringComparer.Ordinal);
        return idOk && kindOk;
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not SubscriptionUpdatedPayload p) return [];
        // Only the new ones drive downstream automation for now; updated items go through
        // notifications but the workflow ignores them. Adjust here once a use case lands.
        return p.NewItems.Cast<object>().ToList();
    }

    public string ResolveOutputItemType(string? triggerFilterJson)
    {
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return WorkflowItemTypes.SubscriptionAny;

        Filter? filter;
        try { filter = JsonSerializer.Deserialize<Filter>(triggerFilterJson, JsonOptions); }
        catch (JsonException) { return WorkflowItemTypes.SubscriptionAny; }

        var kinds = filter?.Kinds;
        if (kinds is not { Length: > 0 }) return WorkflowItemTypes.SubscriptionAny;

        // Only when every pinned kind maps to the same known item type can we promise it.
        var mapped = kinds.Select(k => KindToItemType.GetValueOrDefault(k)).Distinct().ToList();
        if (mapped.Count == 1 && mapped[0] is { } t) return t;
        return WorkflowItemTypes.SubscriptionAny;
    }

    private record Filter
    {
        public int[]? SubscriptionIds { get; init; }
        public string[]? Kinds { get; init; }
    }
}
