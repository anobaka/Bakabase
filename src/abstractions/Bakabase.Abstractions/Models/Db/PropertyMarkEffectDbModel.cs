namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// Records property values set by a Property mark or MediaLibrary mark.
/// Used to track ownership and enable proper cleanup when marks are updated or deleted.
/// For MediaLibrary marks: PropertyPool = Internal (1), PropertyId = 25 (MediaLibraryV2Multi)
/// </summary>
public record PropertyMarkEffectDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// The mark that set this property value (PathMark.Id)
    /// </summary>
    public int MarkId { get; set; }

    /// <summary>
    /// Property pool (Internal=1, Reserved=2, Custom=4)
    /// For MediaLibrary marks: Internal (1)
    /// </summary>
    public int PropertyPool { get; set; }

    /// <summary>
    /// Property ID within the pool.
    /// For MediaLibrary marks: 25 (MediaLibraryV2Multi)
    /// </summary>
    public int PropertyId { get; set; }

    /// <summary>
    /// The resource this value was set on
    /// </summary>
    public int ResourceId { get; set; }

    /// <summary>
    /// The value that was set (serialized).
    /// For multi-select: comma-separated values.
    /// For MediaLibrary: media library ID as string.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The priority of the mark at the time of sync.
    /// Used for resolving conflicts in single-select properties (higher priority wins).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Timestamp when this effect was recorded
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp when this effect was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
