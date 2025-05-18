using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi;

public class BangumiEnhancer(ILoggerFactory loggerFactory, BangumiClient client, IFileManager fileManager)
    : AbstractEnhancer<BangumiEnhancerTarget, BangumiEnhancerContext, object?>(loggerFactory, fileManager)
{
    protected override async Task<BangumiEnhancerContext?> BuildContext(Resource resource, EnhancerFullOptions options,
        CancellationToken ct)
    {
        var keyword = resource.IsFile ? Path.GetFileNameWithoutExtension(resource.FileName) : resource.FileName;
        var detail = await client.SearchAndParseFirst(keyword);

        if (detail != null)
        {
            var ctx = new BangumiEnhancerContext
            {
                Name = detail.Name,
                Introduction = detail.Introduction,
                OtherPropertiesInLeftPanel = detail.OtherPropertiesInLeftPanel,
                Rating = detail.Rating,
                Tags = detail.Tags,
            };

            if (!string.IsNullOrEmpty(detail.CoverUrl))
            {
                var imageData = await client.HttpClient.GetByteArrayAsync(detail.CoverUrl, ct);
                var queryIdx = detail.CoverUrl.IndexOf('?');
                var coverUrl = queryIdx == -1 ? detail.CoverUrl : detail.CoverUrl[..queryIdx];
                ctx.CoverPath = await SaveFile(resource, $"cover{Path.GetExtension(coverUrl)}", imageData);
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
                        BangumiEnhancerTarget.Cover => new ListStringValueBuilder(
                            string.IsNullOrEmpty(context.CoverPath)
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