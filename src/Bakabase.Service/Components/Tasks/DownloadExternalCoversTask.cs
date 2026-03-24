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

public class DownloadExternalCoversTask : AbstractPredefinedBTaskBuilder
{
    public DownloadExternalCoversTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "DownloadExternalCovers";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(5);

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var sourceLinkService = scope.ServiceProvider.GetRequiredService<IResourceSourceLinkService>();
        var httpClientFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DownloadExternalCoversTask>>();
        var resourceService = scope.ServiceProvider.GetRequiredService<IResourceService>();

        // Find all source links with CoverUrls but no LocalCoverPaths
        var pendingLinks = await sourceLinkService.GetPendingCoverDownloads();
        if (pendingLinks.Count == 0) return;

        var processed = 0;
        using var httpClient = httpClientFactory.CreateClient();

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
                    // Trigger resource covers refresh
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
                t.Percentage = processed * 100 / pendingLinks.Count;
                t.Process = $"{processed}/{pendingLinks.Count}";
            });
        }
    }
}
