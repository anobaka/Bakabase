namespace Bakabase.Abstractions.Models.Db;

/// <summary>
/// Records resources created by a Resource mark.
/// Used to track ownership and enable proper cleanup when marks are updated or deleted.
/// </summary>
public record ResourceMarkEffectDbModel
{
    public int Id { get; set; }

    /// <summary>
    /// The mark that created this resource (PathMark.Id)
    /// </summary>
    public int MarkId { get; set; }

    /// <summary>
    /// The path of the created resource (standardized)
    /// </summary>
    public string Path { get; set; } = null!;

    /// <summary>
    /// Timestamp when this effect was recorded
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
