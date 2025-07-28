using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.ThePornDBMovies.Models;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ThePornDBMovies;

public class ThePornDBMoviesClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<ThePornDBMoviesVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null, string? apiToken = null, string? filePath = null, bool skipHash = false)
    {
        try
        {
            if (string.IsNullOrEmpty(apiToken)) return null;
            var client = HttpClient;
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            string? detailUrl = appointUrl?.Replace("//theporndb", "//api.theporndb");

            if (string.IsNullOrEmpty(detailUrl))
            {
                var keyword = GuessSearchKeyword(number);
                var searchUrl = $"https://api.theporndb.net/movies?q={Uri.EscapeDataString(keyword)}&per_page=50";
                var searchJson = await client.GetStringAsync(searchUrl);
                using var searchDoc = JsonDocument.Parse(searchJson);
                var movies = searchDoc.RootElement.GetProperty("data");
                if (movies.GetArrayLength() == 0) return null;
                var first = movies[0];
                var slug = first.GetProperty("slug").GetString();
                if (string.IsNullOrEmpty(slug)) return null;
                detailUrl = $"https://api.theporndb.net/movies/{slug}";
            }

            var detailJson = await client.GetStringAsync(detailUrl);
            using var doc = JsonDocument.Parse(detailJson);
            var data = doc.RootElement.GetProperty("data");

            var title = data.GetProperty("title").GetString() ?? string.Empty;
            var outline = (data.TryGetProperty("description", out var desc) ? desc.GetString() ?? string.Empty : string.Empty)
                .Replace("＜p＞", string.Empty).Replace("＜/p＞", string.Empty);
            var release = data.TryGetProperty("date", out var dateEl) ? dateEl.GetString() ?? string.Empty : string.Empty;
            var year = Regex.Match(release ?? string.Empty, @"\d{4}").Value;
            var trailer = data.TryGetProperty("trailer", out var tl) ? tl.GetString() ?? string.Empty : string.Empty;
            var cover = TryNested(data, ["background", "large"]) ?? (data.TryGetProperty("image", out var img) ? img.GetString() : string.Empty);
            var poster = TryNested(data, ["posters", "large"]) ?? (data.TryGetProperty("poster", out var pst) ? pst.GetString() : string.Empty);
            var runtime = data.TryGetProperty("duration", out var dur) ? (int.TryParse(dur.ToString(), out var d) ? (d / 60).ToString() : string.Empty) : string.Empty;
            var series = TryNested(data, ["site", "name"]) ?? string.Empty;
            var studio = TryNested(data, ["site", "network", "name"]) ?? string.Empty;
            var publisher = studio;
            var director = TryNested(data, ["director", "name"]) ?? string.Empty;
            var tag = string.Join(",", data.TryGetProperty("tags", out var tags) ? tags.EnumerateArray().Select(t => t.GetProperty("name").GetString()) : Array.Empty<string>());
            var femaleActors = data.TryGetProperty("performers", out var perf2) ? perf2.EnumerateArray().Where(p => p.GetProperty("parent").GetProperty("extras").GetProperty("gender").GetString() != "Male").Select(p => p.GetProperty("name").GetString()).ToArray() : Array.Empty<string>();
            var actor = string.Join(",", femaleActors);

            return new ThePornDBMoviesVideoDetail
            {
                Number = BuildNumber(series, release, title),
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release,
                Year = year,
                Runtime = runtime,
                Series = series,
                Studio = studio,
                Publisher = publisher,
                Source = "theporndb",
                CoverUrl = cover,
                PosterUrl = poster,
                Website = detailUrl,
                Mosaic = "无码"
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? TryNested(JsonElement el, string[] path)
    {
        try
        {
            foreach (var key in path) el = el.GetProperty(key);
            return el.GetString();
        }
        catch { return null; }
    }

    private static string GuessSearchKeyword(string input)
    {
        var m = Regex.Match(input, @"([A-Za-z0-9\-\.]{2,})[\-_\. ]{1}2?0?(\d{2}[\.-]\d{2}[\.-]\d{2})");
        if (m.Success)
        {
            var series = m.Groups[1].Value.Replace("-", string.Empty).Replace(".", string.Empty);
            var date = "20" + m.Groups[2].Value.Replace('.', '-');
            return series + " " + date;
        }
        return input.Replace('-', ' ');
    }

    private static string BuildNumber(string series, string release, string title)
    {
        var m = Regex.Match(release ?? string.Empty, @"\d{2}-\d{2}-\d{2}");
        if (!string.IsNullOrEmpty(series) && m.Success)
        {
            return series.Replace(" ", string.Empty) + "." + m.Value.Replace("-", ".");
        }
        return title;
    }
}


