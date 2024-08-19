using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.ThirdParty.Bangumi.Models;
using CsQuery;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Bootstrap.Extensions;
using System.Net;

namespace Bakabase.Modules.ThirdParty.Bangumi;

public class BangumiClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : BakabaseHttpClient(httpClientFactory, loggerFactory)
{
    protected override string HttpClientName => InternalOptions.HttpClientNames.Bangumi;
    private static readonly char[] PropertyValueSeparators = ['、', '/', '(', ')', '\u3000', '（', '）', '；'];
    /// <summary>
    /// For data such as http://xxxx
    /// </summary>
    private static readonly string[] SeparatorExceptions = ["//"];

    public async Task<BangumiDetail?> ParseDetail(string detailUrl)
    {
        var detailHtml = await HttpClient.GetStringAsync(detailUrl);
        var detailCq = new CQ(detailHtml);

        // Name
        var name = detailCq[".nameSingle>a"]?.Text();
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var ctx = new BangumiDetail {Name = name};

        // Introduction
        var intro = detailCq["#subject_summary"]?[0]?.InnerHTML;
        if (!string.IsNullOrEmpty(intro))
        {
            ctx.Introduction = WebUtility.HtmlDecode(intro);
        }

        // Cover
        var coverUrl = detailCq["img.cover"].Attr("src");
        if (coverUrl.IsNotEmpty())
        {
            coverUrl = Regex.Replace(coverUrl, @"r\/\d+\/", string.Empty).AddSchemaSafely();
            ctx.CoverPath = coverUrl;
            // var coverData = await client.GetByteArrayAsync(coverUrl);
            // var ext = Path.GetExtension(coverUrl);
            // enhancements.Add(Enhancement.BuildReservedFile(ReservedResourceFileType.Cover,
            //     new EnhancementFile { Data = coverData, RelativePath = $"cover{ext}" }));
        }

        // Tag
        var tags = detailCq[".subject_tag_section>.inner>a>span"].Select(a => a.Cq().Text()?.Trim())
            .OfType<string>().Select(a => new TagValue(null, a)).ToList();
        ctx.Tags = tags;

        // Rating
        var scoreStr = detailCq[".global_score .number"].Text().Trim();
        if (decimal.TryParse(scoreStr, out var score))
        {
            ctx.Rating = score;
        }

        // other properties in left panel
        var infoBox = detailCq["#infobox>li"];
        var infoList = infoBox.GroupBy(a => a.ChildNodes[0].Cq().Text().Trim().Trim(':')).ToDictionary(
            a => a.Key,
            a => a.SelectMany(b =>
                {
                    var fullStr = string.Concat(b.ChildNodes.Skip(1).Select(x => x.Cq().Text()));
                    Dictionary<string, string>? separatorExceptionMap = null;
                    for (var i = 0; i < SeparatorExceptions.Length; i++)
                    {
                        var se = SeparatorExceptions[i];
                        if (fullStr.Contains(se))
                        {
                            separatorExceptionMap ??= new Dictionary<string, string>();
                            if (!separatorExceptionMap.TryGetValue(se, out var replacement))
                            {
                                separatorExceptionMap[se] = replacement = $"{i}{Guid.NewGuid().ToString("N")[..6]}";
                            }

                            fullStr = fullStr.Replace(se, replacement);
                        }
                    }

                    return fullStr.Split(PropertyValueSeparators, StringSplitOptions.RemoveEmptyEntries)
                        .Select(y =>
                        {
                            var r = y.Trim();
                            if (separatorExceptionMap != null)
                            {
                                foreach (var (org, replacement) in separatorExceptionMap)
                                {
                                    r = r.Replace(replacement, org);
                                }
                            }

                            return r;
                        });
                })
                .ToList());

        ctx.OtherPropertiesInLeftPanel = infoList.Where(x => !string.IsNullOrEmpty(x.Key) && x.Value.Any())
            .ToDictionary(d => d.Key, d => d.Value);

        return ctx;
    }

    public async Task<BangumiDetail?> SearchAndParseFirst(string keyword)
    {
        var searchUrl = BangumiUrlBuilder.Search(keyword);
        var searchHtml = await HttpClient.GetStringAsync(searchUrl);
        var searchCq = new CQ(searchHtml);

        var firstResult = searchCq["#browserItemList>li"].First();
        if (firstResult == null)
        {
            return null;
        }

        var detailRelativeUrl = firstResult.Find("a.cover").Attr("href");
        if (string.IsNullOrEmpty(detailRelativeUrl))
        {
            return null;
        }

        var detailUrl = new Uri(BangumiUrlBuilder.Domain, detailRelativeUrl).ToString();
        return await ParseDetail(detailUrl);
    }
}