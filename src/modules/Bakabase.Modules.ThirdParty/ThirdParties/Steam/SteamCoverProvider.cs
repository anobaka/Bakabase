using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Steam;

public class SteamCoverProvider : ICoverProvider
{
    private readonly IResourceSourceLinkService _sourceLinkService;
    private readonly ILogger<SteamCoverProvider> _logger;
    private readonly SteamClient _steamClient;
    private readonly IFileManager _fileManager;

    public SteamCoverProvider(
        IResourceSourceLinkService sourceLinkService,
        ILogger<SteamCoverProvider> logger,
        SteamClient steamClient,
        IFileManager fileManager)
    {
        _sourceLinkService = sourceLinkService;
        _logger = logger;
        _steamClient = steamClient;
        _fileManager = fileManager;
    }

    public DataOrigin Origin => DataOrigin.Steam;
    public int Priority => 10;

    public bool AppliesTo(Resource resource)
    {
        return resource.SourceLinks?.Any(l => l.Source == ResourceSource.Steam) == true;
    }

    public DataStatus GetStatus(Resource resource)
    {
        var link = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.Steam);
        if (link == null) return DataStatus.Ready;
        if (link.LocalCoverPaths is { Count: > 0 }) return DataStatus.Ready;
        if (link.CoverUrls is { Count: > 0 })
        {
            if (link.CoverDownloadFailedAt.HasValue &&
                (DateTime.Now - link.CoverDownloadFailedAt.Value).TotalHours < 24)
                return DataStatus.Failed;
            return DataStatus.NotStarted;
        }
        return DataStatus.Ready;
    }

    public async Task<List<string>?> GetCoversAsync(Resource resource, CancellationToken ct)
    {
        var steamLink = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.Steam);
        if (steamLink?.LocalCoverPaths is { Count: > 0 })
        {
            return steamLink.LocalCoverPaths;
        }

        return await DownloadCoversAsync(steamLink!, ct);
    }

    private async Task<List<string>?> DownloadCoversAsync(ResourceSourceLink link, CancellationToken ct)
    {
        if (link.CoverUrls is not { Count: > 0 }) return null;

        var coverDir = _fileManager.BuildAbsolutePath("cache", "cover", "source", $"{link.ResourceId}_{link.Source}");
        Directory.CreateDirectory(coverDir);

        var localPaths = new List<string>();
        for (var i = 0; i < link.CoverUrls.Count; i++)
        {
            var url = link.CoverUrls[i];
            try
            {
                var imageData = await _steamClient.DownloadImage(url, ct);
                if (imageData == null) continue;
                var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                var localPath = Path.Combine(coverDir, $"{i}{ext}");
                await File.WriteAllBytesAsync(localPath, imageData, ct);
                localPaths.Add(localPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download cover from {Url}", url);
            }
        }

        if (localPaths.Count > 0)
        {
            link.LocalCoverPaths = localPaths;
            link.CoverDownloadFailedAt = null;
            await _sourceLinkService.Update(link);
            return localPaths;
        }
        else
        {
            link.CoverDownloadFailedAt = DateTime.Now;
            await _sourceLinkService.Update(link);
            return null;
        }
    }

    public async Task InvalidateAsync(int resourceId)
    {
        await _sourceLinkService.ClearLocalCoverPaths(resourceId, ResourceSource.Steam);
    }
}
