using System.Globalization;
using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Steam;

/// <summary>
/// Client for Steam Web API and store API.
/// Handles rate limiting (~200 requests per 5 minutes for store API).
/// </summary>
public class SteamClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamClient> _logger;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);
    private DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(1500); // ~200 req/5min

    public SteamClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClient = httpClientFactory.CreateClient(InternalOptions.HttpClientNames.Default);
        _logger = loggerFactory.CreateLogger<SteamClient>();
    }

    /// <summary>
    /// Gets all games owned by a Steam user.
    /// </summary>
    public async Task<List<SteamOwnedGame>> GetOwnedGames(string apiKey, string steamId, CancellationToken ct)
    {
        var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/" +
                  $"?key={apiKey}&steamid={steamId}&include_appinfo=true&include_played_free_games=true&format=json";

        var json = await GetWithRateLimit(url, ct);
        var response = JsonSerializer.Deserialize<SteamOwnedGamesResponse>(json, JsonSerializerOptions.Web);

        return response?.Response?.Games ?? [];
    }

    /// <summary>
    /// Gets detailed app information from Steam Store API.
    /// </summary>
    public async Task<SteamAppDetails?> GetAppDetails(int appId, string language = "schinese", CancellationToken ct = default)
    {
        var url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l={language}";

        try
        {
            var json = await GetWithRateLimit(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty(appId.ToString(), out var appElement))
            {
                var responseJson = appElement.GetRawText();
                var response = JsonSerializer.Deserialize<SteamAppDetailsResponse>(responseJson, JsonSerializerOptions.Web);
                return response is { Success: true } ? response.Data : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get app details for AppId {AppId}", appId);
        }

        return null;
    }

    /// <summary>
    /// Downloads image data from a URL.
    /// </summary>
    public async Task<byte[]?> DownloadImage(string url, CancellationToken ct)
    {
        try
        {
            return await _httpClient.GetByteArrayAsync(url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Parses a Steam release date string into a DateTime.
    /// Steam uses various date formats like "Nov 10, 2020", "October 2020", "2020", etc.
    /// </summary>
    public static DateTime? ParseReleaseDate(string? dateStr)
    {
        if (string.IsNullOrEmpty(dateStr)) return null;

        // Try common formats
        string[] formats =
        [
            "MMM d, yyyy",    // "Nov 10, 2020"
            "MMMM d, yyyy",   // "November 10, 2020"
            "d MMM, yyyy",    // "10 Nov, 2020"
            "MMM yyyy",       // "Nov 2020"
            "MMMM yyyy",      // "November 2020"
            "yyyy",           // "2020"
            "yyyy年M月d日",    // Chinese format
            "yyyy年M月",       // Chinese format without day
        ];

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateStr, format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var result))
            {
                return result;
            }
        }

        // Try generic parse
        if (DateTime.TryParse(dateStr, out var generic))
        {
            return generic;
        }

        return null;
    }

    private async Task<string> GetWithRateLimit(string url, CancellationToken ct)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastRequestTime;
            if (elapsed < MinRequestInterval)
            {
                await Task.Delay(MinRequestInterval - elapsed, ct);
            }

            _lastRequestTime = DateTime.UtcNow;
            return await _httpClient.GetStringAsync(url, ct);
        }
        finally
        {
            _rateLimiter.Release();
        }
    }
}
