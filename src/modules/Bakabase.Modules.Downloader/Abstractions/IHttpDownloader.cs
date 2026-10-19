using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>Streams and verifies a file without creating an application job or resource.</summary>
public interface IHttpDownloader
{
    Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress,
        CancellationToken cancellationToken);

    /// <summary>Structured transfer reporting; simple adapters retain the original callback contract.</summary>
    Task<string> DownloadWithProgressAsync(HttpDownloadRequest request, Func<HttpTransferProgress, Task>? progress,
        CancellationToken cancellationToken) => DownloadAsync(request,
        progress == null ? null : (percentage, stage) => progress(new HttpTransferProgress
        { Percentage = percentage, Stage = stage ?? "Downloading" }), cancellationToken);
}
