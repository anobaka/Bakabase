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
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
{
    [Enhancer(new object[]
    {
        ReservedResourceProperty.Name,
        ReservedResourceProperty.Introduction,
        ReservedResourceProperty.Series,
        ReservedResourceProperty.Tag,
        ReservedResourceProperty.Publisher,
        ReservedResourceProperty.Rate,
        ReservedResourceProperty.ReleaseDt,
    }, new object[]
    {
        ReservedResourceFileType.Cover
    }, Version = "1.0.2")]
    public class DLsiteEnhancer : InsideWorldHttpClient, IEnhancer
    {
        private static readonly Regex Regex = new Regex(@"[BVR]J\d{6,10}");
        private readonly SpecialTextService _specialTextService;

        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        protected override string HttpClientName => BusinessConstants.HttpClientNames.Default;

        public DLsiteEnhancer(InsideWorldLocalizer localizer, IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory, SpecialTextService specialTextService) : base(localizer, httpClientFactory,
            loggerFactory)
        {
            _specialTextService = specialTextService;
        }

        private const string HtmlUrlTemplate = "https://www.dlsite.com/books/work/=/product_id/{0}.html";

        // rate is here.
        private const string InfoJsonUrlTemplate =
            "https://www.dlsite.com/books/product/info/ajax?product_id={0}&cdn_cache_min=1";

        public async Task<Enhancement[]> Enhance(ResourceDto resource)
        {
            // BJ/VJ/RJ
            var match = Regex.Match(resource.RawName);
            var enhancements = new List<Enhancement>();
            if (match.Success)
            {
                var no = match.Value;
                var url = string.Format(HtmlUrlTemplate, no);
                var rsp = await HttpClient.GetAsync(url);
                if (rsp.StatusCode == HttpStatusCode.NotFound)
                {
                    // throw new Exception($"DLsite returns not found for url: {url}");
                    return enhancements.ToArray();
                }

                var html = await rsp.Content.ReadAsStringAsync();
                var cq = new CQ(html);

                var properties = cq["#work_maker>tbody>tr,#work_outline>tbody>tr"].Select(t => t.Cq())
                    .ToDictionary(t => t.Children("th").Text().Trim(),
                        t =>
                        {
                            var td = t.Children("td");
                            var list = td.Where(a => !a.ClassName.Contains("btn_follow"))
                                .SelectMany(a =>
                                    a.Cq().Find("a").Select(a => a.InnerText)
                                        .Where(x => x?.Trim().IsNotEmpty() == true))
                                .ToArray();
                            return list.Length > 0 ? list : new[] {td.Text()?.Trim()};
                        });
                var publishers = new List<PublisherDto>();
                var companyOrCircleKeys = new[] {"サークル名", "ブランド名"};
                foreach (var k in companyOrCircleKeys)
                {
                    if (properties.TryGetValue(k, out var list))
                    {
                        publishers.AddRange(list.Select(r => new PublisherDto
                        {
                            Name = r
                        }));
                    }
                }

                foreach (var (key, list) in properties)
                {
                    if (companyOrCircleKeys.Contains(key))
                    {
                        continue;
                    }

                    switch (key)
                    {
                        case "著者":
                            var ps = list.Select(r => new PublisherDto
                            {
                                Name = r
                            });
                            if (publishers.Count == 1)
                            {
                                publishers[0].SubPublishers.AddRange(ps);
                            }
                            else
                            {
                                publishers.AddRange(ps);
                            }

                            break;
                        case "販売日":
                        {
                            var dt = await _specialTextService.TryToParseDateTime(list.FirstOrDefault());
                            if (dt.HasValue)
                            {
                                enhancements.Add(Enhancement.BuildReleaseDt(dt.Value));
                            }

                            break;
                        }
                        case "シリーズ名":
                            enhancements.Add(Enhancement.BuildSeries(new SeriesDto {Name = list.FirstOrDefault()}));
                            break;
                        case "ジャンル":
                            enhancements.Add(Enhancement.BuildTag(list.Select(a => new TagDto {Name = a})));
                            break;
                        case "声優":
                            enhancements.Add(Enhancement.BuildCustomProperty("Cast", CustomDataType.String, list,
                                true));
                            break;
                        default:
                            // enhancements.Add(Enhancement.BuildCustomProperty(key, CustomDataType.String, string.Join(", ", list)));
                            break;
                    }
                }

                if (publishers.Any())
                {
                    enhancements.Add(Enhancement.BuildPublisher(publishers));
                }

                var coverUrl = cq[".product-slider-data"].Children().First().Data<string>("src");
                if (coverUrl.IsNotEmpty())
                {
                    var coverData = await HttpClient.GetByteArrayAsync(coverUrl.AddSchemaSafely());
                    var ext = Path.GetExtension(coverUrl);
                    enhancements.Add(Enhancement.BuildReservedFile(ReservedResourceFileType.Cover,
                        new EnhancementFile {Data = coverData, RelativePath = $"cover{ext}"}));
                }

                var name = cq["#work_name"].Text().Trim();
                enhancements.Add(Enhancement.BuildName(name));
                var introduce = cq[".work_parts_area"].First().Text().Trim();
                enhancements.Add(Enhancement.BuildIntroduction(introduce));
                var productInfoStr = await HttpClient.GetStringAsync(string.Format(InfoJsonUrlTemplate, no));
                var productInfo = JsonConvert.DeserializeObject<Dictionary<string, ProductInfo>>(productInfoStr);
                var rate = productInfo[no].RateAverage2Dp;
                if (rate.HasValue)
                {
                    enhancements.Add(Enhancement.BuildRate(rate.Value));
                }
                // todo: Forms
            }

            return enhancements.ToArray();
        }

        private class ProductInfo
        {
            [JsonProperty(propertyName: "rate_average_2dp")]
            public decimal? RateAverage2Dp { get; set; }
            // [JsonProperty("work_name")]
            // public string WorkName { get; set; }
            // [JsonProperty("work_image")]
            // public string WorkImage { get; set; }
        }
    }
}