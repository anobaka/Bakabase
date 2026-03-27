using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.ExHentai;

public class ExHentaiCoverProvider : ICoverProvider
{
    private readonly IResourceSourceLinkService _sourceLinkService;
    private readonly ILogger<ExHentaiCoverProvider> _logger;
    private readonly ExHentaiClient _exHentaiClient;
    private readonly IFileManager _fileManager;

    public ExHentaiCoverProvider(
        IResourceSourceLinkService sourceLinkService,
        ILogger<ExHentaiCoverProvider> logger,
        ExHentaiClient exHentaiClient,
        IFileManager fileManager)
    {
        _sourceLinkService = sourceLinkService;
        _logger = logger;
        _exHentaiClient = exHentaiClient;
        _fileManager = fileManager;
    }

    public DataOrigin Origin => DataOrigin.ExHentai;
    public int Priority => 10;

    public bool AppliesTo(Resource resource)
    {
        return resource.SourceLinks?.Any(l => l.Source == ResourceSource.ExHentai) == true;
    }

    public DataStatus GetStatus(Resource resource)
    {
        var link = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.ExHentai);
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
        var exhLink = resource.SourceLinks?.FirstOrDefault(l => l.Source == ResourceSource.ExHentai);
        if (exhLink?.LocalCoverPaths is { Count: > 0 })
        {
            return exhLink.LocalCoverPaths;
        }

        return await DownloadCoversAsync(exhLink!, ct);
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
                ct.ThrowIfCancellationRequested();
                var (imageData, contentType) = await _exHentaiClient.DownloadImage(url);
                var ext = GetExtensionFromContentType(contentType)
                          ?? Path.GetExtension(new Uri(url).AbsolutePath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                var localPath = Path.Combine(coverDir, $"{i}{ext}");
                await File.WriteAllBytesAsync(localPath, imageData, ct);
                localPaths.Add(localPath);
            }
            catch (OperationCanceledException)
            {
                throw;
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

    private static string? GetExtensionFromContentType(string? contentType)
    {
        return contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" => ".bmp",
            _ => null
        };
    }

    public async Task InvalidateAsync(int resourceId)
    {
        await _sourceLinkService.ClearLocalCoverPaths(resourceId, ResourceSource.ExHentai);
    }
}
