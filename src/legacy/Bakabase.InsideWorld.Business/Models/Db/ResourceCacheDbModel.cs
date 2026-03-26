using System.ComponentModel.DataAnnotations;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Models.Db;

/// <summary>
/// Stores filesystem-level discovery cache for a resource.
/// DB table name remains "ResourceCaches" for backward compatibility.
/// </summary>
public record ResourceCacheDbModel
{
    [Key] public int ResourceId { get; set; }
    public string? CoverPaths { get; set; }
    public string? PlayableFilePaths { get; set; }
    public bool HasMorePlayableFiles { get; set; }
    public ResourceCacheType CachedTypes { get; set; }
}
