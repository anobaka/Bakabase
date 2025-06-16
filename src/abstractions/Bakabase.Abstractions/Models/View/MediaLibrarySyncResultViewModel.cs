namespace Bakabase.Abstractions.Models.View;

public record MediaLibrarySyncResultViewModel
{
    public int ElapsedMs { get; set; }
    public int Added { get; set; }
    public int Deleted { get; set; }
    public int Updated { get; set; }
}