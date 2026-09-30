using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.RequestModels;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai
{
    public partial class ExHentaiClient : BakabaseHttpClient
    {
        public const string Domain = "https://exhentai.org/";
        private readonly SemaphoreSlim _lock = new(1, 1);
        protected override string HttpClientName => InternalOptions.HttpClientNames.ExHentai;

        public ExHentaiClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) : base(
            httpClientFactory, loggerFactory)
        {
        }

        public async Task<ExHentaiConnectionStatus> CheckStatus()
        {
            try
            {
                var rsp = await HttpClient.GetAsync(Domain);
                if (rsp.StatusCode == HttpStatusCode.Redirect)
                {
                    return ExHentaiConnectionStatus.InvalidCookie;
                }

                var html = await rsp.Content.ReadAsStringAsync();
                if (IsBanned(html))
                {
                    return ExHentaiConnectionStatus.IpBanned;
                }

                var cq = new CQ(html);
                if (cq[".nbw"]?.Text()?.Trim() == "Front Page")
                {
                    return ExHentaiConnectionStatus.Ok;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"An error occurred during checking connection status to exhentai: {e.Message}");
            }

            return ExHentaiConnectionStatus.UnknownError;
        }

        /// <summary>
        /// Attempts per page load, the first included. Only transient network failures are retried.
        /// </summary>
        private const int MaxHtmlAttempts = 3;

        private async Task<string> GetHtmlAsync(HttpClient client, string url, CancellationToken ct = default)
            => (await GetExHentaiPageAsync(ValidateImageRequestUri(url), null, ct)).Html;

        private static void ThrowIfBanned(string html)
        {
            // Your IP address has been temporarily banned for excessive pageloads which indicates that you are using automated mirroring/harvesting software. The ban expires in 16 minutes and 38 seconds
            if (IsBanned(html))
            {
                throw new Exception($"ExHentai banned us: {html}");
            }
        }

        private static bool IsBanned(string html) => html.StartsWith("Your") && html.Contains("banned");

        public async Task<ExHentaiList> ParseList(string url, CancellationToken ct = default,
            bool includeMetadata = true)
        {
            var html = await GetHtmlAsync(HttpClient, url, ct);
            var cq = new CQ(html);

            var totalCount = 0;
            {
                var searchTotalCountText = cq[".searchtext"].Text();
                if (searchTotalCountText.IsNotEmpty())
                {
                    var numberText = Regex.Match(searchTotalCountText, @"\d[\d,]*").Value.Replace(",", "");
                    if (int.TryParse(numberText, out var tc))
                    {
                        totalCount = tc;
                    }
                }

                // try watched page
                if (totalCount == 0)
                {
                    var watchedTotalCountText = cq[".ip>strong"].Text();
                    if (watchedTotalCountText.IsNotEmpty())
                    {
                        var numberText = Regex.Match(watchedTotalCountText, @"\d[\d,]*").Value.Replace(",", "");
                        if (int.TryParse(numberText, out var tc))
                        {
                            totalCount = tc;
                        }
                    }
                }
            }

            var nextHref = cq["#unext"].Attr<string>("href");
            var nextUrl = string.IsNullOrWhiteSpace(nextHref)
                ? null
                : new Uri(new Uri(url), nextHref).AbsoluteUri;

            var list = new ExHentaiList
            {
                ResultCount = totalCount,
                NextListUrl = nextUrl,
                Resources = ParseGalleryLinks(cq, new Uri(url))
            };

            if (includeMetadata && list.Resources.Count > 0)
            {
                list.Resources = await GetGalleryMetadata(list.Resources.Select(r => r.Url).ToArray(), ct);
            }

            return list;
        }

        public async Task<ExHentaiList> Search(ExHentaiSearchRequestModel model, CancellationToken ct = default,
            bool includeMetadata = true)
        {
            var queryParameters = new Dictionary<string, object>();
            if (model.Keyword.IsNotEmpty())
            {
                queryParameters["f_search"] = model.Keyword;
            }

            if (model.HideCategories.Any())
            {
                queryParameters["f_cats"] = model.HideCategories.Sum(t => (int) t).ToString();
            }

            var queryString = string.Join('&',
                queryParameters.Select(a =>
                    $"{WebUtility.UrlEncode(a.Key)}={WebUtility.UrlEncode(a.Value.ToString())}"));
            var searchUrl = $"{Domain}?{queryString}";

            // Search pagination uses the next URL supplied by the site, rather than legacy page=N.
            for (var page = 1; page < Math.Max(1, model.PageIndex); page++)
            {
                var discovered = await ParseList(searchUrl, ct, includeMetadata: false);
                if (string.IsNullOrWhiteSpace(discovered.NextListUrl) || discovered.NextListUrl == searchUrl)
                {
                    return new ExHentaiList { ResultCount = discovered.ResultCount, Resources = [] };
                }

                searchUrl = discovered.NextListUrl;
            }

            return await ParseList(searchUrl, ct, includeMetadata);
        }

        /// <summary>Gallery metadata comes from the API; the torrent window supplies download URLs.</summary>
        public async Task<ExHentaiResource> ParseDetail(string url, bool includeTorrents,
            CancellationToken ct = default)
        {
            var galleries = await FetchGalleryMetadata([url], ct);
            var gallery = galleries[0];
            var resource = MapGalleryMetadata(gallery.Key, gallery.Data);
            if (includeTorrents && resource.TorrentCount > 0)
            {
                resource.Torrents = await GetTorrentList(resource.TorrentPageUrl, ct);
                foreach (var torrent in resource.Torrents ?? [])
                {
                    var metadata = gallery.Data.Torrents?.FirstOrDefault(t =>
                        !string.IsNullOrEmpty(t.Hash) &&
                        torrent.DownloadUrl.Contains(t.Hash, StringComparison.OrdinalIgnoreCase));
                    if (metadata != null)
                    {
                        // fsize is the content size; tsize is the size of the .torrent file itself.
                        torrent.Size = metadata.FileSize;
                        torrent.UpdatedAt = DateTimeOffset.FromUnixTimeSeconds(metadata.Added).UtcDateTime;
                    }
                }
            }

            return resource;
        }

        /// <summary>
        /// This is the thumbnail-list page count, which depends on account display settings.
        /// It is deliberately fetched only when downloading images, not from API filecount.
        /// </summary>
        public async Task<int> GetGalleryPageCount(string url, CancellationToken ct = default)
        {
            var html = await GetHtmlAsync(HttpClient, url, ct);
            var cq = new CQ(html);
            if (!cq["#gdt"].Any())
                throw new InvalidDataException($"Gallery thumbnail list was not found at {url}.");
            var pageCells = cq[".ptt"].Find("td");
            var lastPageCell = pageCells.Length > 2 ? pageCells[^2] : null;
            var lastPageText = lastPageCell?.Cq().Find("a").Text();
            return int.TryParse(lastPageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                ? count
                : 1;
        }

        public async Task<(byte[] Data, string? ContentType)> DownloadImage(string pageUrl,
            CancellationToken ct = default)
        {
            var image = await DownloadImage(pageUrl, new ExHentaiImageDownloadOptions(), ct);
            return (image.Data, image.ContentType);
        }

        /// <summary>
        /// Downloads an image directly from its URL (for cover images etc).
        /// Unlike <see cref="DownloadImage"/> which expects an ExHentai page URL
        /// and extracts the image from it, this method downloads the URL as-is.
        /// </summary>
        public async Task<(byte[] Data, string? ContentType)> DownloadImageByUrl(string imageUrl,
            CancellationToken ct = default)
        {
            var imageUri = ValidateImageRequestUri(imageUrl);
            using var response = await SendImageRequestAsync(imageUri, null, ct);
            return await ReadImageBytesAsync(response, ct);
        }

        private static List<ExHentaiResource> ParseGalleryLinks(CQ cq, Uri listUrl)
        {
            var resources = new List<ExHentaiResource>();
            var seen = new Dictionary<(int Id, string Token), ExHentaiResource>();
            foreach (var anchor in cq[".itg"].Find("a"))
            {
                if (!TryParseGalleryKey(anchor.GetAttribute("href"), listUrl, out var key)) continue;
                var name = anchor.Cq().Text().Trim();
                if (seen.TryGetValue((key.Id, key.Token), out var existing))
                {
                    // A thumbnail link can precede the title link for the same gallery.
                    if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = name;
                    continue;
                }

                var resource = new ExHentaiResource { Id = key.Id, Url = key.Url, Name = name };
                resources.Add(resource);
                seen.Add((key.Id, key.Token), resource);
            }

            return resources;
        }

        public static int ExtractIdFromUrl(string url)
        {
            var urlSegments = url.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            var idStr = urlSegments.FirstOrDefault(a => Regex.IsMatch(a, "^\\d{5,10}$"))!;
            return int.Parse(idStr);
        }

        public static (string Title, string PageUrl)[] GetImageTitleAndPageUrlsFromDetailHtml(string detailHtml)
        {
            var cq = new CQ(detailHtml);
            // Page 197: 094.jpg
            var data = cq["#gdt"].Children("a").Select(a => a.Cq())
                .Select(a => (a.Children("div").Attr("title"), a.Attr("href"))).Where(t => t.Item2.IsNotEmpty())
                .ToArray();
            return data;
        }

        public async Task<(string Title, string PageUrl)[]> GetImageTitleAndPageUrlsFromDetailUrl(string url, int page,
            CancellationToken ct = default)
        {
            var html = await GetHtmlAsync(HttpClient, $"{Regex.Replace(url, $@"\?.*", string.Empty)}?p={page}", ct);
            return GetImageTitleAndPageUrlsFromDetailHtml(html);
        }

        public async Task<ExHentaiImageLimits> GetImageLimits()
        {
            var html = await GetHtmlAsync(HttpClient, "https://e-hentai.org/home.php");
            var cq = new CQ(html);
            var homeBox = cq[".homebox"];
            var children = homeBox.Children("p");
            var currentP = children.First();
            var resetCostP = children[1].Cq();

            var currentStrongList = currentP.Find("strong");
            var currentCount = int.Parse(currentStrongList.First().Text());
            var limit = int.Parse(currentStrongList[1].InnerText);

            var resetCost = int.Parse(resetCostP.Find("strong").Text());

            return new ExHentaiImageLimits
            {
                Current = currentCount,
                Limit = limit,
                ResetCost = resetCost
            };
        }

        protected async Task<List<ExHentaiTorrent>?> GetTorrentList(string torrentPageUrl,
            CancellationToken ct = default)
        {
            var html = await GetHtmlAsync(HttpClient, torrentPageUrl, ct);
            var cq = new CQ(html);
            var forms = cq["form"];
            var torrents = new List<ExHentaiTorrent>();
            foreach (var form in forms)
            {
                // CQ's selector indexer searches the document; Find stays inside this form.
                var trs = form.Cq().Find("table tr");
                if (trs?.Length >= 3)
                {
                    
                    var meta = trs.FirstOrDefault()!.Cq().Find("td").Select(x => x.TextContent.Split(':', 2))
                        .Where(x => x.Length == 2)
                        .ToDictionary(d => d[0].Trim(), d => d[1].Trim());
                    if (!meta.TryGetValue("Size", out var size) ||
                        !meta.TryGetValue("Downloads", out var downloaded) ||
                        !meta.TryGetValue("Posted", out var posted)) continue;
                    var downloadAnchor = trs![2]!.Cq().Find("a").First();
                    var href = downloadAnchor.Attr<string>("href");
                    if (string.IsNullOrWhiteSpace(href)) continue;
                    var downloadLink = ResolveTorrentDownloadLink(torrentPageUrl, href,
                        downloadAnchor.Attr<string>("onclick"));
                    var torrent = new ExHentaiTorrent
                    {
                        DownloadUrl = downloadLink,
                        Size = ConvertToBytes(size),
                        Downloaded = int.Parse(downloaded),
                        Seeds = ParseSourceCount("Seeds"),
                        Peers = ParseSourceCount("Peers"),
                        UpdatedAt = DateTime.Parse(posted)
                    };
                    torrents.Add(torrent);

                    int? ParseSourceCount(string field) => meta.TryGetValue(field, out var raw) &&
                        int.TryParse(raw, NumberStyles.Integer | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture, out var count) && count >= 0
                            ? count
                            : null;
                }
            }

            return torrents;
        }

        static long ConvertToBytes(string size)
        {
            string[] parts = size.Trim().Split(' ');
            if (parts.Length != 2)
                throw new ArgumentException("Invalid format");

            double value = double.Parse(parts[0], CultureInfo.InvariantCulture);
            string unit = parts[1].ToUpperInvariant();

            return unit switch
            {
                "B" => (long)value,
                "KIB" => (long)(value * 1024),
                "MIB" => (long)(value * 1024 * 1024),
                "GIB" => (long)(value * 1024 * 1024 * 1024),
                _ => throw new ArgumentException("Unknown unit"),
            };
        }

    }
}
