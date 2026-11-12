using System.Text.Json.Serialization;

namespace Bakabase.Modules.Workflow.Abstractions.Models.Domain;

/// <summary>Provenance, never an authorization token or a content deduplication key.</summary>
public sealed record WorkflowSourceRef(string Module, string EntityKind, string EntityId,
    string? Revision = null, string? ResultKey = null);

public interface IWorkflowSourcePayload
{
    [JsonIgnore] IReadOnlyList<WorkflowSourceRef> SourceRefs { get; }
}
