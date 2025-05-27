using Bakabase.Abstractions.Models.Domain;
using Newtonsoft.Json;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;

public record SharableMediaLibraryTemplate
{
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }

    public List<PathFilter> ResourceFilters { get; set; } = [];
    public List<MediaLibraryTemplateProperty>? Properties { get; set; }
    public MediaLibraryTemplatePlayableFileLocator? PlayableFileLocator { get; set; }
    public List<MediaLibraryTemplateEnhancerOptions>? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public List<string>? SamplePaths { get; set; }
    public SharableMediaLibraryTemplate? Child { get; set; }

    public string ToSharedText() => JsonConvert.SerializeObject(this);
    public static SharableMediaLibraryTemplate FromSharedText(string sharedText)
    {
        return JsonConvert.DeserializeObject<SharableMediaLibraryTemplate>(sharedText)!;
    }
}