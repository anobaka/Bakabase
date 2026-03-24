using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Models.Db;

public record ResourceCacheDbModel
{
    [Key] public int ResourceId { get; set; }
    public string? CoverPaths { get; set; }
    /// <summary>
    /// Legacy field for backward compatibility. Use <see cref="PlayableItems"/> instead.
    /// </summary>
    [Obsolete("Use PlayableItems instead. Will be removed in v2.4.")]
    public string? PlayableFilePaths { get; set; }
    /// <summary>
    /// JSON-serialized List&lt;PlayableItem&gt; supporting multi-source playable items.
    /// </summary>
    public string? PlayableItems { get; set; }
    /// <summary>
    /// DB column remains HasMorePlayableFiles for backward compatibility.
    /// </summary>
    [Column("HasMorePlayableFiles")]
    public bool HasMoreFileSystemPlayableItems { get; set; }
    public ResourceCacheType CachedTypes { get; set; }
}
