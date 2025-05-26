using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathFilter;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Components.PathLocator;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record MediaLibraryTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }

    public List<PathFilter>? ResourceFilters { get; set; }
    public List<MediaLibraryTemplateProperty>? Properties { get; set; }
    public MediaLibraryTemplatePlayableFileLocator? PlayableFileLocator { get; set; }
    public List<MediaLibraryTemplateEnhancerOptions>? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public List<string>? SamplePaths { get; set; }
    public int? ChildrenTemplateId { get; set; }
    public MediaLibraryTemplate? Children { get; set; }
}