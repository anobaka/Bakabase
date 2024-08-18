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

public class BangumiClient : BakabaseHttpClient
{
    public BangumiClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) : base(httpClientFactory,
        loggerFactory)
    {
    }

    protected override string HttpClientName => InternalOptions.HttpClientNames.Bangumi;
    private static char[] PropertyValueSeparators = ['、', '/', '(', ')', '\u3000', '（', '）', '；'];

    public async Task<BangumiDetail?> ParseDetail(string detailUrl)
    {
        var detailHtml = await HttpClient.GetStringAsync(detailUrl);
        var detailCq = new CQ(detailHtml);
        var ctx = new BangumiDetail();

        // Name
        ctx.Name = detailCq[".nameSingle>a"]?.Text();
        if (string.IsNullOrEmpty(ctx.Name))
        {
            return null;
        }

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
                    string.Concat(b.ChildNodes.Skip(1).Select(x => x.Cq().Text()))
                        .Split(PropertyValueSeparators, StringSplitOptions.RemoveEmptyEntries).Select(y => y.Trim()))
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