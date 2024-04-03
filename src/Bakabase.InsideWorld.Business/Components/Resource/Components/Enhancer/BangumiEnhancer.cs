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
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using CsQuery;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
{
    [Enhancer(new object[]
    {
        ReservedResourceProperty.Name,
        ReservedResourceProperty.Tag,
        ReservedResourceProperty.Original,
        ReservedResourceProperty.Rate
    }, new object[]
    {
        ReservedResourceFileType.Cover
    })]
    public class BangumiEnhancer : IEnhancer
    {
        private readonly SpecialTextService _specialTextService;

        public BangumiEnhancer(SpecialTextService specialTextService)
        {
            _specialTextService = specialTextService;
        }

        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        /// <summary>
        /// </summary>
        /// <param name="resource"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        public async Task<Enhancement[]> Enhance(Models.Domain.Resource resource)
        {
            var enhancements = new List<Enhancement>();

            var client = new HttpClient
            {
                DefaultRequestHeaders =
                {
                    {
                        "User-Agent",
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/110.0.0.0 Safari/537.36 Edg/110.0.1587.57"
                    }
                }
            };
            // Search: https://bgm.tv/subject_search/%E3%83%9F%E3%82%B9%E3%83%86%E3%83%AA%E3%81%A8%E8%A8%80%E3%81%86%E5%8B%BF%E3%82%8C+%E5%8B%BF%E8%A8%80%E6%8E%A8%E7%90%86?cat=all
            // Detail: https://bgm.tv/subject/338076
            var keyword = resource.Name ?? Path.GetFileNameWithoutExtension(resource.FileName);
            var searchUrl = $"https://bgm.tv/subject_search/{WebUtility.UrlEncode(keyword)}?cat=all";
            var sampleUri = new Uri(searchUrl);
            var searchHtml = await client.GetStringAsync(searchUrl);
            var searchCq = new CQ(searchHtml);

            var firstResult = searchCq["#browserItemList>li"].First();
            if (firstResult != null)
            {
                var detailRelativeUrl = firstResult.Find("a.cover").Attr("href");
                if (detailRelativeUrl.IsNotEmpty())
                {
                    var detailUrl = new Uri(sampleUri, detailRelativeUrl).ToString();
                    var detailHtml = await client.GetStringAsync(detailUrl);
                    var detailCq = new CQ(detailHtml);

                    // Cover
                    var coverUrl = detailCq["img.cover"].Attr("src");
                    if (coverUrl.IsNotEmpty())
                    {
                        coverUrl = Regex.Replace(coverUrl, @"r\/\d+\/", string.Empty).AddSchemaSafely();
                        var coverData = await client.GetByteArrayAsync(coverUrl);
                        var ext = Path.GetExtension(coverUrl);
                        enhancements.Add(Enhancement.BuildReservedFile(ReservedResourceFileType.Cover,
                            new EnhancementFile {Data = coverData, RelativePath = $"cover{ext}"}));
                    }

                    // Name
                    var name = detailCq[".nameSingle>a"].Text();
                    if (name.IsNotEmpty())
                    {
                        enhancements.Add(Enhancement.BuildName(name));
                    }

                    // Tag
                    var tags = detailCq[".subject_tag_section>.inner>a>span"].Select(a => a.InnerText?.Trim())
                        .Where(a => a.IsNotEmpty()).Select(a => new TagDto
                        {
                            Name = a
                        }).ToArray();
                    if (tags.Any())
                    {
                        enhancements.Add(Enhancement.BuildTag(tags));
                    }

                    var infoBox = detailCq["#infobox>li"];
                    var infoList = infoBox.GroupBy(a => a.ChildNodes[0].Cq().Text().Trim().Trim(':')).ToDictionary(
                        a => a.Key,
                        a => a.SelectMany(b => b.ChildNodes.Skip(1).Select(b => b.Cq()).Select(x => x.Text().Trim('、'))
                            .Where(x => x.IsNotEmpty()).ToArray()).ToArray());


                    foreach (var (key, values) in infoList)
                    {
                        if (key.IsNotEmpty() && values.IsNotEmpty())
                        {
                            CustomDataType dataType;
                            var isArray = values.Length > 1;

                            var dtValues = await _specialTextService.TryToParseDateTime(values);
                            if (dtValues.Length == values.Length)
                            {
                                dataType = CustomDataType.DateTime;
                            }
                            else
                            {
                                var numberValues = values
                                    .Select(a => decimal.TryParse(a, out var n) ? n : (decimal?) null).ToArray();
                                dataType = numberValues.All(a => a.HasValue)
                                    ? CustomDataType.Number
                                    : CustomDataType.String;
                            }

                            enhancements.Add(Enhancement.BuildCustomProperty(key, dataType,
                                values.Length == 1 ? values[0] : values, isArray));
                        }
                    }

                    // Rate
                    var scoreStr = detailCq[".global_score .number"].Text().Trim();
                    if (decimal.TryParse(scoreStr, out var score))
                    {
                        enhancements.Add(Enhancement.BuildRate(score));
                    }
                }
            }

            return enhancements.ToArray();
        }
    }
}