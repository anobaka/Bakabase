namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplatePlayableFileLocator
{
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
}