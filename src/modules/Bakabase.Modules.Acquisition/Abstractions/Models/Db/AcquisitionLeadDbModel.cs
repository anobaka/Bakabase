using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Db;

/// <summary>
/// One place a resource can be obtained from. Only sharing-channel leads live here — a platform the
/// user holds the resource on is already a <c>ResourceSourceLink</c> and is derived into a lead on
/// read.
/// </summary>
public record AcquisitionLeadDbModel
{
    [Key] public int Id { get; set; }

    public int ResourceId { get; set; }

    public AcquisitionLeadKind Kind { get; set; }

    /// <summary>
    /// The link, or a document path plus the row that shares it. Unique across all resources: the
    /// same shared link cannot describe two different resources, and that is what deduplicates a
    /// list imported twice.
    /// </summary>
    [MaxLength(2048)]
    public string Value { get; set; } = null!;

    public AcquisitionLeadOrigin Origin { get; set; }

    [MaxLength(512)] public string? Note { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public AcquisitionLeadResult? LastResult { get; set; }

    public DateTime CreatedAt { get; set; }
}
