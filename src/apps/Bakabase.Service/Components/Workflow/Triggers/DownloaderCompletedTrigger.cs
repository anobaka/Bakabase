using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Triggers;

/// <summary>
/// Trigger fired by <c>DownloadTaskService.OnStatusChanged</c> when a download task
/// reaches Complete. Filter shape:
/// <code>{ "thirdPartyIds"?: number[] }</code>
/// Empty / absent <c>thirdPartyIds</c> matches every completed task; populated narrows
/// to those sources.
/// </summary>
public class DownloaderCompletedTrigger : IWorkflowTrigger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Kind { get; } = DownloaderWorkflowKinds.TriggerCompleted;
    public string DisplayName => "Download task completed";
    public Type PayloadType => typeof(DownloaderCompletedPayload);

    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not DownloaderCompletedPayload p) return false;
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return true;

        Filter? f;
        try { f = JsonSerializer.Deserialize<Filter>(triggerFilterJson, JsonOptions); }
        catch (JsonException) { return false; }

        if (f?.ThirdPartyIds is not { Length: > 0 } pinned) return true;
        return pinned.Contains(p.ThirdPartyId);
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        // One completed task = one item.
        return payload is DownloaderCompletedPayload p ? [p] : [];
    }

    public string ResolveOutputItemType(string? triggerFilterJson) =>
        WorkflowItemTypes.DownloaderCompleted;

    private record Filter
    {
        public int[]? ThirdPartyIds { get; init; }
    }
}
