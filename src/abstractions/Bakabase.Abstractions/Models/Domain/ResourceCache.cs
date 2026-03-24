using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceCache
{
    public List<string>? CoverPaths { get; set; }
    public bool HasMorePlayableFiles { get; set; }
    /// <summary>
    /// Kept for backward compatibility. Populated from PlayableItems where Source == FileSystem.
    /// </summary>
    public List<string>? PlayableFilePaths { get; set; }
    public List<PlayableItem>? PlayableItems { get; set; }
    public List<ResourceCacheType> CachedTypes { get; set; } = [];
}
