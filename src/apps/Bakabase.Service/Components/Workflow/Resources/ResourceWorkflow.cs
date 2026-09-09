using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Workflow;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Resources;

/// <summary>
/// A resource flowing through a workflow chain. Deliberately a snapshot rather than a live
/// handle: a chain may run long after the event, and an activity acting on stale-but-explicit
/// data is easier to reason about than one silently seeing a resource change under it.
/// </summary>
public sealed record ResourceWorkflowItem : IHasWorkflowSystemVariables, IHasResourceId
{
    public required int Id { get; init; }

    /// <summary>Which resource this is about (capability map E3) — the same value as <see cref="Id"/>,
    /// named the way an activity that only needs a resource asks for it.</summary>
    public int ResourceId => Id;

    public string? Name { get; init; }

    /// <summary>Where the files are. Null while the resource has none.</summary>
    public string? Path { get; init; }

    public bool HasLocalPath { get; init; }

    public IReadOnlyList<ResourceWorkflowSourceLink> SourceLinks { get; init; } = [];

    public IReadOnlyDictionary<string, string> GetWorkflowSystemVariables() => new Dictionary<string, string>
    {
        ["id"] = Id.ToString(),
        ["name"] = Name ?? "",
        ["path"] = Path ?? "",
        ["directoryName"] = string.IsNullOrEmpty(Path)
            ? ""
            : System.IO.Path.GetFileName(Path.TrimEnd('/', '\\')) ?? "",
        ["hasLocalPath"] = HasLocalPath ? "true" : "false",
        ["sources"] = string.Join(", ", SourceLinks.Select(l => l.Source.ToString())),
    };
}

public class ResourceItemTypeDescriptor : IWorkflowItemTypeDescriptor
{
    public string ItemType => WorkflowItemTypes.Resource;
    public string DisplayName => "Resource";
    public System.Type ClrType => typeof(ResourceWorkflowItem);
}
