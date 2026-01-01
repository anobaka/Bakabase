using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Bakabase.InsideWorld.Models.Configs;
using Microsoft.Extensions.Options;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai.Models.RequestModels;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai
{
    public class ExHentaiEnhancer(
        ILoggerFactory loggerFactory,
        ExHentaiClient exHentaiClient,
        IServiceProvider services,
        ISpecialTextService specialTextService,
        IFileManager fileManager,
        IStandardValueService standardValueService,
        IServiceProvider serviceProvider)
        : AbstractKeywordEnhancer<ExHentaiEnhancerTarget, ExHentaiEnhancerContext, object?>(loggerFactory, fileManager, standardValueService, specialTextService, serviceProvider)
    {
        private readonly ExHentaiClient _exHentaiClient = exHentaiClient;
        private readonly IServiceProvider _services = services;
        private readonly ISpecialTextService _specialTextService = specialTextService;
        private const string UrlKeywordRegex = "[a-zA-Z0-9]{10,}";

        protected override async Task<ExHentaiEnhancerContext?> BuildContextInternal(string keyword, Resource resource, EnhancerFullOptions options, EnhancementLogCollector logCollector, CancellationToken ct)
        {
            var name = keyword;
            var urlKeywords = new HashSet<string>();

            var names = new List<string> {resource.FileName}.Where(a => a.IsNotEmpty()).ToArray();
            if (names.Any(n => System.Text.RegularExpressions.Regex.IsMatch(n, UrlKeywordRegex)))
            {
                var wrappers = await _specialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper);
                var urlKeywordCandidates = new List<(string Str, string Keyword)>();
                foreach (var wrapper in wrappers)
                {
                    var el = System.Text.RegularExpressions.Regex.Escape(wrapper.Value1);
                    var er = System.Text.RegularExpressions.Regex.Escape(wrapper.Value2!);
                    foreach (var n in names)
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(n, $"{el}(?<k>{UrlKeywordRegex}){er}");
                        if (match.Success)
                        {
                            urlKeywordCandidates.AddRange(match.Groups["k"].Captures.Select(a => a.Value)
                                .Select(a => (Str: $"{wrapper.Value1}{a}{wrapper.Value2}", Keyword: a)));
                        }
                    }
                }

                foreach (var (str, urlKeyword) in urlKeywordCandidates)
                {
                    name = name.Replace(str, null);
                    urlKeywords.Add(urlKeyword);
                }

                if (urlKeywords.Any())
                {
                    logCollector.LogInfo(EnhancementLogEvent.KeywordResolved,
                        $"Found URL keywords in filename",
                        new { UrlKeywords = urlKeywords.ToList(), ProcessedName = name });
                }
            }

            var searchUrl = $"{ExHentaiClient.Domain}?f_search={System.Net.WebUtility.UrlEncode(name)}";
            logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                $"Searching ExHentai",
                new { Url = searchUrl, Keyword = name });

            var searchRsp = await _exHentaiClient.Search(
                new ExHentaiSearchRequestModel {Keyword = name, PageIndex = 1, PageSize = 1});

            logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                $"Search returned {searchRsp?.Resources?.Count ?? 0} results",
                new { Url = searchUrl, ResultCount = searchRsp?.Resources?.Count ?? 0 });

            var targetUrl = searchRsp.Resources?.FirstOrDefault()?.Url;
            if (searchRsp?.Resources?.Count > 1 && urlKeywords.Any())
            {
                targetUrl = searchRsp.Resources.FirstOrDefault(a => urlKeywords.Any(b => a.Url.Contains(b)))?.Url ??
                            targetUrl;
            }

            if (targetUrl != null)
            {
                logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                    $"Fetching detail page",
                    new { Url = targetUrl });

                var detail = await _exHentaiClient.ParseDetail(targetUrl, false);

                logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                    detail != null ? $"Got detail with {detail.Tags?.Count ?? 0} tag groups" : "Failed to parse detail",
                    new { Url = targetUrl, Found = detail != null, TagGroupCount = detail?.Tags?.Count ?? 0 });

                if (detail != null)
                {

                    var ctx = new ExHentaiEnhancerContext
                    {
                        Introduction = detail.Introduction,
                        Rating = detail.Rate > 0 ? detail.Rate : null
                    };

                    if (detail.Tags.IsNotEmpty())
                    {
                        var tagGroups = detail.Tags.ToDictionary(t => t.Key, t => t.Value.ToList());

                        ctx.Tags = tagGroups;
                    }

                    if (!string.IsNullOrEmpty(detail.CoverUrl))
                    {
                        logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                            "Downloading cover image",
                            new { Url = detail.CoverUrl });

                        var imageData = await _exHentaiClient.HttpClient.GetByteArrayAsync(detail.CoverUrl, ct);

                        logCollector.LogInfo(EnhancementLogEvent.HttpResponse,
                            $"Cover image downloaded ({imageData.Length} bytes)",
                            new { Url = detail.CoverUrl, Size = imageData.Length });

                        var queryIdx = detail.CoverUrl.IndexOf('?');
                        var coverUrl = queryIdx == -1 ? detail.CoverUrl : detail.CoverUrl[..queryIdx];
                        ctx.CoverPath = await SaveFile(resource, $"cover{Path.GetExtension(coverUrl)}", imageData);
                        logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                            $"Cover saved: {ctx.CoverPath}",
                            new { CoverPath = ctx.CoverPath });
                    }

                    return ctx;
                }
            }

            return null;
        }

        protected override EnhancerId TypedId => EnhancerId.ExHentai;

        protected override async Task<List<EnhancementTargetValue<ExHentaiEnhancerTarget>>> ConvertContextByTargets(
            ExHentaiEnhancerContext context, EnhancementLogCollector logCollector, CancellationToken ct)
        {
            var enhancements = new List<EnhancementTargetValue<ExHentaiEnhancerTarget>>();
            foreach (var target in SpecificEnumUtils<ExHentaiEnhancerTarget>.Values)
            {
                IStandardValueBuilder valueBuilder = target switch
                {
                    ExHentaiEnhancerTarget.Name => new StringValueBuilder(context.Name),
                    ExHentaiEnhancerTarget.Introduction => new StringValueBuilder(context.Introduction),
                    ExHentaiEnhancerTarget.Rating => new DecimalValueBuilder(context.Rating),
                    ExHentaiEnhancerTarget.Tags => new ListTagValueBuilder(context.Tags
                        ?.SelectMany(d => d.Value.Select(x => new TagValue(d.Key, x))).ToList()),
                    ExHentaiEnhancerTarget.Cover => new ListStringValueBuilder(string.IsNullOrEmpty(context.CoverPath)
                        ? null
                        : [context.CoverPath]),
                    _ => throw new ArgumentOutOfRangeException()
                };

                if (valueBuilder.Value != null)
                {
                    enhancements.Add(new EnhancementTargetValue<ExHentaiEnhancerTarget>(target, null, valueBuilder));
                }
            }

            return enhancements;
        }
    }
}