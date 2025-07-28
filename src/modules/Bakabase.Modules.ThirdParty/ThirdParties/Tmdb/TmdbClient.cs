using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Bakabase.Modules.ThirdParty.ThirdParties.Tmdb.Models;
using System.Globalization;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;

public class TmdbClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, IBOptions<ITmdbOptions> options)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Tmdb;
    private readonly IBOptions<ITmdbOptions> _options = options;

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private string BuildUrlWithApiKey(string baseUrl)
    {
        var apiKey = _options.Value.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("TMDB API key is not configured");
        }

        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}api_key={apiKey}";
    }

    public async Task<TmdbMovieDetail?> GetMovieDetail(int movieId)
    {
        var url = BuildUrlWithApiKey(TmdbUrlBuilder.MovieDetail(movieId));
        var response = await HttpClient.GetStringAsync(url);
        
        var detail = JsonSerializer.Deserialize<TmdbMovieDetailResponse>(response, _jsonOptions);
        if (detail == null) return null;

        return new TmdbMovieDetail
        {
            Id = detail.Id,
            Title = detail.Title,
            OriginalTitle = detail.OriginalTitle,
            Overview = detail.Overview,
            PosterPath = detail.PosterPath,
            BackdropPath = detail.BackdropPath,
            ReleaseDate = DateTime.TryParse(detail.ReleaseDate, out var releaseDate) ? releaseDate : null,
            Runtime = detail.Runtime,
            VoteAverage = detail.VoteAverage,
            VoteCount = detail.VoteCount,
            Genres = detail.Genres?.Select(g => new TmdbGenre { Id = g.Id, Name = g.Name }).ToList(),
            ProductionCountries = detail.ProductionCountries?.Select(pc => pc.Name).Where(n => !string.IsNullOrEmpty(n)).ToList(),
            SpokenLanguages = detail.SpokenLanguages?.Select(sl => sl.EnglishName).Where(n => !string.IsNullOrEmpty(n)).ToList(),
            Status = detail.Status,
            Tagline = detail.Tagline,
            Budget = detail.Budget,
            Revenue = detail.Revenue
        };
    }

    public async Task<TmdbMovieDetail?> SearchAndGetFirst(string query)
    {
        var url = BuildUrlWithApiKey(TmdbUrlBuilder.SearchMovie(query));
        var response = await HttpClient.GetStringAsync(url);
        
        var searchResult = JsonSerializer.Deserialize<TmdbSearchResultResponse>(response, _jsonOptions);
        var firstResult = searchResult?.Results?.FirstOrDefault();
        
        if (firstResult == null) return null;

        return await GetMovieDetail(firstResult.Id);
    }

    private record TmdbMovieDetailResponse
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? OriginalTitle { get; set; }
        public string? Overview { get; set; }
        public string? PosterPath { get; set; }
        public string? BackdropPath { get; set; }
        public string? ReleaseDate { get; set; }
        public int Runtime { get; set; }
        public decimal VoteAverage { get; set; }
        public int VoteCount { get; set; }
        public List<TmdbGenreResponse>? Genres { get; set; }
        public List<TmdbProductionCountryResponse>? ProductionCountries { get; set; }
        public List<TmdbSpokenLanguageResponse>? SpokenLanguages { get; set; }
        public string? Status { get; set; }
        public string? Tagline { get; set; }
        public long Budget { get; set; }
        public long Revenue { get; set; }
    }

    private record TmdbGenreResponse
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private record TmdbProductionCountryResponse
    {
        public string? Iso31661 { get; set; }
        public string? Name { get; set; }
    }

    private record TmdbSpokenLanguageResponse
    {
        public string? Iso6391 { get; set; }
        public string? EnglishName { get; set; }
        public string? Name { get; set; }
    }

    private record TmdbSearchResultResponse
    {
        public int Page { get; set; }
        public List<TmdbMovieSearchItemResponse>? Results { get; set; }
        public int TotalPages { get; set; }
        public int TotalResults { get; set; }
    }

    private record TmdbMovieSearchItemResponse
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? OriginalTitle { get; set; }
        public string? Overview { get; set; }
        public string? PosterPath { get; set; }
        public string? BackdropPath { get; set; }
        public string? ReleaseDate { get; set; }
        public decimal VoteAverage { get; set; }
        public int VoteCount { get; set; }
        public List<int>? GenreIds { get; set; }
        public string? OriginalLanguage { get; set; }
        public decimal Popularity { get; set; }
        public bool Adult { get; set; }
        public bool Video { get; set; }
    }
}