using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>Delegates a transfer to aria2 and waits for files visible to this process.</summary>
public interface IAria2Downloader
{
    Task<TorrentDownloadResult> DownloadMagnetAsync(string magnet, string workingDirectory,
        Aria2DownloadOptions options, Func<int, string?, Task>? progress, CancellationToken ct);
}
