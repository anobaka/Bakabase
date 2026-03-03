namespace Bakabase.Modules.Enhancer.Components.Enhancers.Steam;

public record SteamEnhancerContext
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string>? Developers { get; set; }
    public List<string>? Publishers { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public Dictionary<string, List<string>>? Genres { get; set; }
    public Dictionary<string, List<string>>? Categories { get; set; }
    public decimal? MetacriticScore { get; set; }
    public string? CoverPath { get; set; }
}
