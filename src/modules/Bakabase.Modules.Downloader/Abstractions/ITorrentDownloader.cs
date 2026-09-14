using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>Downloads verified BitTorrent files; preserves their relative directory structure.</summary>
public interface ITorrentDownloader
{
    Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
    Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
    Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
}
