using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Extensions;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;

public class DLsiteEnhancer : AbstractKeywordEnhancer<DLsiteEnhancerTarget, DLsiteEnhancerContext, object?>
{
    private readonly DLsiteClient _client;
    private readonly IStandardValueService _standardValueService;

    /// <summary>
    /// BJ/VJ/RJ
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex IdRegex = new System.Text.RegularExpressions.Regex(@"[BVR]J\d{6,10}");

    public DLsiteEnhancer(ILoggerFactory loggerFactory, IFileManager fileManager, DLsiteClient client, IStandardValueService standardValueService, ISpecialTextService specialTextService, IServiceProvider serviceProvider) : base(loggerFactory, fileManager, standardValueService, specialTextService, serviceProvider)
    {
        _client = client;
        _standardValueService = standardValueService;
    }

    protected override async Task<DLsiteEnhancerContext?> BuildContextInternal(string keyword, Resource resource, EnhancerFullOptions options,
        CancellationToken ct)
    {
        var match = IdRegex.Match(keyword);
        if (!match.Success)
        {
            return null;
        }

        var id = match.Value;
        var detail = await _client.ParseWorkDetailById(id);
        if (detail == null)
        {
            return null;
        }

        var ctx = new DLsiteEnhancerContext
        {
            Introduction = detail.Introduction,
            Name = detail.Name,
            PropertiesOnTheRightSideOfCover = detail.PropertiesOnTheRightSideOfCover,
            Rating = detail.Rating
        };

        if (detail.CoverUrls?.Any() == true)
        {
            var coverPaths = new List<string>();
            for (var index = 0; index < detail.CoverUrls.Length; index++)
            {
                var coverUrl = detail.CoverUrls[index];
                var imageData = await _client.HttpClient.GetByteArrayAsync(coverUrl, ct);
                var queryIdx = coverUrl.IndexOf('?');
                var coverUrlWithoutQuery = queryIdx == -1 ? coverUrl : coverUrl[..queryIdx];
                var filenameInUrl = Path.GetFileName(coverUrlWithoutQuery).RemoveInvalidFileNameChars();
                var coverPath = await SaveFile(resource, $"cover.{index}.{filenameInUrl}", imageData);
                coverPaths.Add(coverPath);
            }

            ctx.CoverPaths = coverPaths.ToArray();
        }

        return ctx;
    }

    protected override EnhancerId TypedId => EnhancerId.DLsite;

    protected override async Task<List<EnhancementTargetValue<DLsiteEnhancerTarget>>> ConvertContextByTargets(
        DLsiteEnhancerContext context, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<DLsiteEnhancerTarget>>();
        foreach (var target in SpecificEnumUtils<DLsiteEnhancerTarget>.Values)
        {
            IStandardValueBuilder? valueBuilder = null;
            switch (target)
            {
                case DLsiteEnhancerTarget.Name:
                    valueBuilder = new StringValueBuilder(context.Name);
                    break;
                case DLsiteEnhancerTarget.Introduction:
                    valueBuilder = new StringValueBuilder(context.Introduction);
                    break;
                case DLsiteEnhancerTarget.Rating:
                    valueBuilder = new DecimalValueBuilder(context.Rating);
                    break;
                case DLsiteEnhancerTarget.PropertiesOnTheRightSideOfCover:
                {
                    if (context.PropertiesOnTheRightSideOfCover != null)
                    {
                        foreach (var (key, values) in context.PropertiesOnTheRightSideOfCover)
                        {
                            enhancements.Add(new EnhancementTargetValue<DLsiteEnhancerTarget>(
                                DLsiteEnhancerTarget.PropertiesOnTheRightSideOfCover, key,
                                new ListStringValueBuilder(values)));
                        }
                    }

                    break;
                }
                case DLsiteEnhancerTarget.Cover:
                    valueBuilder = new ListStringValueBuilder(context.CoverPaths?.ToList());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (valueBuilder?.Value != null)
            {
                enhancements.Add(new EnhancementTargetValue<DLsiteEnhancerTarget>(target, null, valueBuilder));
            }
        }

        return enhancements;
    }
}