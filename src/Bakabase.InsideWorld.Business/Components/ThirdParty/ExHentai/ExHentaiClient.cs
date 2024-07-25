using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.CookieValidation.Infrastructures;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.Constants;
using Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai.Models.RequestModels;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Configuration.SystemProperty;
using Bootstrap.Components.Configuration.SystemProperty.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using CsQuery;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.ExHentai
{
    public class ExHentaiClient : BakabaseHttpClient
    {
        public const string Domain = "https://exhentai.org/";
        private readonly SemaphoreSlim _lock = new(1, 1);
        protected override string HttpClientName => InternalOptions.HttpClientNames.ExHentai;

        public ExHentaiClient(InsideWorldLocalizer localizer, IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory) : base(httpClientFactory, loggerFactory)
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

        private async Task<string> GetHtmlAsync(HttpClient client, string url)
        {
            await _lock.WaitAsync();
            var tryTimes = 0;
            @try:
            try
            {
                tryTimes++;
                //_logger.LogInformation($"Requesting: {url}");
                var html = await client.GetStringAsync(url);
                ThrowIfBanned(html);
                return html;
            }
            catch (TaskCanceledException tce)
            {
                if (tce.InnerException is TimeoutException)
                {
                    if (tryTimes < 3)
                    {
                        Logger.LogWarning($"Timeout, retrying...({tryTimes})");
                        goto @try;
                    }
                    else
                    {
                        Logger.LogError("Reach max retry limits: 3, throwing");
                    }
                }

                throw;
            }
            catch (HttpRequestException hre)
            {
                if (hre.InnerException is IOException ioe)
                {
                    if (ioe.Message.Contains("EOF"))
                    {
                        if (tryTimes < 3)
                        {
                            Logger.LogWarning($"{ioe.Message}, retrying...({tryTimes})");
                            goto @try;
                        }
                        else
                        {
                            Logger.LogError("Reach max retry limits: 3, throwing");
                        }
                    }
                }

                throw;
            }
            catch (Exception e)
            {
                throw;
            }
            finally
            {
                if (_lock.CurrentCount == 0)
                {
                    _lock.Release();
                }
            }
        }

        private static void ThrowIfBanned(string html)
        {
            // Your IP address has been temporarily banned for excessive pageloads which indicates that you are using automated mirroring/harvesting software. The ban expires in 16 minutes and 38 seconds
            if (IsBanned(html))
            {
                throw new Exception($"ExHentai banned us: {html}");
            }
        }

        private static bool IsBanned(string html) => html.StartsWith("Your") && html.Contains("banned");

        public async Task<ExHentaiList> ParseList(string url)
        {
            var html = await GetHtmlAsync(HttpClient, url);
            var cq = new CQ(html);

            var totalCount = 0;
            {
                var searchTotalCountText = cq[".searchtext"].Text();
                if (searchTotalCountText.IsNotEmpty())
                {
                    var numberText = Regex.Match(searchTotalCountText, "\\d+").Value;
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
                        var numberText = Regex.Match(watchedTotalCountText, "\\d+").Value;
                        if (int.TryParse(numberText, out var tc))
                        {
                            totalCount = tc;
                        }
                    }
                }
            }

            var nextUrl = cq["#unext"].Attr<string>("href");

            var list = new ExHentaiList
            {
                ResultCount = totalCount,
                NextListUrl = nextUrl,
                Resources = ParseList(cq)
            };

            return list;
        }

        public async Task<ExHentaiList> Search(ExHentaiSearchRequestModel model)
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

            if (model.PageIndex > 1)
            {
                queryParameters["page"] = model.PageIndex - 1;
            }

            var queryString = string.Join('&',
                queryParameters.Select(a =>
                    $"{WebUtility.UrlEncode(a.Key)}={WebUtility.UrlEncode(a.Value.ToString())}"));
            var searchUrl = $"{Domain}?{queryString}";

            return await ParseList(searchUrl);
        }

        public async Task<ExHentaiResource> ParseDetail(string url)
        {
            var html = await GetHtmlAsync(HttpClient, url);
            var cq = new CQ(html);
            var nameCq = cq["#gn"];
            var name = nameCq.Text();
            var rawNameCq = cq["#gj"];
            var rawName = rawNameCq.Text();
            var categoryCq = cq["#gdc>div.cs"];
            var categoryClass = categoryCq.Attr<string>("class");
            if (!ExHentaiExtensions.TryParseFromClassName(categoryClass, out var category))
            {
                throw new Exception($"Failed to parsing category from class: {categoryClass}");
            }

            var tagList = cq["#taglist"];
            var tags = new Dictionary<string, string[]>();
            var tagTrCqs = tagList.Find("tr").Select(a => a.Cq()).ToArray();
            foreach (var tr in tagTrCqs)
            {
                var tds = tr.Find("td");
                var group = tr.Find(".tc").Text().Trim(':');
                if (tds.Length > 1)
                {
                    var groupTags = tds[1].ChildElements.Select(a => a.Cq().Text()).ToArray();
                    tags[group] = groupTags.ToArray();
                }
                else
                {
                    tags[group] = new string[] { };
                }
            }

            var fullRatingText = cq["#rating_label"].Text();
            var ratingMatch = Regex.Match(fullRatingText ?? string.Empty, @"[\.\d]+$");
            var rating = 0m;
            if (ratingMatch.Success)
            {
                decimal.TryParse(ratingMatch.Value, out rating);
            }

            var paginationCq = cq[".ptt"];
            var pageCqs = paginationCq.Find("td");
            var lastPageCq = pageCqs.Length > 2 ? pageCqs[^2] : null;
            var lastPageAText = lastPageCq?.Cq().Find("a").Text();
            var pageCount = int.TryParse(lastPageAText, out var pc) ? pc : 1;

            var fileCqs = cq["#gdt"].Children();
            var firstFileCq = fileCqs.FirstOrDefault();
            var coverUrl = firstFileCq?.Cq().Find("img").FirstOrDefault()?.GetAttribute("src");

            var infoContainerCq = cq["#gdd"];
            var fileCountTrCq = infoContainerCq.Find("tr")
                .FirstOrDefault(a => a.Cq().Children().Eq(0).Text() == "Length:")?.Cq();
            var fileCountTds = fileCountTrCq?.Find("td");
            var fileCount = 0;
            if (fileCountTds?.Length > 1)
            {
                var fileCountTdText = fileCountTrCq?.Find("td").Eq(1).Text();
                var fileCountText = Regex.Match(fileCountTdText, "\\d+").Value;
                fileCount = int.TryParse(fileCountText, out var fc) ? fc : 0;
            }

            return new ExHentaiResource
            {
                Name = name,
                RawName = rawName.IsNotEmpty() ? rawName : name,
                Category = category,
                Tags = tags,
                Rate = rating,
                PageCount = pageCount,
                CoverUrl = coverUrl,
                FileCount = fileCount,
                // Introduction = , 
                // UpdateDt = , 
                Url = url,
                Id = ExtractIdFromUrl(url)
            };
        }

        public async Task<byte[]> DownloadImage(string pageUrl)
        {
            var pageHtml = await GetHtmlAsync(HttpClient, pageUrl);
            var pageCq = new CQ(pageHtml);
            var img = pageCq["#img"];
            var imgUrl = img.Attr("src");
            var bytes = await HttpClient.GetByteArrayAsync(imgUrl);
            return bytes;
        }

        private static List<ExHentaiResource> ParseList(CQ cq)
        {
            var container = cq[".itg"];
            var resources = new List<ExHentaiResource>();
            if (container.Any())
            {
                var dms = container[0].Classes.FirstOrDefault(t => t.StartsWith("gl"));
                switch (dms)
                {
                    // Minimal
                    // Minimal+
                    case "gltm":
                    {
                        resources = container.Children("tbody").Children("tr").Skip(1).Select(a =>
                        {
                            var acq = a.Cq();
                            var nameCq = acq.Find(".gl3m.glname>a");
                            var name = nameCq.Text();
                            var url = nameCq.Attr<string>("href");

                            var imgCq = acq.Find(".gl2m .glthumb img");
                            var imgUrl = imgCq.Attr<string>("src");

                            var categoryCq = acq.Find(".gl1m.glcat>.cs").Eq(0);
                            var categoryClass = categoryCq.Attr<string>("class");
                            if (!ExHentaiExtensions.TryParseFromClassName(categoryClass, out var category))
                            {
                                throw new Exception($"Failed to parsing category from class: {categoryClass}");
                            }

                            var dateCq = acq.Find(".gl2m>div").Last();
                            var dateStr = dateCq.Text();
                            var updateDt = DateTime.Parse(dateStr);

                            var pageCq = acq.Find(".gl2m>.glthumb")?.Children()?.Last()?.Children()?.Last()?.Children()
                                ?.Last();
                            var page = int.Parse(Regex.Match(pageCq.Text(), @"\d+").Value);
                            var torrentUrlCq = acq.Find(".gldown>a");
                            var torrentUrl = torrentUrlCq.Attr<string>("href");

                            var resource = new ExHentaiResource
                            {
                                Name = name,
                                Category = category,
                                CoverUrl = imgUrl,
                                FileCount = page,
                                TorrentPageUrl = torrentUrl,
                                UpdateDt = updateDt,
                                Url = url
                            };
                            return resource;
                        }).ToList();
                        break;
                    }
                    // Compact
                    case "gltc":
                    {
                        resources = container.Children("tbody").Children("tr").Skip(1).Select(a =>
                        {
                            var acq = a.Cq();
                            var nameCq = acq.Find(".gl3c.glname>a");
                            var name = nameCq.Text();
                            var url = nameCq.Attr<string>("href");

                            var imgCq = acq.Find(".gl2c .glthumb img");
                            var imgUrl = imgCq.Attr<string>("src");

                            var categoryCq = acq.Find(".gl1c.glcat>.cn").Eq(0);
                            var categoryClass = categoryCq.Attr<string>("class");
                            if (!ExHentaiExtensions.TryParseFromClassName(categoryClass, out var category))
                            {
                                throw new Exception($"Failed to parsing category from class: {categoryClass}");
                            }

                            var dateCq = acq.Find(".gl2c>div").Last()?.First();
                            var dateStr = dateCq.Text();
                            var updateDt = DateTime.Parse(dateStr);

                            var pageCq = acq.Find(".gl2c>.glthumb")?.Children()?.Last()?.Children()?.Last()?.Children()
                                ?.Last();
                            var page = int.Parse(Regex.Match(pageCq.Text(), @"\d+").Value);
                            var torrentUrlCq = acq.Find(".gldown>a");
                            var torrentUrl = torrentUrlCq.Attr<string>("href");

                            var resource = new ExHentaiResource
                            {
                                Name = name,
                                Category = category,
                                CoverUrl = imgUrl,
                                FileCount = page,
                                TorrentPageUrl = torrentUrl,
                                UpdateDt = updateDt,
                                Url = url
                            };
                            return resource;
                        }).ToList();
                        break;
                    }
                    // Extended
                    case "glte":
                    {
                        resources = container.Children("tbody").Children("tr").Select(a =>
                        {
                            var acq = a.Cq();
                            var nameCq = acq.Find(".gl4e.glname>div").First();
                            var name = nameCq.Text();

                            var url = acq.Find(".gl1e a").Attr<string>("href");

                            var imgCq = acq.Find(".gl1e img");
                            var imgUrl = imgCq.Attr<string>("src");

                            var categoryCq = acq.Find(".gl3e>.cn").Eq(0);
                            var categoryClass = categoryCq.Attr<string>("class");
                            if (!ExHentaiExtensions.TryParseFromClassName(categoryClass, out var category))
                            {
                                throw new Exception($"Failed to parsing category from class: {categoryClass}");
                            }

                            var dateCq = acq.Find(".gl3e>div")[1].Cq();
                            var dateStr = dateCq.Text();
                            var updateDt = DateTime.Parse(dateStr);

                            var pageCq = acq.Find(".gl3e>div")[4].Cq();
                            var page = int.Parse(Regex.Match(pageCq.Text(), @"\d+").Value);
                            var torrentUrlCq = acq.Find(".gldown>a");
                            var torrentUrl = torrentUrlCq.Attr<string>("href");

                            var resource = new ExHentaiResource
                            {
                                Name = name,
                                Category = category,
                                CoverUrl = imgUrl,
                                FileCount = page,
                                TorrentPageUrl = torrentUrl,
                                UpdateDt = updateDt,
                                Url = url
                            };
                            return resource;
                        }).ToList();
                        break;
                    }
                    // Thumbnail
                    case "gld":
                    {
                        resources = container.Children().Select(a =>
                        {
                            var acq = a.Cq();
                            var nameCq = acq.Find("a").Eq(0);
                            var name = nameCq.Text();
                            var url = nameCq.Attr<string>("href");

                            var imgCq = acq.Find(".gl3t img");
                            var imgUrl = imgCq.Attr<string>("src");

                            var infoCq = acq.Find(".gl5t");
                            var infoCq1 = infoCq.Children("div").Eq(0);
                            var categoryCq = infoCq1.Children("div").Eq(0);
                            var categoryClass = categoryCq.Attr<string>("class");
                            if (!ExHentaiExtensions.TryParseFromClassName(categoryClass, out var category))
                            {
                                throw new Exception($"Failed to parsing category from class: {categoryClass}");
                            }

                            var dateCq = infoCq1.Children("div").Eq(1);
                            var dateStr = dateCq.Text();
                            var updateDt = DateTime.Parse(dateStr);

                            var infoCq2 = infoCq.Children("div").Eq(1);
                            var pageCq = infoCq2.Children("div").Eq(1);
                            var page = int.Parse(Regex.Match(pageCq.Text(), @"\d+").Value);
                            var torrentUrlCq = infoCq2.Find(".gldown>a");
                            var torrentUrl = torrentUrlCq.Attr<string>("href");

                            var resource = new ExHentaiResource
                            {
                                Name = name,
                                Category = category,
                                CoverUrl = imgUrl,
                                FileCount = page,
                                TorrentPageUrl = torrentUrl,
                                UpdateDt = updateDt,
                                Url = url
                            };
                            return resource;
                        }).ToList();
                        break;
                    }
                }
            }

            foreach (var resource in resources)
            {
                resource.Id = ExtractIdFromUrl(resource.Url);
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
            var data = cq["#gdt"].Children().Where(a => a.ClassName.Contains("gdt")).Select(a => a.Cq())
                .Select(a => (Regex.Match(a.Find("img").Attr("title"), "\\:.*").Value.Trim(':').Trim(),
                    a.Find("a").Attr("href"))).Where(t => t.Item2.IsNotEmpty()).ToArray();
            return data;
        }

        public async Task<(string Title, string PageUrl)[]> GetImageTitleAndPageUrlsFromDetailUrl(string url, int page)
        {
            var html = await GetHtmlAsync(HttpClient, $"{Regex.Replace(url, $@"\?.*", string.Empty)}?p={page}");
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
    }
}