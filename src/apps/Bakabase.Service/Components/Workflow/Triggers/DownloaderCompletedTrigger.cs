using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
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
    public WorkflowActivationMode ActivationMode => WorkflowActivationMode.SystemEvent;
    public string SourceModule => "downloader";
    public string Description => "When a downloader task completes, enabled workflows matching its downloader source receive the completed task. This includes a pre-check that finds nothing left to download. It is one event per completed task, not per downloaded file or saved result.";
    public string DescriptionKey => "workflow.trigger.downloaderCompleted.description";
    public Type PayloadType => typeof(DownloaderCompletedPayload);

    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not DownloaderCompletedPayload p) return false;
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return true;

        var f = JsonSerializer.Deserialize<Filter>(triggerFilterJson, JsonOptions);

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
