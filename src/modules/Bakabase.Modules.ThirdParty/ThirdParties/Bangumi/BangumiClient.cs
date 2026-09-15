using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.StandardValue.Models.Domain;
using CsQuery;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Bootstrap.Extensions;
using System.Net;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi.Models;
using Bakabase.Abstractions.Helpers;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

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

        var ctx = new BangumiDetail {Name = name, DetailUrl = detailUrl};

        // Introduction
        var intro = detailCq["#subject_summary"]?[0]?.InnerHTML;
        if (!string.IsNullOrEmpty(intro))
        {
            ctx.Introduction = StringHelpers.MinifyHtml(WebUtility.HtmlDecode(intro));
        }

        // Cover
        var coverUrl = detailCq["img.cover"].Attr("src");
        if (coverUrl.IsNotEmpty())
        {
            coverUrl = Regex.Replace(coverUrl, @"r\/\d+\/", string.Empty).AddSchemaSafely();
            ctx.CoverUrl = coverUrl;
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

    /// <summary>
    /// The subjects bgm.tv says are related to this one — sequels, prequels, entries in the same
    /// series, adaptations.
    /// <para>
    /// Read from the API rather than scraped: relations are the one thing Bangumi publishes as
    /// data, and it is the most reliable statement anywhere of what belongs with what.
    /// </para>
    /// </summary>
    /// <param name="subjectId">The subject the relations are of.</param>
    /// <param name="relations">
    /// Which kinds of relation to keep, by their Chinese label ("续集", "系列", …). Empty keeps all
    /// of them, which for a long-running series is usually too much.
    /// </param>
    public async Task<List<BangumiRelatedSubject>> GetRelatedSubjectsAsync(string subjectId,
        IReadOnlyCollection<string>? relations = null, CancellationToken ct = default)
    {
        var url = $"https://api.bgm.tv/v0/subjects/{Uri.EscapeDataString(subjectId)}/subjects";
        var request = new HttpRequestMessage(HttpMethod.Get, url);

        // bgm.tv asks callers to identify themselves and answers anonymous ones inconsistently.
        request.Headers.TryAddWithoutValidation("User-Agent", BangumiApiUserAgent);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = await HttpClient.SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        var related = System.Text.Json.JsonSerializer.Deserialize<List<BangumiRelatedSubject>>(json,
            BangumiApiJsonOptions) ?? [];

        return relations is {Count: > 0}
            ? related.Where(r => r.Relation != null && relations.Contains(r.Relation)).ToList()
            : related;
    }

    /// <summary>bgm.tv asks for a project identifier; this is ours.</summary>
    private const string BangumiApiUserAgent = "Bakabase/1.0 (+https://github.com/anobaka/Bakabase)";

    private static readonly System.Text.Json.JsonSerializerOptions BangumiApiJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<BangumiDetail?> SearchAndParseFirst(string keyword, string? category = null)
    {
        var searchUrl = BangumiUrlBuilder.Search(keyword, category ?? "all");
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