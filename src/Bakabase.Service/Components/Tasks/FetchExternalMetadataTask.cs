using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// Fetches detailed metadata from external sources for resources that don't have it yet,
/// downloads covers, and applies metadata mappings to properties.
/// Uses IResourceResolver.FetchDetailedMetadataAsync for source-specific logic.
/// </summary>
public class FetchExternalMetadataTask : AbstractPredefinedBTaskBuilder
{
    public FetchExternalMetadataTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "FetchExternalMetadata";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(5);

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<FetchExternalMetadataTask>>();
        var resolvers = scope.ServiceProvider.GetRequiredService<IEnumerable<IResourceResolver>>();
        var sourceLinkService = scope.ServiceProvider.GetRequiredService<IResourceSourceLinkService>();
        var metadataSyncService = scope.ServiceProvider.GetRequiredService<ISourceMetadataSyncService>();

        // Phase 1: Fetch metadata for each non-FileSystem source via its resolver
        foreach (var resolver in resolvers.Where(r => r.Source != ResourceSource.FileSystem))
        {
            args.CancellationToken.ThrowIfCancellationRequested();

            var items = await GetItemsNeedingMetadata(scope.ServiceProvider, resolver.Source);
            if (items.Count == 0) continue;

            var sourceName = resolver.Source.ToString();
            var processed = 0;
            await args.UpdateTask(t => t.Process = $"{sourceName}: 0/{items.Count}");

            foreach (var (resourceId, sourceKey) in items)
            {
                args.CancellationToken.ThrowIfCancellationRequested();
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);

                try
                {
                    var metadata = await resolver.FetchDetailedMetadataAsync(sourceKey, args.CancellationToken);
                    if (metadata != null)
                    {
                        // Store raw JSON in source DbModel
                        await StoreMetadataJson(scope.ServiceProvider, resolver.Source, sourceKey, metadata.RawJson);

                        // Update CoverUrls on source link
                        if (metadata.CoverUrls is { Count: > 0 })
                        {
                            var links = await sourceLinkService.GetByResourceId(resourceId);
                            var link = links.FirstOrDefault(l =>
                                l.Source == resolver.Source && l.SourceKey == sourceKey);
                            if (link != null && link.CoverUrls is not { Count: > 0 })
                            {
                                link.CoverUrls = metadata.CoverUrls;
                                await sourceLinkService.Update(link);
                            }
                        }

                        // Apply metadata mappings to resource properties
                        await metadataSyncService.SyncMetadataToProperties(
                            resourceId, resolver.Source, args.CancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to fetch {Source} metadata for key {Key}", sourceName, sourceKey);
                }

                processed++;
                await args.UpdateTask(t => t.Process = $"{sourceName}: {processed}/{items.Count}");
            }
        }

        // Phase 2: Download covers (source links with CoverUrls but no LocalCoverPaths)
        await DownloadCovers(scope.ServiceProvider, args, logger);
    }

    /// <summary>
    /// Gets items that need metadata fetch (have ResourceId but no MetadataFetchedAt).
    /// Returns (ResourceId, SourceKey) pairs.
    /// </summary>
    private async Task<List<(int ResourceId, string SourceKey)>> GetItemsNeedingMetadata(
        IServiceProvider sp, ResourceSource source)
    {
        return source switch
        {
            ResourceSource.Steam => (await sp.GetRequiredService<ISteamAppService>().GetAll())
                .Where(a => a.ResourceId.HasValue && a.MetadataFetchedAt == null)
                .Select(a => (a.ResourceId!.Value, a.AppId.ToString()))
                .ToList(),
            ResourceSource.DLsite => (await sp.GetRequiredService<IDLsiteWorkService>().GetAll())
                .Where(w => w.ResourceId.HasValue && w.MetadataFetchedAt == null)
                .Select(w => (w.ResourceId!.Value, w.WorkId))
                .ToList(),
            ResourceSource.ExHentai => (await sp.GetRequiredService<IExHentaiGalleryService>().GetAll())
                .Where(g => g.ResourceId.HasValue && g.MetadataFetchedAt == null)
                .Select(g => (g.ResourceId!.Value, $"{g.GalleryId}/{g.GalleryToken}"))
                .ToList(),
            _ => []
        };
    }

    /// <summary>
    /// Stores the raw metadata JSON and sets MetadataFetchedAt on the source DbModel.
    /// </summary>
    private async Task StoreMetadataJson(IServiceProvider sp, ResourceSource source, string sourceKey, string? json)
    {
        if (string.IsNullOrEmpty(json)) return;

        switch (source)
        {
            case ResourceSource.Steam:
            {
                var svc = sp.GetRequiredService<ISteamAppService>();
                if (int.TryParse(sourceKey, out var appId))
                {
                    var app = await svc.GetByAppId(appId);
                    if (app != null)
                    {
                        app.MetadataJson = json;
                        app.MetadataFetchedAt = DateTime.Now;
                        await svc.AddOrUpdate(app);
                    }
                }

                break;
            }
            case ResourceSource.DLsite:
            {
                var svc = sp.GetRequiredService<IDLsiteWorkService>();
                var work = await svc.GetByWorkId(sourceKey);
                if (work != null)
                {
                    work.MetadataJson = json;
                    work.MetadataFetchedAt = DateTime.Now;
                    await svc.AddOrUpdate(work);
                }

                break;
            }
            case ResourceSource.ExHentai:
            {
                var svc = sp.GetRequiredService<IExHentaiGalleryService>();
                var parts = sourceKey.Split('/');
                if (parts.Length == 2 && long.TryParse(parts[0], out var galleryId))
                {
                    var gallery = await svc.GetByGalleryId(galleryId, parts[1]);
                    if (gallery != null)
                    {
                        gallery.MetadataJson = json;
                        gallery.MetadataFetchedAt = DateTime.Now;
                        await svc.AddOrUpdate(gallery);
                    }
                }

                break;
            }
        }
    }

    private async Task DownloadCovers(IServiceProvider sp, BTaskArgs args, ILogger logger)
    {
        var sourceLinkService = sp.GetRequiredService<IResourceSourceLinkService>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var resourceService = sp.GetRequiredService<IResourceService>();

        var pendingLinks = await sourceLinkService.GetPendingCoverDownloads();
        if (pendingLinks.Count == 0) return;

        var processed = 0;
        using var httpClient = httpClientFactory.CreateClient();

        await args.UpdateTask(t => t.Process = $"Covers: 0/{pendingLinks.Count}");

        foreach (var link in pendingLinks)
        {
            args.CancellationToken.ThrowIfCancellationRequested();
            await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);

            try
            {
                var localPaths = new List<string>();
                var coverDir = Path.Combine(
                    AppContext.BaseDirectory, "cache", "cover", "source",
                    $"{link.ResourceId}_{link.Source}");
                Directory.CreateDirectory(coverDir);

                for (var i = 0; i < link.CoverUrls!.Count; i++)
                {
                    var url = link.CoverUrls[i];
                    try
                    {
                        var imageData = await httpClient.GetByteArrayAsync(url, args.CancellationToken);
                        var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                        var localPath = Path.Combine(coverDir, $"{i}{ext}");
                        await File.WriteAllBytesAsync(localPath, imageData, args.CancellationToken);
                        localPaths.Add(localPath);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to download cover from {Url} for resource {ResourceId}", url, link.ResourceId);
                    }
                }

                if (localPaths.Count > 0)
                {
                    link.LocalCoverPaths = localPaths;
                    await sourceLinkService.Update(link);
                    await resourceService.InvalidateResourceCovers(link.ResourceId);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Error downloading covers for resource {ResourceId}, source {Source}",
                    link.ResourceId, link.Source);
            }

            processed++;
            await args.UpdateTask(t => t.Process = $"Covers: {processed}/{pendingLinks.Count}");
        }
    }
}
