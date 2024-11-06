using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceCache
{
    public List<string>? CoverPaths { get; set; }
    public bool HasMorePlayableFiles { get; set; }
    public List<string>? PlayableFilePaths { get; set; }
    public List<ResourceCacheType> CachedTypes { get; set; } = [];
}