using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record ResourceCache
{
    public List<string>? CoverPaths { get; set; }
    public bool HasMoreFileSystemPlayableItems { get; set; }

    /// <summary>
    /// Kept for backward compatibility. Populated from PlayableItems where Source == FileSystem.
    /// </summary>
    [Obsolete("Use PlayableItems instead. Will be removed in v2.4.")]
    public List<string>? PlayableFilePaths
    {
        get;
        [Obsolete("Use PlayableItems instead. Will be removed in v2.4.", true)]
        set;
    }

    public List<PlayableItem>? PlayableItems { get; set; }
    public List<ResourceCacheType> CachedTypes { get; set; } = [];
}