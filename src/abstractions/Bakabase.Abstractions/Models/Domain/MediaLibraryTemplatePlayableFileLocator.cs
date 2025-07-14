namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplatePlayableFileLocator
{
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public List<ExtensionGroup>? ExtensionGroups { get; set; }
    public HashSet<string>? Extensions { get; set; }
    public int? MaxFileCount { get; set; }
}