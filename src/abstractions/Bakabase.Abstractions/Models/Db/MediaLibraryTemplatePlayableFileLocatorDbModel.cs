namespace Bakabase.Abstractions.Models.Db;

public record MediaLibraryTemplatePlayableFileLocatorDbModel
{
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public HashSet<string>? Extensions { get; set; }
    public int? MaxFileCount { get; set; }
}