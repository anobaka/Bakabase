using Bakabase.Modules.Workflow.Abstractions.Models.Domain;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>Private, server-prepared context. It is persisted with the task, not stored in recipe definitions.</summary>
public sealed record AcquisitionPreparedContext
{
    public IReadOnlyDictionary<string, string> Variables { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<WorkflowSourceRef> SourceRefs { get; init; } = [];
}
