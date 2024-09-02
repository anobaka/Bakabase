using System.Text.RegularExpressions;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.DLsite;

public class DLsiteEnhancer : AbstractEnhancer<DLsiteEnhancerTarget, DLsiteEnhancerContext, object?>
{
    private readonly DLsiteClient _client;

    /// <summary>
    /// BJ/VJ/RJ
    /// </summary>
    private static readonly Regex IdRegex = new Regex(@"[BVR]J\d{6,10}");

    public DLsiteEnhancer(IEnumerable<IStandardValueHandler> valueConverters, ILoggerFactory loggerFactory,
        IFileManager fileManager, DLsiteClient client) : base(valueConverters, loggerFactory, fileManager)
    {
        _client = client;
    }

    protected override async Task<DLsiteEnhancerContext?> BuildContext(Resource resource, EnhancerFullOptions options,
        CancellationToken ct)
    {
        var match = IdRegex.Match(resource.FileName);
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
                var coverPath = await SaveFile(resource.Id, $"cover.{index}.{filenameInUrl}", imageData);
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