using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Components.Enhancers.Steam;

/// <summary>
/// Enhancer that fetches game metadata from Steam Store API.
/// Uses the resource's SourceKey (AppId) to look up game details.
/// </summary>
public class SteamEnhancer(
    ILoggerFactory loggerFactory,
    SteamClient steamClient,
    IFileManager fileManager,
    IServiceProvider serviceProvider)
    : AbstractEnhancer<SteamEnhancerTarget, SteamEnhancerContext, object?>(loggerFactory, fileManager,
        serviceProvider)
{
    protected override EnhancerId TypedId => EnhancerId.Steam;

    protected override async Task<SteamEnhancerContext?> BuildContextInternal(
        Resource resource, object? options,
        EnhancementLogCollector logCollector, CancellationToken ct)
    {
        // Steam resources use SourceKey as AppId
        if (resource.Source != ResourceSource.Steam || string.IsNullOrEmpty(resource.SourceKey))
        {
            logCollector.LogWarning(EnhancementLogEvent.Configuration,
                "Resource is not a Steam resource or has no SourceKey");
            return null;
        }

        if (!int.TryParse(resource.SourceKey, out var appId))
        {
            logCollector.LogWarning(EnhancementLogEvent.Configuration,
                $"Invalid Steam AppId: {resource.SourceKey}");
            return null;
        }

        logCollector.LogInfo(EnhancementLogEvent.DataFetching,
            $"Fetching Steam app details for AppId {appId}");

        var details = await steamClient.GetAppDetails(appId, ct: ct);
        if (details == null)
        {
            logCollector.LogWarning(EnhancementLogEvent.DataFetched,
                $"No details found for AppId {appId}");
            return null;
        }

        logCollector.LogInfo(EnhancementLogEvent.DataFetched,
            $"Got details for: {details.Name}",
            new { AppId = appId, Name = details.Name });

        var ctx = new SteamEnhancerContext
        {
            Name = details.Name,
            Description = details.ShortDescription,
            Developers = details.Developers,
            Publishers = details.Publishers,
            MetacriticScore = details.Metacritic?.Score,
            ReleaseDate = SteamClient.ParseReleaseDate(details.ReleaseDate?.Date),
        };

        // Convert genres
        if (details.Genres is { Count: > 0 })
        {
            ctx.Genres = new Dictionary<string, List<string>>
            {
                ["Genre"] = details.Genres
                    .Where(g => !string.IsNullOrEmpty(g.Description))
                    .Select(g => g.Description!)
                    .ToList()
            };
        }

        // Convert categories
        if (details.Categories is { Count: > 0 })
        {
            ctx.Categories = new Dictionary<string, List<string>>
            {
                ["Category"] = details.Categories
                    .Where(c => !string.IsNullOrEmpty(c.Description))
                    .Select(c => c.Description!)
                    .ToList()
            };
        }

        // Download cover image
        var coverUrl = details.HeaderImage ?? details.CapsuleImage;
        if (!string.IsNullOrEmpty(coverUrl))
        {
            logCollector.LogInfo(EnhancementLogEvent.HttpRequest,
                "Downloading cover image",
                new { Url = coverUrl });

            var imageData = await steamClient.DownloadImage(coverUrl, ct);
            if (imageData != null)
            {
                var ext = Path.GetExtension(new Uri(coverUrl).AbsolutePath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                ctx.CoverPath = await SaveFile(resource, $"cover{ext}", imageData);

                logCollector.LogInfo(EnhancementLogEvent.FileSaved,
                    $"Cover saved: {ctx.CoverPath}");
            }
        }

        return ctx;
    }

    protected override Task<List<EnhancementTargetValue<SteamEnhancerTarget>>> ConvertContextByTargets(
        SteamEnhancerContext context, EnhancementLogCollector logCollector, CancellationToken ct)
    {
        var enhancements = new List<EnhancementTargetValue<SteamEnhancerTarget>>();

        foreach (var target in SpecificEnumUtils<SteamEnhancerTarget>.Values)
        {
            switch (target)
            {
                case SteamEnhancerTarget.Name:
                case SteamEnhancerTarget.Description:
                case SteamEnhancerTarget.Developer:
                case SteamEnhancerTarget.Publisher:
                case SteamEnhancerTarget.ReleaseDate:
                case SteamEnhancerTarget.MetacriticScore:
                case SteamEnhancerTarget.Cover:
                {
                    IStandardValueBuilder valueBuilder = target switch
                    {
                        SteamEnhancerTarget.Name => new StringValueBuilder(context.Name),
                        SteamEnhancerTarget.Description => new StringValueBuilder(context.Description),
                        SteamEnhancerTarget.Developer => new ListStringValueBuilder(context.Developers),
                        SteamEnhancerTarget.Publisher => new ListStringValueBuilder(context.Publishers),
                        SteamEnhancerTarget.ReleaseDate => new DateTimeValueBuilder(context.ReleaseDate),
                        SteamEnhancerTarget.MetacriticScore => new DecimalValueBuilder(context.MetacriticScore),
                        SteamEnhancerTarget.Cover => new ListStringValueBuilder(
                            string.IsNullOrEmpty(context.CoverPath) ? null : [context.CoverPath]),
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    if (valueBuilder.Value != null)
                    {
                        enhancements.Add(new EnhancementTargetValue<SteamEnhancerTarget>(target, null,
                            valueBuilder));
                    }

                    break;
                }
                case SteamEnhancerTarget.Genre:
                {
                    if (context.Genres != null)
                    {
                        var tags = context.Genres
                            .SelectMany(d => d.Value.Select(v => new TagValue(d.Key, v)))
                            .ToList();
                        if (tags.Count > 0)
                        {
                            enhancements.Add(new EnhancementTargetValue<SteamEnhancerTarget>(
                                SteamEnhancerTarget.Genre, null, new ListTagValueBuilder(tags)));
                        }
                    }

                    break;
                }
                case SteamEnhancerTarget.Category:
                {
                    if (context.Categories != null)
                    {
                        var tags = context.Categories
                            .SelectMany(d => d.Value.Select(v => new TagValue(d.Key, v)))
                            .ToList();
                        if (tags.Count > 0)
                        {
                            enhancements.Add(new EnhancementTargetValue<SteamEnhancerTarget>(
                                SteamEnhancerTarget.Category, null, new ListTagValueBuilder(tags)));
                        }
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return Task.FromResult(enhancements);
    }
}
