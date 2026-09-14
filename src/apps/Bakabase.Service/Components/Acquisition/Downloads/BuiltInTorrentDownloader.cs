using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using MonoTorrent.Client;

namespace Bakabase.Service.Components.Acquisition.Downloads;

public record AcquisitionTorrentDownloadResult(string Directory, IReadOnlyList<string> Files);

public interface IAcquisitionTorrentDownloader
{
    Task<AcquisitionTorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);

    Task<AcquisitionTorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct);
}

/// <summary>
/// One in-process engine per acquisition. Only verified content leaves the task's directory;
/// cancellation keeps partial pieces for a hash-checked retry and always releases the engine.
/// </summary>
public sealed class BuiltInTorrentDownloader : IAcquisitionTorrentDownloader
{
    private readonly Func<string> _cacheRoot;
    private readonly ILogger<BuiltInTorrentDownloader> _logger;
    private readonly bool _enableDht;

    public BuiltInTorrentDownloader(AppService appService, ILogger<BuiltInTorrentDownloader> logger)
        : this(() => Path.Combine(appService.AppDataDirectory, "acquisition-torrent-cache"), logger, true)
    {
    }

    // Local transfer tests disable discovery outside their loopback tracker.
    internal BuiltInTorrentDownloader(string cacheRoot, ILogger<BuiltInTorrentDownloader> logger, bool enableDht)
        : this(() => cacheRoot, logger, enableDht)
    {
    }

    private BuiltInTorrentDownloader(Func<string> cacheRoot, ILogger<BuiltInTorrentDownloader> logger, bool enableDht)
    {
        _cacheRoot = cacheRoot;
        _logger = logger;
        _enableDht = enableDht;
    }

    public Task<AcquisitionTorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (!MagnetLink.TryParse(magnetUri, out var magnet))
            throw new ArgumentException("The magnet link does not contain a valid BitTorrent info hash.", nameof(magnetUri));

        return DownloadAsync(magnet, null, workingDirectory, timeout, progress, ct);
    }

    public Task<AcquisitionTorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (metadata.Length > 4 * 1024 * 1024 || !Torrent.TryLoad(metadata, out var torrent))
            throw new ArgumentException("The torrent metadata must be a valid .torrent file no larger than 4 MiB.", nameof(metadata));

        ValidatePaths(torrent);

        return DownloadAsync(null, torrent, workingDirectory, timeout, progress, ct);
    }

    internal EngineSettings SettingsFor(string workingDirectory)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(workingDirectory))));
        return new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(Path.GetFullPath(_cacheRoot()), key),
            AllowPortForwarding = false,
            AllowLocalPeerDiscovery = false,
            ListenEndPoints = new Dictionary<string, IPEndPoint>(),
            DhtEndPoint = _enableDht ? new IPEndPoint(IPAddress.Any, 0) : null,
            AutoSaveLoadDhtCache = _enableDht,
            AutoSaveLoadMagnetLinkMetadata = true,
            // Re-hash existing pieces on every retry, including after an unclean shutdown.
            AutoSaveLoadFastResume = false,
            UsePartialFiles = true,
        }.ToSettings();
    }

    private async Task<AcquisitionTorrentDownloadResult> DownloadAsync(MagnetLink? magnet, Torrent? torrent,
        string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "The download timeout must be between zero and 30 days.");

        ct.ThrowIfCancellationRequested();
        var payloadDirectory = Path.Combine(Path.GetFullPath(workingDirectory), "torrent-data");
        Directory.CreateDirectory(payloadDirectory);
        var settings = SettingsFor(workingDirectory);
        Directory.CreateDirectory(settings.CacheDirectory);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        using var engine = new ClientEngine(settings);
        TorrentManager? manager = null;
        try
        {
            if (torrent == null)
            {
                if (progress != null) await progress(0, "Finding peers and downloading torrent metadata");
                var metadataPath = Path.Combine(settings.CacheDirectory, "source.torrent");
                if (File.Exists(metadataPath) && new FileInfo(metadataPath).Length <= 4 * 1024 * 1024)
                {
                    Torrent.TryLoad(await File.ReadAllBytesAsync(metadataPath, deadline.Token), out torrent);
                    if (torrent != null && !torrent.InfoHashes.Contains(magnet!.InfoHashes.V1OrV2)) torrent = null;
                }
                if (torrent == null)
                {
                    // Resolve metadata before permitting payload writes. This also lets us reject
                    // path traversal in a magnet's as-yet unknown file list before downloading it.
                    var metadata = await engine.DownloadMetadataAsync(magnet!, deadline.Token);
                    if (metadata.Length > 4 * 1024 * 1024 || !Torrent.TryLoad(metadata.Span, out torrent))
                        throw new IOException("The magnet returned invalid or oversized torrent metadata.");
                    ValidatePaths(torrent);
                    await File.WriteAllBytesAsync(metadataPath, metadata.ToArray(), deadline.Token);
                }
            }
            ValidatePaths(torrent);

            var torrentSettings = new TorrentSettingsBuilder
            {
                // The task already supplies a containing folder. Keep all internal relative paths
                // without inserting a second wrapper named after the torrent.
                CreateContainingDirectory = false,
                AllowDht = _enableDht,
                AllowPeerExchange = true,
            }.ToSettings();
            manager = await engine.AddAsync(torrent, payloadDirectory, torrentSettings);

            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.TorrentStateChanged += (_, args) =>
            {
                if (args.NewState is TorrentState.Seeding or TorrentState.Error) finished.TrySetResult();
            };
            await manager.StartAsync().WaitAsync(deadline.Token);
            while (manager.State != TorrentState.Seeding)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (manager.State == TorrentState.Error)
                    throw new IOException($"The torrent download failed: {manager.Error?.Exception.Message}", manager.Error?.Exception);

                if (progress != null)
                {
                    var message = manager.HasMetadata
                        ? $"{manager.Monitor.DataBytesDownloaded:N0} bytes received · {manager.Monitor.DownloadSpeed:N0} B/s · {manager.State}"
                        : "Finding peers and downloading torrent metadata";
                    await progress(manager.HasMetadata ? Math.Min(99, (int)manager.Progress) : 0, message);
                }

                await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token));
            }

            // Seeding means every piece has passed its torrent hash. Stop before handing the
            // paths to placement, so no writer or uploader retains the files as they are moved.
            await manager.StopAsync(TimeSpan.FromSeconds(2));
            var files = new List<string>();
            foreach (var file in manager.Files)
            {
                var fullPath = Path.GetFullPath(file.FullPath);
                var relative = Path.GetRelativePath(payloadDirectory, fullPath);
                if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                    throw new IOException("The torrent contains a file outside its download directory.");
                if (file.Length == 0 && !File.Exists(fullPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    using var empty = File.Create(fullPath);
                }
                if (!File.Exists(fullPath) || new FileInfo(fullPath).Length != file.Length)
                    throw new IOException($"The completed torrent file is missing or incomplete: {relative}");
                files.Add(fullPath);
            }
            if (files.Count == 0) throw new IOException("The torrent completed without any files.");
            if (progress != null) await progress(100, "Torrent files verified; download stopped");
            return new AcquisitionTorrentDownloadResult(payloadDirectory, files);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"The torrent did not finish within {timeout.TotalMinutes:0.##} minutes. Check its peers or retry later.");
        }
        finally
        {
            if (manager != null && manager.State != TorrentState.Stopped)
            {
                try { await manager.StopAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not cleanly stop the torrent; disposing its engine"); }
            }
        }
    }

    internal static void ValidatePaths(Torrent torrent)
    {
        foreach (var file in torrent.Files)
        {
            var segments = file.Path.Split(['/', '\\']);
            if (Path.IsPathRooted(file.Path) || file.Path.StartsWith('/') || file.Path.StartsWith('\\') ||
                segments.Any(s => s is "" or "." or "..") || segments[0].Contains(':'))
                throw new ArgumentException("The torrent contains an absolute or unsafe relative file path.");
        }
    }
}
