namespace Bakabase.Modules.ThirdParty.ThirdParties.Tmdb.Models;

public record TmdbMovieDetail
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public int Runtime { get; set; }
    public decimal VoteAverage { get; set; }
    public int VoteCount { get; set; }
    public List<TmdbGenre>? Genres { get; set; }
    public List<string>? ProductionCountries { get; set; }
    public List<string>? SpokenLanguages { get; set; }
    public string? Status { get; set; }
    public string? Tagline { get; set; }
    public long Budget { get; set; }
    public long Revenue { get; set; }
}

public record TmdbGenre
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public record TmdbSearchResult
{
    public int Page { get; set; }
    public List<TmdbMovieSearchItem>? Results { get; set; }
    public int TotalPages { get; set; }
    public int TotalResults { get; set; }
}

public record TmdbMovieSearchItem
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public decimal VoteAverage { get; set; }
    public int VoteCount { get; set; }
    public List<int>? GenreIds { get; set; }
    public string? OriginalLanguage { get; set; }
    public decimal Popularity { get; set; }
    public bool Adult { get; set; }
    public bool Video { get; set; }
}