using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplateProperty
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
    public Bakabase.Abstractions.Models.Domain.Property? Property { get; set; }
    public List<PathLocator>? ValueLocators { get; set; }
}