using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public partial class ExHentaiClient
{
    public const string ApiUrl = "https://api.e-hentai.org/api.php";
    private const int ApiBatchSize = 25;
    private readonly SemaphoreSlim _apiLock = new(1, 1);
    private int _apiRequestsSincePause;
    private static readonly JsonSerializerOptions ApiJsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>Fetches metadata without loading a gallery or any thumbnail/image pages.</summary>
    public async Task<ExHentaiResource> GetGalleryMetadata(string url, CancellationToken ct = default)
    {
        var galleries = await GetGalleryMetadata(new[] { url }, ct);
        return galleries[0];
    }

    /// <summary>Requests at most 25 unique gallery keys per POST and preserves input order.</summary>
    public async Task<List<ExHentaiResource>> GetGalleryMetadata(IReadOnlyList<string> urls,
        CancellationToken ct = default)
    {
        var galleries = await FetchGalleryMetadata(urls, ct);
        return galleries.Select(g => MapGalleryMetadata(g.Key, g.Data)).ToList();
    }

    private async Task<List<(GalleryKey Key, ApiGalleryData Data)>> FetchGalleryMetadata(
        IReadOnlyList<string> urls, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var keys = urls.Select(url => TryParseGalleryKey(url, null, out var key)
            ? key
            : throw new ArgumentException($"Invalid E-Hentai gallery URL: {url}", nameof(urls))).ToArray();
        var metadata = new Dictionary<(int Id, string Token), ApiGalleryData>();
        var uniqueKeys = keys.DistinctBy(k => (k.Id, k.Token));

        foreach (var batch in uniqueKeys.Chunk(ApiBatchSize))
        {
            var response = await RequestGalleryMetadata(batch, ct);
            if (!string.IsNullOrWhiteSpace(response.Error))
                throw new InvalidOperationException($"E-Hentai API: {response.Error}");

            foreach (var key in batch)
            {
                var entry = response.Galleries?.FirstOrDefault(g => g.Id == key.Id &&
                    (!string.IsNullOrWhiteSpace(g.Error) ||
                     string.Equals(g.Token, key.Token, StringComparison.OrdinalIgnoreCase)));
                if (entry == null)
                    throw new InvalidDataException($"E-Hentai API omitted gallery {key.Id}/{key.Token}.");
                if (!string.IsNullOrWhiteSpace(entry.Error))
                    throw new InvalidOperationException($"E-Hentai API gallery {key.Id}: {entry.Error}");

                metadata.Add((key.Id, key.Token), entry);
            }
        }

        return keys.Select(k => (k, metadata[(k.Id, k.Token)])).ToList();
    }

    private async Task<ApiGalleryResponse> RequestGalleryMetadata(GalleryKey[] keys, CancellationToken ct)
    {
        await _apiLock.WaitAsync(ct);
        try
        {
            var request = new
            {
                method = "gdata",
                gidlist = keys.Select(k => new object[] { k.Id, k.Token }).ToArray(),
                @namespace = 1
            };
            // The API rejects HTTP/1.1 chunked bodies with "Empty JSON Request".
            // Buffer the JSON so each attempt sends an exact Content-Length.
            var requestJson = JsonSerializer.Serialize(request);
            for (var attempt = 1; ; attempt++)
            {
                // Keep this budget shared across all callers and count retries as requests too.
                if (_apiRequestsSincePause >= 4)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                    _apiRequestsSincePause = 0;
                }

                ct.ThrowIfCancellationRequested();
                _apiRequestsSincePause++;
                try
                {
                    using var message = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
                    {
                        Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
                    };
                    // The public endpoint needs no login. Its first request must not seed the
                    // ExHentai page cookie container with the unrelated e-hentai.org domain.
                    message.Options.Set(ThirdPartyRequestOptions.AccountKey, "metadata-api");
                    message.Options.Set(ThirdPartyRequestOptions.Cookie, "");
                    message.Options.Set(ThirdPartyRequestOptions.SkipConfiguredHeaders, true);
                    message.Options.Set(ThirdPartyRequestOptions.SuppressSensitiveHeaders, true);
                    using var response = await HttpClient.SendAsync(message, ct);
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync(ct);
                    ThrowIfBanned(json);
                    return JsonSerializer.Deserialize<ApiGalleryResponse>(json, ApiJsonOptions)
                           ?? throw new InvalidDataException("E-Hentai API returned an empty response.");
                }
                catch (TaskCanceledException e) when (!ct.IsCancellationRequested &&
                    e.InnerException is TimeoutException && attempt < 3)
                {
                    Logger.LogWarning("E-Hentai API timed out; retrying ({Attempt}/3)", attempt);
                }
                catch (HttpRequestException e) when (e.InnerException is IOException io &&
                    io.Message.Contains("EOF", StringComparison.Ordinal) && attempt < 3)
                {
                    Logger.LogWarning("E-Hentai API connection closed; retrying ({Attempt}/3)", attempt);
                }
                catch (JsonException e)
                {
                    throw new InvalidDataException("E-Hentai API returned invalid gallery metadata JSON.", e);
                }
            }
        }
        finally
        {
            _apiLock.Release();
        }
    }

    private static ExHentaiResource MapGalleryMetadata(GalleryKey key, ApiGalleryData data)
    {
        var name = WebUtility.HtmlDecode(data.Title) ?? "";
        var rawName = WebUtility.HtmlDecode(data.JapaneseTitle);
        var tags = (data.Tags ?? []).Select(tag => tag.Split(':', 2))
            .GroupBy(parts => parts.Length == 2 ? parts[0] : "other", StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(parts => parts.Length == 2 ? parts[1] : parts[0]).ToArray(),
                StringComparer.Ordinal);

        return new ExHentaiResource
        {
            Id = key.Id,
            Url = key.Url,
            Name = name,
            RawName = string.IsNullOrWhiteSpace(rawName) ? name : rawName,
            Category = data.Category switch
            {
                "Doujinshi" => ExHentaiCategory.Doushijin,
                "Manga" => ExHentaiCategory.Manga,
                "Artist CG" => ExHentaiCategory.ArtistCG,
                "Game CG" => ExHentaiCategory.GameCG,
                "Western" => ExHentaiCategory.Western,
                "Image Set" => ExHentaiCategory.ImageSet,
                "Non-H" => ExHentaiCategory.NonH,
                "Cosplay" => ExHentaiCategory.Cosplay,
                "Asian Porn" => ExHentaiCategory.AsianPorn,
                "Misc" => ExHentaiCategory.Misc,
                _ => ExHentaiCategory.Unknown
            },
            CoverUrl = data.Thumbnail ?? "",
            FileCount = data.FileCount,
            Rate = data.Rating,
            UpdateDt = DateTimeOffset.FromUnixTimeSeconds(data.Posted).UtcDateTime,
            Tags = tags,
            TorrentCount = data.TorrentCount,
            TorrentPageUrl = data.TorrentCount > 0
                ? new Uri(new Uri(key.Url), $"/gallerytorrents.php?gid={key.Id}&t={key.Token}").AbsoluteUri
                : ""
        };
    }

    private readonly record struct GalleryKey(int Id, string Token, string Url);

    private static bool TryParseGalleryKey(string? url, Uri? baseUri, out GalleryKey key)
    {
        key = default;
        if (string.IsNullOrWhiteSpace(url)) return false;
        Uri? uri;
        var valid = baseUri == null
            ? Uri.TryCreate(url, UriKind.Absolute, out uri)
            : Uri.TryCreate(baseUri, url, out uri);
        if (!valid || uri == null ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            (uri.Host != "exhentai.org" && uri.Host != "e-hentai.org")) return false;

        var match = Regex.Match(uri.AbsolutePath, @"^/g/(?<gid>\d+)/(?<token>[a-f\d]{10})/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups["gid"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var id) || id <= 0) return false;

        var token = match.Groups["token"].Value.ToLowerInvariant();
        key = new GalleryKey(id, token, new Uri(uri, $"/g/{id}/{token}/").AbsoluteUri);
        return true;
    }

    private sealed class ApiGalleryResponse
    {
        [JsonPropertyName("gmetadata")] public ApiGalleryData[]? Galleries { get; set; }
        public string? Error { get; set; }
    }

    private sealed class ApiGalleryData
    {
        [JsonPropertyName("gid")] public int Id { get; set; }
        public string? Token { get; set; }
        public string? Error { get; set; }
        public string? Title { get; set; }
        [JsonPropertyName("title_jpn")] public string? JapaneseTitle { get; set; }
        public string? Category { get; set; }
        [JsonPropertyName("thumb")] public string? Thumbnail { get; set; }
        public long Posted { get; set; }
        [JsonPropertyName("filecount")] public int FileCount { get; set; }
        public decimal Rating { get; set; }
        public string[]? Tags { get; set; }
        [JsonPropertyName("torrentcount")] public int TorrentCount { get; set; }
        public ApiTorrentData[]? Torrents { get; set; }
    }

    private sealed class ApiTorrentData
    {
        public string? Hash { get; set; }
        public long Added { get; set; }
        [JsonPropertyName("fsize")] public long FileSize { get; set; }
    }
}
