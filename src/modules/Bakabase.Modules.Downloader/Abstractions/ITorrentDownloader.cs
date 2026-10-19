using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>Downloads verified BitTorrent files; preserves their relative directory structure.</summary>
public interface ITorrentDownloader
{
    // Preserve existing third-party implementations and test fakes while the built-in transport
    // supplies richer metrics and the exact destination behavior itself.
    Task<TorrentDownloadResult> DownloadAsync(TorrentDownloadRequest request,
        Func<TorrentTransferProgress, Task>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Metadata != null ? 1 : 0) + (request.MagnetUri != null ? 1 : 0) + (request.TorrentUrl != null ? 1 : 0) != 1)
            throw new ArgumentException("Exactly one torrent source is required.", nameof(request));
        Func<int, string?, Task>? report = progress == null ? null : (percentage, stage) =>
            progress(new TorrentTransferProgress {Percentage = percentage, Stage = stage ?? "Downloading"});
        if (request.Metadata != null)
            return DownloadTorrentAsync(request.Metadata, request.WorkingDirectory, request.Timeout, report, ct);
        if (request.MagnetUri != null)
            return DownloadMagnetAsync(request.MagnetUri, request.WorkingDirectory, request.Timeout, report, ct);
        if (request.TorrentUrl != null)
            return DownloadTorrentUrlAsync(request.TorrentUrl, request.WorkingDirectory, request.Timeout, report, ct);
        throw new ArgumentException("A torrent source is required.", nameof(request));
    }

    Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
    Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
    Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
}
