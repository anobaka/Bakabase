using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Records property values set by a Property mark or MediaLibrary mark.
/// </summary>
public class PropertyMarkEffect
{
    public int Id { get; set; }
    public int MarkId { get; set; }
    public PropertyPool PropertyPool { get; set; }
    public int PropertyId { get; set; }
    public int ResourceId { get; set; }
    public string? Value { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
