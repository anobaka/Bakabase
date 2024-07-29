using System.Net;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components.Enhancers.Bakabase;
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
    BangumiClient client)
    : AbstractEnhancer<BangumiEnhancerTarget, BangumiEnhancerContext, object?>(valueConverters, loggerFactory)
{
    protected override async Task<BangumiEnhancerContext?> BuildContext(Resource resource)
    {
        var keyword = resource.IsFile ? Path.GetFileNameWithoutExtension(resource.FileName) : resource.FileName;
        var detail = await client.SearchAndParseFirst(keyword);

        if (detail != null)
        {
            return new BangumiEnhancerContext
            {
                Name = detail.Name,
                CoverUrl = detail.CoverUrl,
                Introduction = detail.Introduction,
                OtherPropertiesInLeftPanel = detail.OtherPropertiesInLeftPanel,
                Rating = detail.Rating,
                Tags = detail.Tags
            };
        }

        return null;
    }

    protected override EnhancerId TypedId => EnhancerId.Bangumi;

    protected override async Task<List<EnhancementTargetValue<BangumiEnhancerTarget>>> ConvertContextByTargets(
        BangumiEnhancerContext context)
    {
        var enhancements = new List<EnhancementTargetValue<BangumiEnhancerTarget>>();
        foreach (var target in SpecificEnumUtils<BangumiEnhancerTarget>.Values)
        {
            if (target == BangumiEnhancerTarget.OtherPropertiesInLeftPanel)
            {
                if (context.OtherPropertiesInLeftPanel != null)
                {
                    foreach (var (key, values) in context.OtherPropertiesInLeftPanel)
                    {
                        enhancements.Add(new EnhancementTargetValue<BangumiEnhancerTarget>(
                            BangumiEnhancerTarget.OtherPropertiesInLeftPanel, key, new ListStringValueBuilder(values)));
                    }
                }
            }
            else
            {
                IStandardValueBuilder valueBuilder = target switch
                {
                    BangumiEnhancerTarget.Name => new StringValueBuilder(context.Name),
                    BangumiEnhancerTarget.Rating => new DecimalValueBuilder(context.Rating),
                    BangumiEnhancerTarget.Tag => new ListTagValueBuilder(context.Tags),
                    BangumiEnhancerTarget.Introduction => new StringValueBuilder(context.Introduction),
                    BangumiEnhancerTarget.OtherPropertiesInLeftPanel => throw new ArgumentOutOfRangeException(),
                    _ => throw new ArgumentOutOfRangeException()
                };

                if (valueBuilder.Value != null)
                {
                    enhancements.Add(new EnhancementTargetValue<BangumiEnhancerTarget>(target, null, valueBuilder));
                }
            }
        }

        return enhancements;
    }
}