using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Input;

public class ThirdPartyContentTrackerDailyHistoryInputModel : IValidatableObject
{
    [Required]
    [MaxLength(100)]
    public string DomainKey { get; set; } = null!;

    [MaxLength(500)]
    public string? Filter { get; set; }

    [Required]
    [MaxLength(10)]
    public string CenterDay { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string TimeZoneId { get; set; } = null!;

    [Required]
    [MaxLength(1000)]
    public List<string> ExcludeContentIds { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ExcludeContentIds?.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 200) == true)
            yield return new ValidationResult("Excluded content IDs must contain 1 to 200 characters.",
                [nameof(ExcludeContentIds)]);
    }
}
