using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Db;

/// <summary>A durable producer-to-acquisition handoff. The source identity is its idempotency key.</summary>
public sealed class AcquisitionHandoffDbModel
{
    [Key] public string Id { get; set; } = "";
    public string SourceModule { get; set; } = "";
    public string SourceKind { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string SourceRevision { get; set; } = "";
    public int ProducerRunId { get; set; }
    public string Status { get; set; } = "queued";
    public string? Code { get; set; }
    public int? AcquisitionTaskId { get; set; }
    public string? OutputDirectory { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
