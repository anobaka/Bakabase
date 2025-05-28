using Newtonsoft.Json;

namespace Bakabase.Abstractions.Models.Domain.Sharable;

public record SharableMediaLibraryTemplate
{
    public string Name { get; set; } = null!;
    public string? Author { get; set; }
    public string? Description { get; set; }

    public List<SharablePathFilter> ResourceFilters { get; set; } = [];
    public List<SharableMediaLibraryTemplateProperty>? Properties { get; set; }
    public SharableMediaLibraryTemplatePlayableFileLocator? PlayableFileLocator { get; set; }
    public List<SharableMediaLibraryTemplateEnhancerOptions>? Enhancers { get; set; }
    public string? DisplayNameTemplate { get; set; }
    public List<string>? SamplePaths { get; set; }
    public SharableMediaLibraryTemplate? Child { get; set; }

    public string ToShareCode() => JsonConvert.SerializeObject(this);
    public static SharableMediaLibraryTemplate FromShareCode(string shareCode)
    {
        return JsonConvert.DeserializeObject<SharableMediaLibraryTemplate>(shareCode)!;
    }
}