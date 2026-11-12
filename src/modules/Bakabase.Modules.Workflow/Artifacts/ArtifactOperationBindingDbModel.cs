using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Workflow.Artifacts;

/// <summary>A durable execution slot cannot silently switch to another side-effect operation.</summary>
public sealed class ArtifactOperationBindingDbModel
{
    [Key] public string Id { get; set; } = "";
    public string OwnerKind { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string Slot { get; set; } = "";
    /// <summary>Opaque operation identity; the operation may belong to any registered backend.</summary>
    public string OperationId { get; set; } = "";
}
