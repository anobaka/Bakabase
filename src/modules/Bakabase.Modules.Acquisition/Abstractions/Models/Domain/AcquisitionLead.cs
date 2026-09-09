using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Abstractions.Models.Domain;

/// <summary>
/// One place a resource can be obtained from.
/// </summary>
public record AcquisitionLead
{
    /// <summary>Zero for a lead derived from a source link — those have no row of their own.</summary>
    public int Id { get; set; }

    public int ResourceId { get; set; }

    public AcquisitionLeadKind Kind { get; set; }

    public string Value { get; set; } = null!;

    public AcquisitionLeadOrigin Origin { get; set; }

    public string? Note { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public AcquisitionLeadResult? LastResult { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// True for a lead derived from the resource's identity on a platform that holds it. Such a lead
    /// is not stored and cannot be edited or deleted on its own — it follows the source link.
    /// </summary>
    public bool IsDerived { get; set; }

    /// <summary>
    /// For a derived lead, the platform it came from, so the UI can name it.
    /// </summary>
    public string? SourceName { get; set; }
}
