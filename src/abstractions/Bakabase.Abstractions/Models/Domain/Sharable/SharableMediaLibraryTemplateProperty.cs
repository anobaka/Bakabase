namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplateProperty
{
    public Property Property { get; set; } = null!;
    public List<PathPropertyLocator>? ValueLocators { get; set; }
}