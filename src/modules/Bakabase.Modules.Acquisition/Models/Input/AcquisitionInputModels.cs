using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Acquisition.Models.Input;

/// <summary>"Get me this." Everything else follows from the lead.</summary>
public record AcquisitionCreationInputModel
{
    [Required] public int ResourceId { get; set; }

    /// <summary>
    /// A stored lead to use. When set, its kind and value are taken from the lead itself, and
    /// <see cref="LeadKind"/> / <see cref="LeadValue"/> are ignored.
    /// </summary>
    public int? AcquisitionLeadId { get; set; }

    public AcquisitionLeadKind? LeadKind { get; set; }

    public string? LeadValue { get; set; }

    /// <summary>Which recipe to run. Absent means the one the lead's kind implies.</summary>
    public int? RecipeDefinitionId { get; set; }

    /// <summary>Context only — which collection the user started this from.</summary>
    public int? CollectionId { get; set; }
}

public record AcquisitionResumeInputModel
{
    /// <summary>
    /// The answer to whatever the current step is waiting for, in the shape that step asked for.
    /// </summary>
    [Required]
    public string SignalJson { get; set; } = "{}";
}

/// <summary>
/// "Get me whatever is behind this link." The one-field version: everything else — which resource
/// it is, whether one already exists, which recipe fits — is worked out from the link.
/// </summary>
public record AcquisitionFromUrlInputModel
{
    [Required] public string Url { get; set; } = null!;

    /// <summary>Which recipe to run. Absent means the one the link's kind implies.</summary>
    public int? RecipeDefinitionId { get; set; }

    public int? CollectionId { get; set; }
}
