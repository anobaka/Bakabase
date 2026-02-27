using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db;

public record SteamAppDbModel
{
    public int Id { get; set; }
    public int AppId { get; set; }
    public string? Name { get; set; }
    public int PlaytimeForever { get; set; }
    public int RtimeLastPlayed { get; set; }
    public string? ImgIconUrl { get; set; }
    public bool HasCommunityVisibleStats { get; set; }
    public string? MetadataJson { get; set; }
    public DateTime? MetadataFetchedAt { get; set; }
    public bool IsInstalled { get; set; }
    public string? InstallPath { get; set; }
    public int? ResourceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
