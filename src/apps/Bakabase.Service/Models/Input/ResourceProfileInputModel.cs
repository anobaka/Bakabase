using Bakabase.Abstractions.Models.Domain;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.Service.Models.Input;

public record ResourceProfileInputModel : IValidatableObject
{
    /// <summary>Null for this library; otherwise the stable identity of the resource owner.</summary>
    public string? SourceDeviceId { get; set; }

    /// <summary>Pins source-owned filter IDs to one incarnation of that library.</summary>
    public string? SourceLibraryEpoch { get; set; }

    /// <summary>Null inherits; browser, native or ask changes how this device opens matching resources.</summary>
    public string? OpeningMode { get; set; }

    public string Name { get; set; } = null!;

    /// <summary>
    /// Search criteria using serialized DbValue format
    /// </summary>
    public ResourceSearchInputModel? Search { get; set; }

    public string? NameTemplate { get; set; }

    public ResourceProfileEnhancerOptions? EnhancerOptions { get; set; }

    public ResourceProfilePlayableFileOptions? PlayableFileOptions { get; set; }

    public ResourceProfilePlayerOptions? PlayerOptions { get; set; }

    public ResourceProfilePropertyOptions? PropertyOptions { get; set; }

    public int Priority { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        string? error = null;
        try
        {
            ResourceProfileScope.Validate(SourceDeviceId, SourceLibraryEpoch, OpeningMode, NameTemplate,
                EnhancerOptions, PlayableFileOptions, PropertyOptions);
        }
        catch (ArgumentException e) { error = e.Message; }
        if (error != null) yield return new ValidationResult(error);
    }
}
