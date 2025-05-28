namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateProperty
{
    public Bakabase.Abstractions.Models.Domain.Property Property { get; set; } = null!;
    public List<PathLocator>? ValueLocators { get; set; }
}