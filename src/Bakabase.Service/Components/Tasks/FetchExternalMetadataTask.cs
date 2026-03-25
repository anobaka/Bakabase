using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.ThirdParty.ThirdParties.DLsite;
using Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;
using Bakabase.Modules.ThirdParty.ThirdParties.Steam;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// Fetches detailed metadata from external sources (Steam, DLsite, ExHentai) for resources
/// that don't have metadata yet, downloads covers, and applies metadata mappings to properties.
/// Replaces DownloadExternalCoversTask.
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

        // Phase 1: Fetch metadata for items that need it
        await FetchMetadata(scope.ServiceProvider, args, logger);

        // Phase 2: Download covers (same logic as old DownloadExternalCoversTask)
        await DownloadCovers(scope.ServiceProvider, args, logger);
    }

    private async Task FetchMetadata(IServiceProvider sp, BTaskArgs args, ILogger logger)
    {
        var steamAppService = sp.GetRequiredService<ISteamAppService>();
        var dlsiteWorkService = sp.GetRequiredService<IDLsiteWorkService>();
        var exHentaiGalleryService = sp.GetRequiredService<IExHentaiGalleryService>();
        var metadataSyncService = sp.GetRequiredService<ISourceMetadataSyncService>();
        var sourceLinkService = sp.GetRequiredService<IResourceSourceLinkService>();

        // Steam: fetch app details for items missing metadata
        var steamApps = (await steamAppService.GetAll())
            .Where(a => a.ResourceId.HasValue && a.MetadataFetchedAt == null)
            .ToList();

        if (steamApps.Count > 0)
        {
            var steamClient = sp.GetRequiredService<SteamClient>();
            var processed = 0;

            await args.UpdateTask(t => t.Process = $"Steam: 0/{steamApps.Count}");

            foreach (var app in steamApps)
            {
                args.CancellationToken.ThrowIfCancellationRequested();
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);

                try
                {
                    var detail = await steamClient.GetAppDetails(app.AppId, ct: args.CancellationToken);
                    if (detail != null)
                    {
                        app.MetadataJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web);
                        app.MetadataFetchedAt = DateTime.Now;
                        await steamAppService.AddOrUpdate(app);

                        // Apply metadata mappings
                        await metadataSyncService.SyncMetadataToProperties(
                            app.ResourceId!.Value, ResourceSource.Steam, args.CancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch Steam metadata for AppId {AppId}", app.AppId);
                }

                processed++;
                await args.UpdateTask(t => t.Process = $"Steam: {processed}/{steamApps.Count}");
            }
        }

        // DLsite: fetch work details for items missing metadata
        var dlsiteWorks = (await dlsiteWorkService.GetAll())
            .Where(w => w.ResourceId.HasValue && w.MetadataFetchedAt == null)
            .ToList();

        if (dlsiteWorks.Count > 0)
        {
            var dlsiteClient = sp.GetRequiredService<DLsiteClient>();
            var processed = 0;

            await args.UpdateTask(t => t.Process = $"DLsite: 0/{dlsiteWorks.Count}");

            foreach (var work in dlsiteWorks)
            {
                args.CancellationToken.ThrowIfCancellationRequested();
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);

                try
                {
                    var detail = await dlsiteClient.ParseWorkDetailById(work.WorkId);
                    if (detail != null)
                    {
                        work.MetadataJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web);
                        work.MetadataFetchedAt = DateTime.Now;
                        await dlsiteWorkService.AddOrUpdate(work);

                        // Update CoverUrls on the source link if detail has covers
                        if (detail.CoverUrls is { Length: > 0 })
                        {
                            var links = await sourceLinkService.GetByResourceId(work.ResourceId!.Value);
                            var dlsiteLink = links.FirstOrDefault(l =>
                                l.Source == ResourceSource.DLsite && l.SourceKey == work.WorkId);
                            if (dlsiteLink != null && dlsiteLink.CoverUrls is not { Count: > 0 })
                            {
                                dlsiteLink.CoverUrls = detail.CoverUrls.ToList();
                                await sourceLinkService.Update(dlsiteLink);
                            }
                        }

                        // Apply metadata mappings
                        await metadataSyncService.SyncMetadataToProperties(
                            work.ResourceId!.Value, ResourceSource.DLsite, args.CancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch DLsite metadata for WorkId {WorkId}", work.WorkId);
                }

                processed++;
                await args.UpdateTask(t => t.Process = $"DLsite: {processed}/{dlsiteWorks.Count}");
            }
        }

        // ExHentai: fetch gallery details for items missing metadata
        var galleries = (await exHentaiGalleryService.GetAll())
            .Where(g => g.ResourceId.HasValue && g.MetadataFetchedAt == null)
            .ToList();

        if (galleries.Count > 0)
        {
            var exHentaiClient = sp.GetRequiredService<ExHentaiClient>();
            var processed = 0;

            await args.UpdateTask(t => t.Process = $"ExHentai: 0/{galleries.Count}");

            foreach (var gallery in galleries)
            {
                args.CancellationToken.ThrowIfCancellationRequested();
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);

                try
                {
                    var url = $"https://exhentai.org/g/{gallery.GalleryId}/{gallery.GalleryToken}/";
                    var detail = await exHentaiClient.ParseDetail(url, false);
                    if (detail != null)
                    {
                        gallery.MetadataJson = JsonSerializer.Serialize(detail, JsonSerializerOptions.Web);
                        gallery.MetadataFetchedAt = DateTime.Now;
                        await exHentaiGalleryService.AddOrUpdate(gallery);

                        // Update CoverUrl on source link
                        if (!string.IsNullOrEmpty(detail.CoverUrl))
                        {
                            var links = await sourceLinkService.GetByResourceId(gallery.ResourceId!.Value);
                            var ehLink = links.FirstOrDefault(l =>
                                l.Source == ResourceSource.ExHentai);
                            if (ehLink != null && ehLink.CoverUrls is not { Count: > 0 })
                            {
                                ehLink.CoverUrls = [detail.CoverUrl];
                                await sourceLinkService.Update(ehLink);
                            }
                        }

                        // Apply metadata mappings
                        await metadataSyncService.SyncMetadataToProperties(
                            gallery.ResourceId!.Value, ResourceSource.ExHentai, args.CancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to fetch ExHentai metadata for Gallery {GalleryId}", gallery.GalleryId);
                }

                processed++;
                await args.UpdateTask(t => t.Process = $"ExHentai: {processed}/{galleries.Count}");
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
                            "Failed to download cover from {Url} for resource {ResourceId}, source {Source}",
                            url, link.ResourceId, link.Source);
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
                    "Error processing cover download for resource {ResourceId}, source {Source}",
                    link.ResourceId, link.Source);
            }

            processed++;
            await args.UpdateTask(t =>
            {
                t.Process = $"Covers: {processed}/{pendingLinks.Count}";
            });
        }
    }
}
