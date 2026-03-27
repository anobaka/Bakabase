using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// Unified task that prepares cover, playable item, and metadata data for all resources.
/// Merges the former PrepareDataTask and FetchExternalMetadataTask into a single per-resource loop.
/// </summary>
public class ResourceDataTask : AbstractPredefinedBTaskBuilder
{
    public ResourceDataTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "ResourceData";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(5);

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var ct = args.CancellationToken;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ResourceDataTask>>();
        var resourceService = scope.ServiceProvider.GetRequiredService<IResourceService>();
        var coverProviderService = scope.ServiceProvider.GetRequiredService<ICoverProviderService>();
        var playableItemProviderService = scope.ServiceProvider.GetRequiredService<IPlayableItemProviderService>();
        var coverProviders = scope.ServiceProvider.GetRequiredService<IEnumerable<ICoverProvider>>().ToList();
        var playableItemProviders = scope.ServiceProvider.GetRequiredService<IEnumerable<IPlayableItemProvider>>().ToList();
        var metadataProviders = scope.ServiceProvider.GetRequiredService<IEnumerable<IMetadataProvider>>()
            .ToDictionary(p => p.Origin);
        var sourceLinkService = scope.ServiceProvider.GetRequiredService<IResourceSourceLinkService>();
        var metadataSyncService = scope.ServiceProvider.GetRequiredService<ISourceMetadataSyncService>();

        var resources = await resourceService.GetAll(
            additionalItems: ResourceAdditionalItem.Cover | ResourceAdditionalItem.PlayableItem);
        var total = resources.Count;
        var processed = 0;

        foreach (var resource in resources)
        {
            ct.ThrowIfCancellationRequested();
            await args.PauseToken.WaitWhilePausedAsync(ct);

            // Phase 1: Cover providers — resolve if any applicable provider is NotStarted or Failed
            try
            {
                var needsCoverWork = coverProviders
                    .Where(p => p.AppliesTo(resource))
                    .Any(p =>
                    {
                        var status = p.GetStatus(resource);
                        return status == DataStatus.NotStarted || status == DataStatus.Failed;
                    });

                if (needsCoverWork)
                {
                    await coverProviderService.ResolveCoversAsync(resource, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve covers for resource {ResourceId}", resource.Id);
            }

            // Phase 2: PlayableItem providers — resolve if any applicable provider is NotStarted or Failed
            try
            {
                var needsPlayableWork = playableItemProviders
                    .Where(p => p.AppliesTo(resource))
                    .Any(p =>
                    {
                        var status = p.GetStatus(resource);
                        return status == DataStatus.NotStarted || status == DataStatus.Failed;
                    });

                if (needsPlayableWork)
                {
                    await playableItemProviderService.GetPlayableItemsAsync(resource, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to resolve playable items for resource {ResourceId}", resource.Id);
            }

            // Phase 3: Metadata providers — fetch for each SourceLink without metadata
            if (resource.SourceLinks != null)
            {
                foreach (var link in resource.SourceLinks.Where(l => l.MetadataFetchedAt == null))
                {
                    var origin = link.Source switch
                    {
                        ResourceSource.Steam => DataOrigin.Steam,
                        ResourceSource.DLsite => DataOrigin.DLsite,
                        ResourceSource.ExHentai => DataOrigin.ExHentai,
                        _ => (DataOrigin?)null
                    };

                    if (origin == null || !metadataProviders.TryGetValue(origin.Value, out var metadataProvider))
                    {
                        continue;
                    }

                    try
                    {
                        var metadata = await metadataProvider.FetchMetadataAsync(link.SourceKey, ct);
                        if (metadata != null)
                        {
                            link.MetadataJson = metadata.RawJson;
                            link.MetadataFetchedAt = DateTime.Now;

                            if (metadata.CoverUrls is { Count: > 0 } && link.CoverUrls is not { Count: > 0 })
                            {
                                link.CoverUrls = metadata.CoverUrls;
                                link.CoverDownloadFailedAt = null;
                            }

                            await sourceLinkService.Update(link);
                            await metadataSyncService.SyncMetadataToProperties(link.ResourceId, link.Source, ct);
                        }
                        else
                        {
                            link.MetadataFetchedAt = DateTime.Now;
                            await sourceLinkService.Update(link);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to fetch metadata for {Source} key {Key}", link.Source, link.SourceKey);
                    }
                }
            }

            processed++;
            await args.UpdateTask(t =>
            {
                t.Percentage = total > 0 ? processed * 100 / total : 100;
                t.Process = $"{processed}/{total}";
            });
        }
    }
}
