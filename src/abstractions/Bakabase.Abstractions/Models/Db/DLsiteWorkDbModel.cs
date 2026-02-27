using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db;

public record DLsiteWorkDbModel
{
    public int Id { get; set; }
    [Required] public string WorkId { get; set; } = null!;
    public string? Title { get; set; }
    public string? Circle { get; set; }
    public string? WorkType { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime? MetadataFetchedAt { get; set; }
    public string? DrmKey { get; set; }
    public bool IsPurchased { get; set; }
    public bool IsDownloaded { get; set; }
    public string? LocalPath { get; set; }
    public int? ResourceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
