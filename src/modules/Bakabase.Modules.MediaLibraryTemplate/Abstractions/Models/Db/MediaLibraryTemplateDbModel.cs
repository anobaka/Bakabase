namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;

public record MediaLibraryTemplateDbModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? ResourceFilters { get; set; }
    public string? Properties { get; set; }
    public string? PlayableFileLocator { get; set; }
    public string? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public string? SamplePaths { get; set; }
    public int? ChildTemplateId { get; set; }
}