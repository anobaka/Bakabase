using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.InsideWorld.Business.Workflow;

/// <summary>
/// Workflow kind constants for the resource domain. They live here, next to the service that
/// publishes them, for the same reason the downloader's do: the publisher must be able to name
/// its own event.
/// </summary>
public static class ResourceWorkflowKinds
{
    public const string Module = "resource";

    /// <summary>
    /// A resource that had no local files now has them — downloaded, installed, or pointed at a
    /// folder the user already had.
    /// </summary>
    public static readonly string TriggerMaterialized = WorkflowTriggerKinds.Build(Module, "materialized");
}

/// <summary>One of a resource's identities on an external platform.</summary>
public record ResourceWorkflowSourceLink(ResourceSource Source, string SourceKey);

/// <summary>
/// Event payload published on <see cref="ResourceWorkflowKinds.TriggerMaterialized"/>. Carries
/// what the chain needs to act without re-reading the resource, and what the trigger filter needs
/// to decide whether this materialization is the one a workflow was waiting for.
/// </summary>
public record ResourceMaterializedPayload
{
    public int ResourceId { get; init; }

    /// <summary>Where the files now are.</summary>
    public string Path { get; init; } = "";

    public string? Name { get; init; }

    public IReadOnlyList<ResourceWorkflowSourceLink> SourceLinks { get; init; } = [];
}
