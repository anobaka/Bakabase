using System.Net;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;
using Bakabase.Modules.Enhancer.Components.Enhancers.ExHentai;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.ThirdParty.Bangumi;
using Bootstrap.Extensions;
using CsQuery;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;

public class BangumiEnhancer(
    IEnumerable<IStandardValueHandler> valueConverters,
    ILoggerFactory loggerFactory,
    ISpecialTextService specialTextService,
    BangumiClient client,
    IFileManager fileManager)
    : AbstractEnhancer<BangumiEnhancerTarget, BangumiEnhancerContext, object?>(valueConverters, loggerFactory, fileManager)
{
    protected override async Task<BangumiEnhancerContext?> BuildContext(Resource resource, EnhancerFullOptions options, CancellationToken ct)
    {
        var keyword = resource.IsFile ? Path.GetFileNameWithoutExtension(resource.FileName) : resource.FileName;
        var detail = await client.SearchAndParseFirst(keyword);

        if (detail != null)
        {
            var ctx= new BangumiEnhancerContext
            {
                Name = detail.Name,
                Introduction = detail.Introduction,
                OtherPropertiesInLeftPanel = detail.OtherPropertiesInLeftPanel,
                Rating = detail.Rating,
                Tags = detail.Tags,
            };

            if (!string.IsNullOrEmpty(detail.CoverPath))
            {
                var imageData = await client.HttpClient.GetByteArrayAsync(detail.CoverPath, ct);
                var queryIdx = detail.CoverPath.IndexOf('?');
                var coverUrl = queryIdx == -1 ? detail.CoverPath : detail.CoverPath[..queryIdx];
                ctx.CoverPath = await SaveFile(resource.Id, $"cover{Path.GetExtension(coverUrl)}", imageData);
            }

            return ctx;
        }

        return null;
    }

    protected override EnhancerId TypedId => EnhancerId.Bangumi;

    protected override async Task<List<EnhancementTargetValue<BangumiEnhancerTarget>>> ConvertContextByTargets(
        BangumiEnhancerContext context, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<BangumiEnhancerTarget>>();
        foreach (var target in SpecificEnumUtils<BangumiEnhancerTarget>.Values)
        {
            switch (target)
            {
                case BangumiEnhancerTarget.Name:
                case BangumiEnhancerTarget.Tags:
                case BangumiEnhancerTarget.Introduction:
                case BangumiEnhancerTarget.Rating:
                case BangumiEnhancerTarget.Cover:
                {
                    IStandardValueBuilder valueBuilder = target switch
                    {
                        BangumiEnhancerTarget.Name => new StringValueBuilder(context.Name),
                        BangumiEnhancerTarget.Rating => new DecimalValueBuilder(context.Rating),
                        BangumiEnhancerTarget.Tags => new ListTagValueBuilder(context.Tags),
                        BangumiEnhancerTarget.Introduction => new StringValueBuilder(context.Introduction),
                        BangumiEnhancerTarget.OtherPropertiesInLeftPanel => throw new ArgumentOutOfRangeException(),
                        BangumiEnhancerTarget.Cover => new ListStringValueBuilder(string.IsNullOrEmpty(context.CoverPath)
                            ? null
                            : [context.CoverPath]),

                        _ => throw new ArgumentOutOfRangeException()
                    };

                    if (valueBuilder.Value != null)
                    {
                        enhancements.Add(new EnhancementTargetValue<BangumiEnhancerTarget>(target, null, valueBuilder));
                    }

                    break;
                }
                case BangumiEnhancerTarget.OtherPropertiesInLeftPanel:
                {
                    if (context.OtherPropertiesInLeftPanel != null)
                    {
                        foreach (var (key, values) in context.OtherPropertiesInLeftPanel)
                        {
                            enhancements.Add(new EnhancementTargetValue<BangumiEnhancerTarget>(
                                BangumiEnhancerTarget.OtherPropertiesInLeftPanel, key,
                                new ListStringValueBuilder(values)));
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return enhancements;
    }
}