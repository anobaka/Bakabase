using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>Streams and verifies a file without creating an application job or resource.</summary>
public interface IHttpDownloader
{
    Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress,
        CancellationToken cancellationToken);
}
