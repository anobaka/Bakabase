using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input;

/// <summary>Content-time limits of a page, excluding every content item on that page.</summary>
public class ThirdPartyContentTrackerPageBoundariesInputModel : IValidatableObject
{
    [Required]
    [MaxLength(100)]
    public string DomainKey { get; set; } = null!;

    [MaxLength(500)]
    public string? Filter { get; set; }

    [Required]
    public DateTime? OldestUpdatedAt { get; set; }

    [Required]
    public DateTime? NewestUpdatedAt { get; set; }

    [Required]
    [MaxLength(1000)]
    public List<string> ExcludeContentIds { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OldestUpdatedAt.HasValue && OldestUpdatedAt.Value.Kind != DateTimeKind.Utc)
            yield return new ValidationResult("The oldest content time must use UTC (Z).", [nameof(OldestUpdatedAt)]);
        if (NewestUpdatedAt.HasValue && NewestUpdatedAt.Value.Kind != DateTimeKind.Utc)
            yield return new ValidationResult("The newest content time must use UTC (Z).", [nameof(NewestUpdatedAt)]);
        if (OldestUpdatedAt.HasValue && NewestUpdatedAt.HasValue && OldestUpdatedAt > NewestUpdatedAt)
            yield return new ValidationResult("The oldest content time must not follow the newest content time.",
                [nameof(OldestUpdatedAt), nameof(NewestUpdatedAt)]);
        if (ExcludeContentIds?.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 200) == true)
            yield return new ValidationResult("Excluded content IDs must contain 1 to 200 characters.",
                [nameof(ExcludeContentIds)]);
    }
}
