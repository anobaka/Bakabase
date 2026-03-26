using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>
/// Stores filesystem-level discovery results for a resource.
/// Does NOT include data from external sources (Steam, DLsite, etc.) -
/// those are resolved at query time via SourceLinks and ResourceResolvers.
/// </summary>
public record ResourceFileSystemCache
{
    public List<string>? CoverPaths { get; set; }
    public List<string>? PlayableFilePaths { get; set; }
    public bool HasMorePlayableFiles { get; set; }
    public List<ResourceCacheType> CachedTypes { get; set; } = [];
}
