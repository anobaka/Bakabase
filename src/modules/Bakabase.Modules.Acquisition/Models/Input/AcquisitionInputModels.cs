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
