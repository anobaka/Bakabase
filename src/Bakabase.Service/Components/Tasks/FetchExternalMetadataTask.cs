using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
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
/// Uses ResourceSourceLink as the unified metadata storage and IResourceResolver for source-specific fetch logic.
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
        var resolvers = scope.ServiceProvider.GetRequiredService<IEnumerable<IResourceResolver>>()
            .Where(r => r.Source != ResourceSource.FileSystem)
            .ToDictionary(r => r.Source);
        var sourceLinkService = scope.ServiceProvider.GetRequiredService<IResourceSourceLinkService>();
        var metadataSyncService = scope.ServiceProvider.GetRequiredService<ISourceMetadataSyncService>();

        // Phase 1: Fetch metadata for source links that don't have it yet
        var pendingLinks = await sourceLinkService.GetPendingMetadataFetches();
        if (pendingLinks.Count > 0)
        {
            var processed = 0;
            await args.UpdateTask(t => t.Process = $"Metadata: 0/{pendingLinks.Count}");

            foreach (var link in pendingLinks)
            {
                args.CancellationToken.ThrowIfCancellationRequested();
                await args.PauseToken.WaitWhilePausedAsync(args.CancellationToken);

                if (!resolvers.TryGetValue(link.Source, out var resolver))
                {
                    processed++;
                    continue;
                }

                try
                {
                    var metadata = await resolver.FetchDetailedMetadataAsync(link.SourceKey, args.CancellationToken);
                    if (metadata != null)
                    {
                        // Store metadata on the source link
                        link.MetadataJson = metadata.RawJson;
                        link.MetadataFetchedAt = DateTime.Now;

                        // Update CoverUrls if available
                        if (metadata.CoverUrls is { Count: > 0 } && link.CoverUrls is not { Count: > 0 })
                        {
                            link.CoverUrls = metadata.CoverUrls;
                            link.CoverDownloadFailedAt = null;
                        }

                        await sourceLinkService.Update(link);

                        // Apply metadata mappings to resource properties
                        await metadataSyncService.SyncMetadataToProperties(
                            link.ResourceId, link.Source, args.CancellationToken);
                    }
                    else
                    {
                        // Mark as fetched even if no metadata returned (to avoid infinite retry)
                        link.MetadataFetchedAt = DateTime.Now;
                        await sourceLinkService.Update(link);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Failed to fetch {Source} metadata for key {Key}", link.Source, link.SourceKey);
                }

                processed++;
                await args.UpdateTask(t => t.Process = $"Metadata: {processed}/{pendingLinks.Count}");
            }
        }

        // Phase 2: Download covers (source links with CoverUrls but no LocalCoverPaths)
        await DownloadCovers(scope.ServiceProvider, args, logger);
    }

    private async Task DownloadCovers(IServiceProvider sp, BTaskArgs args, ILogger logger)
    {
        var sourceLinkService = sp.GetRequiredService<IResourceSourceLinkService>();
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var resourceService = sp.GetRequiredService<IResourceService>();
        var fileManager = sp.GetRequiredService<IFileManager>();

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
                var coverDir = fileManager.BuildAbsolutePath(
                    "cache", "cover", "source", $"{link.ResourceId}_{link.Source}");
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
                    link.CoverDownloadFailedAt = null;
                    await sourceLinkService.Update(link);
                    await resourceService.InvalidateResourceCovers(link.ResourceId);
                }
                else
                {
                    link.CoverDownloadFailedAt = DateTime.Now;
                    await sourceLinkService.Update(link);
                    logger.LogWarning(
                        "All cover downloads failed for resource {ResourceId}, source {Source}. Will retry after backoff.",
                        link.ResourceId, link.Source);
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
