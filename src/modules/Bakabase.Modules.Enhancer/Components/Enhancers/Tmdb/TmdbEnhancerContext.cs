using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Tmdb;

public record TmdbEnhancerContext
{
    public string? CoverPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public decimal? Rating { get; set; }
    public decimal? VoteCount { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public decimal? Runtime { get; set; }
    public List<TagValue>? Genres { get; set; }
    public List<string>? ProductionCountries { get; set; }
    public List<string>? SpokenLanguages { get; set; }
    public string? Status { get; set; }
    public string? Tagline { get; set; }
    public decimal? Budget { get; set; }
    public decimal? Revenue { get; set; }
}