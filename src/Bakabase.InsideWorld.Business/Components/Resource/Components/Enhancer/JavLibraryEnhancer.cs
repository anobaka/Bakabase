using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Network;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
{
    [Enhancer(new object[]
    {
        ReservedResourceProperty.Name,
        ReservedResourceProperty.Tag,
        ReservedResourceProperty.Publisher,
        ReservedResourceProperty.Rate,
        ReservedResourceProperty.ReleaseDt,
    }, new object[]
    {
        ReservedResourceFileType.Cover
    }, Version = "1.0.1")]
    public class JavLibraryEnhancer : InsideWorldHttpClient, IEnhancer
    {
        private static readonly Regex CodeRegex = new(@"(?<prefix>[a-zA-Z]+)-?(?<no>\d+)");
        private const string SearchTemplate = "https://www.javlibrary.com/cn/vl_searchbyid.php?keyword={0}";
        protected override string HttpClientName => BusinessConstants.HttpClientNames.JavLibrary;

        private readonly SpecialTextService _specialTextService;

        public JavLibraryEnhancer(InsideWorldLocalizer localizer, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, SpecialTextService specialTextService) : base(localizer, httpClientFactory, loggerFactory)
        {
            _specialTextService = specialTextService;
        }

        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        public async Task<Enhancement[]> Enhance(ResourceDto resource)
        {
            var match = CodeRegex.Match(resource.RawName);
            var enhancements = new List<Enhancement>();
            if (match.Success)
            {
                var prefix = match.Groups["prefix"].Value;
                var no = match.Groups["no"].Value;

                var code = $"{prefix}-{no}";

                // search in jav library
                var searchUrl = string.Format(SearchTemplate, code);

                var client = new HttpClient(new HttpClientHandler {AllowAutoRedirect = false})
                {
                    DefaultRequestHeaders =
                    {
                        {
                            "User-Agent",
                            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/107.0.0.0 Safari/537.36 Edg/107.0.1418.35"
                        }
                    }
                };
                var rsp = await client.GetAsync(searchUrl);
                if (rsp.StatusCode == HttpStatusCode.OK)
                {
                    // Found multiple results, skip.
                    return enhancements.ToArray();
                }

                if (rsp.StatusCode == HttpStatusCode.Redirect)
                {
                    var detailUrl = new Uri(new Uri(searchUrl), rsp.Headers.Location);
                    var detailHtml = await client.GetStringAsync(detailUrl);
                    var cq = new CQ(detailHtml);
                    // releaseDt
                    var releaseDtStr = cq["#video_date"]?.Find(".text")?.Text();
                    var releaseDt = await _specialTextService.TryToParseDateTime(releaseDtStr);
                    if (releaseDt.HasValue)
                    {
                        enhancements.Add(Enhancement.BuildReleaseDt(releaseDt.Value));
                    }

                    // rate
                    var rateStr = cq["#video_review .score"]?.Text().Trim('(', ')');
                    if (decimal.TryParse(rateStr, out var rate))
                    {
                        enhancements.Add(Enhancement.BuildRate(rate));
                    }

                    // name
                    var name = cq["#video_title"]?.Find("h3")?.Text();
                    if (name.IsNotEmpty())
                    {
                        enhancements.Add(Enhancement.BuildName(name));
                    }

                    // tag
                    var tags = cq["#video_genres .genre"]?.Select(a => a.InnerText)?.ToArray();
                    if (tags?.Length > 0)
                    {
                        enhancements.Add(Enhancement.BuildTag(tags.Select(a => new TagDto {Name = a})));
                    }

                    // cast
                    var casts = cq["#video_cast .cast"]?.Select(a => a.InnerText)?.ToArray();
                    if (casts?.Length > 0)
                    {
                        enhancements.Add(Enhancement.BuildPublisher(casts.Select(a => new PublisherDto {Name = a})));
                    }

                    // cover 
                    var coverUrl = cq["#video_jacket_img"]?.Attr("src");
                    if (coverUrl.IsNotEmpty())
                    {
                        if (coverUrl.StartsWith("//"))
                        {
                            coverUrl = $"https://{coverUrl.TrimStart('/')}";
                        }

                        var bytes = await client.GetByteArrayAsync(coverUrl);
                        var extension = Path.GetExtension(coverUrl);
                        enhancements.Add(Enhancement.BuildReservedFile(ReservedResourceFileType.Cover,
                            new EnhancementFile[] {new() {Data = bytes, RelativePath = $"cover{extension}"}}));
                    }
                }
            }

            return enhancements.ToArray();
        }
    }
}