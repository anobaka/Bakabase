namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public List<PathFilter>? ResourceFilters { get; set; }
    public List<MediaLibraryTemplateProperty>? Properties { get; set; }
    public MediaLibraryTemplatePlayableFileLocator? PlayableFileLocator { get; set; }
    public List<MediaLibraryTemplateEnhancerOptions>? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public List<string>? SamplePaths { get; set; }
    public int? ChildTemplateId { get; set; }
    public MediaLibraryTemplate? Child { get; set; }
}