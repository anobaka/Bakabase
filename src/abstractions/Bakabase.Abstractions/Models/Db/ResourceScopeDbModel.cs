namespace Bakabase.Abstractions.Models.Db;

/// <summary>A reusable named resource condition. Integer filter IDs belong to its pinned source library.</summary>
public record ResourceScopeDbModel
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? SourceDeviceId { get; set; }
    public string? SourceLibraryEpoch { get; set; }
    public string? SearchJson { get; set; }
    public int Revision { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
