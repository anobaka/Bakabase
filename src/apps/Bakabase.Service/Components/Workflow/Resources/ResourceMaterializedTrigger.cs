using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Resources;

/// <summary>
/// Fires when a resource gains local files. Filter shape:
/// <code>{ "sources"?: number[] }</code>
/// Absent or empty matches every materialization; populated narrows to resources carrying an
/// identity on one of those platforms — "when a DLsite work I bought finally lands, do X".
/// </summary>
public class ResourceMaterializedTrigger : IWorkflowTrigger
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public string Kind { get; } = ResourceWorkflowKinds.TriggerMaterialized;
    public string DisplayName => "Resource materialized";
    public WorkflowActivationMode ActivationMode => WorkflowActivationMode.SystemEvent;
    public string SourceModule => "resource";
    public string Description => "When the materialization service successfully links a resource to existing local content, enabled workflows matching its resource sources receive that resource. This is not an event for every resource creation or filesystem change, and unlinking content does not publish it.";
    public string DescriptionKey => "workflow.trigger.resourceMaterialized.description";
    public Type PayloadType => typeof(ResourceMaterializedPayload);

    public bool Matches(object payload, string? triggerFilterJson)
    {
        if (payload is not ResourceMaterializedPayload p) return false;
        if (string.IsNullOrWhiteSpace(triggerFilterJson)) return true;

        var f = JsonSerializer.Deserialize<Filter>(triggerFilterJson, JsonOptions);

        if (f?.Sources is not { Length: > 0 } pinned) return true;
        return p.SourceLinks.Any(l => pinned.Contains((int)l.Source));
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        // One materialization = one resource.
        if (payload is not ResourceMaterializedPayload p) return [];

        return
        [
            new ResourceWorkflowItem
            {
                Id = p.ResourceId,
                Name = p.Name,
                Path = p.Path,
                HasLocalPath = true,
                SourceLinks = p.SourceLinks
            }
        ];
    }

    public string ResolveOutputItemType(string? triggerFilterJson) => WorkflowItemTypes.Resource;

    private record Filter
    {
        /// <summary><see cref="ResourceSource"/> values.</summary>
        public int[]? Sources { get; init; }
    }
}
