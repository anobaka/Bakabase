using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db;

public record ExHentaiGalleryDbModel
{
    public int Id { get; set; }
    public long GalleryId { get; set; }
    [Required] public string GalleryToken { get; set; } = null!;
    public string? Title { get; set; }
    public string? TitleJpn { get; set; }
    public string? Category { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime? MetadataFetchedAt { get; set; }
    public bool IsDownloaded { get; set; }
    public string? LocalPath { get; set; }
    public int? ResourceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
