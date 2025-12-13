using System.Net.Http.Json;
using System.Text.Json;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.Hdouban.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Hdouban;

public class HdoubanClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Default;

    public async Task<HdoubanVideoDetail?> SearchAndParseVideo(string number, string? appointUrl = null)
    {
        try
        {
            // Search via API
            var searchUrl = $"https://api.6dccbca.com/api/search?ty=movie&search={Uri.EscapeDataString(number)}&page=1&pageSize=12";
            var searchJson = await HttpClient.GetStringAsync(searchUrl);
            using var doc = JsonDocument.Parse(searchJson);
            if (!doc.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty("list", out var list))
            {
                return null;
            }

            string? detailId = null;
            var normalized = number.ToUpperInvariant().Replace("-", "").Trim();
            foreach (var item in list.EnumerateArray())
            {
                var num = item.GetProperty("number").GetString() ?? string.Empty;
                var name = item.GetProperty("name").GetString() ?? string.Empty;
                var numNorm = num.ToUpperInvariant().Replace("-", "");
                if (normalized == numNorm || name.ToUpperInvariant().Replace("-", "").Contains(normalized))
                {
                    detailId = item.GetProperty("id").GetRawText().Trim('"');
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(detailId)) return null;

            // Detail via API
            var detailUrl = "https://api.6dccbca.com/api/movie/detail";
            var payload = new Dictionary<string, string> { { "id", detailId } };
            using var resp = await HttpClient.PostAsJsonAsync(detailUrl, payload);
            if (!resp.IsSuccessStatusCode) return null;
            var detailStr = await resp.Content.ReadAsStringAsync();
            using var detailDoc = JsonDocument.Parse(detailStr);
            if (!detailDoc.RootElement.TryGetProperty("data", out var res)) return null;

            var webNumber = res.GetProperty("number").GetString() ?? number;
            if (!System.Text.RegularExpressions.Regex.IsMatch(webNumber, "n\\d{3,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                webNumber = webNumber.ToUpperInvariant();
            }

            var title = res.GetProperty("name").GetString() ?? string.Empty;
            title = title.Replace(webNumber, "").Trim();
            if (string.IsNullOrWhiteSpace(title)) return null;

            string ActorFromArray(JsonElement arr)
            {
                var list = new List<string>();
                foreach (var a in arr.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? string.Empty;
                    var sex = a.TryGetProperty("sex", out var s) ? s.GetString() ?? string.Empty : string.Empty;
                    if (sex.Contains("♀")) list.Add(name.Replace("♀", ""));
                }
                return string.Join(",", list);
            }

            string TagFromArray(JsonElement arr)
            {
                var list = new List<string>();
                foreach (var t in arr.EnumerateArray()) list.Add(t.GetProperty("name").GetString() ?? string.Empty);
                return string.Join(",", list);
            }

            var cover = res.GetProperty("big_cove").GetString() ?? string.Empty;
            var poster = res.GetProperty("small_cover").GetString() ?? string.Empty;
            var actor = res.TryGetProperty("actors", out var actors) ? ActorFromArray(actors) : string.Empty;
            var tag = res.TryGetProperty("labels", out var labels) ? TagFromArray(labels) : string.Empty;
            var director = res.TryGetProperty("director", out var directors) && directors.GetArrayLength() > 0
                ? directors[0].GetProperty("name").GetString() ?? string.Empty
                : string.Empty;
            var studio = res.TryGetProperty("company", out var company) && company.GetArrayLength() > 0
                ? company[0].GetProperty("name").GetString() ?? string.Empty
                : string.Empty;
            var series = res.TryGetProperty("series", out var seriesArr) && seriesArr.GetArrayLength() > 0
                ? seriesArr[0].GetProperty("name").GetString() ?? string.Empty
                : string.Empty;
            var release = res.GetProperty("release_time").GetString()?.Replace(" 00:00:00", "") ?? string.Empty;
            var runtime = res.GetProperty("time").GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(runtime) && int.TryParse(runtime, out var seconds))
            {
                runtime = (seconds / 3600).ToString();
            }
            var year = string.IsNullOrWhiteSpace(release) ? string.Empty : (release.Length >= 4 ? release.Substring(0, 4) : "");

            var mosaic = (title + studio + tag).Contains("国产") || (title + studio + tag).Contains("國產") ? "国产" : string.Empty;

            var detail = new HdoubanVideoDetail
            {
                Number = webNumber,
                Title = title,
                OriginalTitle = title,
                Actor = actor,
                Tag = tag,
                Release = release.Replace("N/A", string.Empty),
                Year = year,
                Runtime = runtime.Replace("N/A", string.Empty),
                Series = series.Replace("N/A", string.Empty),
                Studio = studio.Replace("N/A", string.Empty),
                Publisher = studio,
                CoverUrl = cover,
                PosterUrl = poster,
                Website = appointUrl ?? $"https://ormtgu.com/moviedetail/{detailId}",
                Source = "hdouban",
                Mosaic = mosaic,
                SearchUrl = searchUrl
            };

            return detail;
        }
        catch
        {
            return null;
        }
    }
}


