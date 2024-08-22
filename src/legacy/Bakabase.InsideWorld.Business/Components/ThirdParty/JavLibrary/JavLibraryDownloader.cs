using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Tasks.Progressor.Abstractions;
using Bootstrap.Components.Tasks.Progressor.SignalR;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.ThirdParty.JavLibrary
{
    public class JavLibraryDownloader : AbstractProgressor<JavLibraryDownloaderProgress,
            JavLibraryDownloaderStartRequestModel>,
        ISignalRGenericProgressor<JavLibraryDownloaderStartRequestModel>
    {
        private readonly IHttpClientFactory _httpClientFactory;
        public JavLibraryDownloader(IHttpClientFactory httpClientFactory, IProgressDispatcher progressDispatcher = null) : base(progressDispatcher)
        {
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task StartCore(JavLibraryDownloaderStartRequestModel @params, CancellationToken ct)
        {
            await UpdateProgress(t => t.Results = @params.Urls.ToDictionary(a => a, a => (bool?)null));


            var client = _httpClientFactory.CreateClient(InternalOptions.HttpClientNames.JavLibrary);

            // var client = new HttpClient
            // {
            //     DefaultRequestHeaders =
            //     {
            //         {"Cookie", @params.Cookie},
            //         {
            //             "User-Agent",
            //             "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/94.0.4606.71 Safari/537.36 Edg/94.0.992.38"
            //         }
            //     }
            // };

            var downloadUrlsFilename = Path.Combine(@params.DownloadPath, $"downloads-{DateTime.Now:HHmmss}.txt");
            File.Delete(downloadUrlsFilename);

            //throw new Exception("123212312312");

            foreach (var u in @params.Urls)
            {
                var tryTimes = 0;
                while (tryTimes++ < 3)
                {
                    try
                    {
                        await Task.Delay(1000, ct);
                        // http://www.javlibrary.com/cn/?v=javme2zd7q
                        var coverHtml = await client.GetStringAsync(u, ct);
                        var coverCq = new CQ(coverHtml);
                        var title = Regex.Match(coverCq["#video_title .post-title"].Text(), @"^[a-zA-Z]+-\d+").Value
                            .ToString();

                        var page = 1;
                        var done = false;
                        string downloadUrl = null;
                        string magnet = null;
                        while (!done)
                        {
                            // https://www.javlibrary.com/cn/videocomments.php?mode=2&v=javme2zd7q&page=1
                            var url = u.Replace("/cn/", "/cn/videocomments.php") + $"&mode=2&page={page}";
                            await Task.Delay(1000, ct);
                            var commentHtml = await client.GetStringAsync(url, ct);
                            var commentCq = new CQ(commentHtml);
                            var comments = commentCq["#video_comments .comment .t"];
                            if (!comments.Any())
                            {
                                break;
                            }

                            foreach (var c in comments)
                            {
                                var hidden = c.Cq().Find(".hidden");
                                if (hidden.Any())
                                {
                                    var text = hidden.Text();
                                    foreach (var kw in @params.TorrentLinkKeywords)
                                    {
                                        var reg = new Regex($@"\[url=(?<u>(http.+|\/\/){kw}[^\]]+)\]");
                                        var match = reg.Match(text);
                                        if (match.Success)
                                        {
                                            downloadUrl = match.Groups["u"].Value;
                                            done = true;
                                            break;
                                        }
                                    }

                                    if (magnet == null)
                                    {
                                        var magnetReg = new Regex(@"magnet\:\?.+?($|\s)");
                                        var magnetMatch = magnetReg.Match(text);
                                        if (magnetMatch.Success)
                                        {
                                            magnet = magnetMatch.Value.Trim();
                                        }
                                    }
                                }

                                if (done)
                                {
                                    break;
                                }
                            }

                            page++;
                        }

                        if (!done && magnet.IsNotEmpty())
                        {
                            done = true;
                        }

                        if (done)
                        {
                            var rawCoverUrl = coverCq["#video_jacket_img"].Attr("src");
                            var coverUrl = rawCoverUrl.StartsWith("//") ? $"https:{rawCoverUrl}" : rawCoverUrl;
                            var coverFilename = Path.Combine(@params.DownloadPath, $"{title}.jpg");
                            await File.WriteAllBytesAsync(coverFilename, await client.GetByteArrayAsync(coverUrl, ct),
                                ct);
                            await Task.Delay(1000, ct);
                            await File.AppendAllLinesAsync(downloadUrlsFilename,
                                new[] { $"{u}\t{title}\t{downloadUrl}\t{magnet}" }, ct);
                            await UpdateProgress(t => t.Results[u] = true);
                        }
                        else
                        {
                            await UpdateProgress(t => t.Results[u] = false);
                        }

                        break;
                    }
                    catch (Exception)
                    {
                        if (tryTimes >= 3)
                        {
                            throw;
                        }
                    }
                }
            }
        }
    }
}