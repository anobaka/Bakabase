using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Shared;

public record SharedMediaLibraryTemplate
{
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }

    public List<SharedPathFilter> ResourceFilters { get; set; } = [];
    public List<SharedMediaLibraryTemplateProperty>? Properties { get; set; }
    public SharedMediaLibraryTemplatePlayableFileLocator? PlayableFileLocator { get; set; }
    public List<SharedMediaLibraryTemplateEnhancerOptions>? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public List<string>? SamplePaths { get; set; }
    public SharedMediaLibraryTemplate? Child { get; set; }

    public string ToSharedText() => JsonConvert.SerializeObject(this);
    public static SharedMediaLibraryTemplate FromSharedText(string sharedText)
    {
        return JsonConvert.DeserializeObject<SharedMediaLibraryTemplate>(sharedText)!;
    }
}