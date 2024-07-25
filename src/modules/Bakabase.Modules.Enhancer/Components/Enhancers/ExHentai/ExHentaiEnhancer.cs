using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.ExHentai;
using Bakabase.Modules.ThirdParty.ExHentai.Models.RequestModels;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai
{
    public class ExHentaiEnhancer(IEnumerable<IStandardValueHandler> valueConverters, ILoggerFactory loggerFactory, ExHentaiClient exHentaiClient, IServiceProvider services, IOptions<ExHentaiOptions> options, ISpecialTextService specialTextService)
        : AbstractEnhancer<ExHentaiEnhancerTarget, ExHentaiEnhancerContext, object?>(valueConverters, loggerFactory)
    {
        private readonly ExHentaiClient _exHentaiClient = exHentaiClient;
        private readonly IServiceProvider _services = services;
        private readonly IOptions<ExHentaiOptions> _options = options;
        private readonly ISpecialTextService _specialTextService = specialTextService;
        private const string UrlKeywordRegex = "[a-zA-Z0-9]{10,}";

        protected override async Task<ExHentaiEnhancerContext?> BuildContext(Resource resource)
        {
            var options = _options.Value;

            var name = resource.IsFile ? Path.GetFileNameWithoutExtension(resource.FileName) : resource.FileName;
            var urlKeywords = new HashSet<string>();

            var names = new List<string> {resource.FileName}.Where(a => a.IsNotEmpty()).ToArray();
            if (names.Any(n => Regex.IsMatch(n, UrlKeywordRegex)))
            {
                var wrappers = await _specialTextService.GetAll(x => x.Type == SpecialTextType.Wrapper);
                var urlKeywordCandidates = new List<(string Str, string Keyword)>();
                foreach (var wrapper in wrappers)
                {
                    var el = Regex.Escape(wrapper.Value1);
                    var er = Regex.Escape(wrapper.Value2!);
                    foreach (var n in names)
                    {
                        var match = Regex.Match(n, $"{el}(?<k>{UrlKeywordRegex}){er}");
                        if (match.Success)
                        {
                            urlKeywordCandidates.AddRange(match.Groups["k"].Captures.Select(a => a.Value)
                                .Select(a => (Str: $"{wrapper.Value1}{a}{wrapper.Value2}", Keyword: a)));
                        }
                    }
                }

                foreach (var (str, keyword) in urlKeywordCandidates)
                {
                    name = name.Replace(str, null);
                    urlKeywords.Add(keyword);
                }
            }

            var searchRsp = await _exHentaiClient.Search(
                new ExHentaiSearchRequestModel {Keyword = name, PageIndex = 1, PageSize = 1});

            var targetUrl = searchRsp.Resources?.FirstOrDefault()?.Url;
            if (searchRsp?.Resources?.Count > 1 && urlKeywords.Any())
            {
                targetUrl = searchRsp.Resources.FirstOrDefault(a => urlKeywords.Any(b => a.Url.Contains(b)))?.Url ??
                            targetUrl;
            }

            if (targetUrl != null)
            {
                var detail = await _exHentaiClient.ParseDetail(targetUrl);
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
                        if (options.Enhancer?.ExcludedTags?.Any() == true)
                        {
                            foreach (var t in options.Enhancer.ExcludedTags.Where(t => t.IsNotEmpty()))
                            {
                                var segments = t.Split(':', StringSplitOptions.RemoveEmptyEntries);

                                if (segments.Length > 1)
                                {
                                    var group = segments[0];
                                    var tag = segments[1];

                                    var groups = group == "*" ? tagGroups.Keys.ToArray() : new[] {group};
                                    foreach (var g in groups)
                                    {
                                        if (tagGroups.TryGetValue(g, out var tags))
                                        {
                                            if (tag != "*")
                                            {
                                                tagGroups[g] = tags.Where(x => x != tag).ToList();
                                            }
                                            else
                                            {
                                                tagGroups.Remove(g);
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        ctx.Tags = tagGroups;
                    }

                    return ctx;
                }
            }

            return null;
        }

        protected override EnhancerId TypedId => EnhancerId.Bakabase;

        protected override async Task<Dictionary<ExHentaiEnhancerTarget, IStandardValueBuilder>>
            ConvertContextByTargets(ExHentaiEnhancerContext context)
        {
            var targetValues = new Dictionary<ExHentaiEnhancerTarget, IStandardValueBuilder>();
            foreach (var target in SpecificEnumUtils<ExHentaiEnhancerTarget>.Values)
            {
                var v = BuildTargetValue(context, target);
                if (v != null)
                {
                    targetValues.Add(target, v);
                }
            }

            return targetValues;
        }

        protected IStandardValueBuilder? BuildTargetValue(ExHentaiEnhancerContext data, ExHentaiEnhancerTarget target)
        {
            switch (target)
            {
                case ExHentaiEnhancerTarget.Rating:
                {
                    if (data.Rating.HasValue)
                    {
                        return new DecimalValueBuilder(data.Rating.Value);
                    }

                    break;
                }
                case ExHentaiEnhancerTarget.Tags:
                {
                    var value = data.Tags?.SelectMany(t => t.Value.Select(x => new TagValue(t.Key, x))).ToList();
                    if (value?.Count > 0)
                    {
                        return new ListTagValueBuilder(value);
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, null);
            }

            return null;
        }
    }
}