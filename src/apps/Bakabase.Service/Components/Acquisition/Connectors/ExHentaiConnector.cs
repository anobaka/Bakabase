using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Connectors;

/// <summary>
/// ExHentai, as a place the user has galleries on.
/// <para>
/// Fetching hands the gallery to the downloader that already exists for it — with its rate
/// limiting, its retries and its queue — and then waits. Reimplementing any of that inside an
/// acquisition step would be a second downloader to keep working.
/// </para>
/// </summary>
public class ExHentaiConnector(
    IExHentaiGalleryService galleries,
    ExHentaiDownloaderHelper helper,
    DownloadTaskService downloads,
    ILogger<ExHentaiConnector> logger) : IPlatformConnector
{
    public ResourceSource Source => ResourceSource.ExHentai;

    public bool CanFetch => true;

    public async Task<IReadOnlyList<PlatformHolding>> EnumerateHoldingsAsync(CancellationToken ct) =>
        (await galleries.GetAll())
        .Select(g => new PlatformHolding(
            SourceKeyOf(g.GalleryId, g.GalleryToken),
            g.Title ?? g.TitleJpn ?? SourceKeyOf(g.GalleryId, g.GalleryToken),
            g.LocalPath,
            g.CoverUrl == null ? null : [g.CoverUrl]))
        .ToList();

    public async Task<PlatformFetchOutcome> FetchAsync(string sourceKey, string workDirectory,
        Func<int, string?, Task>? onProgress, CancellationToken ct)
    {
        var gallery = await Find(sourceKey);

        if (gallery == null)
        {
            return new PlatformFetchOutcome.Refused(
                $"{sourceKey} is not in your ExHentai galleries. Sync them first.");
        }

        if (!string.IsNullOrEmpty(gallery.LocalPath) && System.IO.Directory.Exists(gallery.LocalPath))
        {
            return new PlatformFetchOutcome.Done(gallery.LocalPath);
        }

        var url = $"https://exhentai.org/g/{gallery.GalleryId}/{gallery.GalleryToken}/";
        var tasks = await helper.BuildTasks(new DownloadTaskAddInputModel
        {
            ThirdPartyId = ThirdPartyId.ExHentai,
            Type = (int) ExHentaiDownloadTaskType.SingleWork,
            Keys = [url],
            Names = string.IsNullOrEmpty(gallery.Title) ? null : [gallery.Title],
        });

        if (tasks.Length == 0)
        {
            logger.LogWarning("[ExHentai] The downloader produced no task for {Url}", url);

            return new PlatformFetchOutcome.Refused(
                "The ExHentai downloader would not take that gallery. Check its settings.");
        }

        await downloads.AddRange(tasks);

        return new PlatformFetchOutcome.Started(
            $"Downloading {gallery.Title ?? sourceKey}. This will carry on when it is done.");
    }

    public async Task<string?> DetectLocalPathAsync(string sourceKey, CancellationToken ct)
    {
        var gallery = await Find(sourceKey);

        return string.IsNullOrEmpty(gallery?.LocalPath) ? null : gallery.LocalPath;
    }

    /// <summary>
    /// A gallery is named by two things, so the key joins them. The same shape a source link uses.
    /// </summary>
    private static string SourceKeyOf(long galleryId, string token) => $"{galleryId}/{token}";

    private async Task<Bakabase.Abstractions.Models.Db.ExHentaiGalleryDbModel?> Find(string sourceKey)
    {
        var parts = sourceKey.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2 || !long.TryParse(parts[0], out var galleryId)) return null;

        return await galleries.GetByGalleryId(galleryId, parts[1]);
    }
}
