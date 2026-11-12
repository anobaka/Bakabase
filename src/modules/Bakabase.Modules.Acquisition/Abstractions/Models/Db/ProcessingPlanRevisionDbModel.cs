using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Db;

/// <summary>Append-only, owner-reviewed processing instructions for one exact source result.</summary>
public sealed class ProcessingPlanRevisionDbModel
{
    [Key] public string Id { get; set; } = "";
    public string SourceKey { get; set; } = "";
    public string SourceJson { get; set; } = "";
    public int Revision { get; set; }
    public string SourceHash { get; set; } = "";
    public string? OriginalPlanJson { get; set; }
    public string PlanJson { get; set; } = "";
    public string EvidenceJson { get; set; } = "[]";
    public string Reason { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
