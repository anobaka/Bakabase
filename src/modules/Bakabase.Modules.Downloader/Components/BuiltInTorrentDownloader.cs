using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.Logging;
using MonoTorrent;
using MonoTorrent.Client;

namespace Bakabase.Modules.Downloader.Components;

/// <summary>
/// One in-process engine per download. Only verified content leaves the task's directory;
/// cancellation keeps partial pieces for a hash-checked retry and always releases the engine.
/// </summary>
public sealed class BuiltInTorrentDownloader : ITorrentDownloader
{
    private readonly Func<string> _cacheRoot;
    private readonly ILogger<BuiltInTorrentDownloader> _logger;
    private readonly bool _enableDht;
    private readonly ITorrentNetworkOptionsProvider? _networkOptions;

    public BuiltInTorrentDownloader(Func<string> cacheRoot, ILogger<BuiltInTorrentDownloader> logger,
        IHttpClientFactory httpClientFactory, ITorrentNetworkOptionsProvider? networkOptions = null)
        : this(cacheRoot, logger, true, networkOptions)
    {
    }

    // Local transfer tests disable discovery outside their loopback tracker.
    internal BuiltInTorrentDownloader(string cacheRoot, ILogger<BuiltInTorrentDownloader> logger, bool enableDht,
        IHttpClientFactory? httpClientFactory = null, ITorrentNetworkOptionsProvider? networkOptions = null)
        : this(() => cacheRoot, logger, enableDht, networkOptions)
    {
    }

    private BuiltInTorrentDownloader(Func<string> cacheRoot, ILogger<BuiltInTorrentDownloader> logger,
        bool enableDht, ITorrentNetworkOptionsProvider? networkOptions)
    {
        _cacheRoot = cacheRoot;
        _logger = logger;
        _networkOptions = networkOptions;
        _enableDht = enableDht;
    }

    public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) =>
        DownloadAsync(new TorrentDownloadRequest {MagnetUri = magnetUri, WorkingDirectory = workingDirectory, Timeout = timeout},
            LegacyProgress(progress), ct);

    public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) =>
        DownloadAsync(new TorrentDownloadRequest {Metadata = metadata, WorkingDirectory = workingDirectory, Timeout = timeout},
            LegacyProgress(progress), ct);

    public Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct) =>
        DownloadAsync(new TorrentDownloadRequest {TorrentUrl = url, WorkingDirectory = workingDirectory, Timeout = timeout},
            LegacyProgress(progress), ct);

    private static Func<TorrentTransferProgress, Task>? LegacyProgress(Func<int, string?, Task>? progress) =>
        progress == null ? null : update => progress(update.Percentage, update.SpeedBytesPerSecond is { } speed
            ? $"{update.CompletedBytes:N0} bytes verified · {speed:N0} B/s · {update.Stage}"
            : update.Stage);

    public async Task<TorrentDownloadResult> DownloadAsync(TorrentDownloadRequest request,
        Func<TorrentTransferProgress, Task>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTimeout(request.Timeout);
        if ((request.Metadata != null ? 1 : 0) + (request.MagnetUri != null ? 1 : 0) + (request.TorrentUrl != null ? 1 : 0) != 1)
            throw new ArgumentException("Exactly one torrent source is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            throw new ArgumentException("A download directory is required.", nameof(request));
        var network = new TorrentNetwork(_networkOptions?.GetOptions() ?? new TorrentNetworkOptions());
        MagnetLink? magnet = null;
        Torrent? torrent = request.Metadata != null ? TorrentMetadata.Parse(request.Metadata) : null;
        if (request.MagnetUri != null && !MagnetLink.TryParse(request.MagnetUri, out magnet))
            throw new ArgumentException("The magnet link does not contain a valid BitTorrent info hash.", nameof(request));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(request.Timeout);
        try
        {
            if (request.TorrentUrl != null)
            {
                if (!Uri.TryCreate(request.TorrentUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                    !string.IsNullOrEmpty(uri.UserInfo))
                    throw new ArgumentException("Enter an HTTP(S) torrent metadata URL without embedded credentials.", nameof(request));
                if (progress != null) await progress(new TorrentTransferProgress {Stage = "ReadingMetadata"});
                using var http = network.CreateHttpClient();
                http.Timeout = Timeout.InfiniteTimeSpan;
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > TorrentMetadata.MaxMetadataBytes)
                    throw new ArgumentException("The torrent metadata exceeds 4 MiB.");
                await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
                torrent = TorrentMetadata.Parse(await TorrentMetadata.ReadBoundedAsync(input, deadline.Token));
            }
            return await TransferAsync(magnet, torrent, request.WorkingDirectory, request.UseWorkingDirectoryAsDestination,
                network, progress, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"The torrent did not finish within {request.Timeout.TotalMinutes:0.##} minutes. Check its peers or retry later.");
        }
        catch (HttpRequestException) when (network.UsesProxy)
        {
            throw new IOException("The torrent metadata request through the BT proxy failed. Check the proxy and torrent URL.");
        }
    }

    internal EngineSettings SettingsFor(string workingDirectory, bool usesProxy = false)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(workingDirectory))));
        return new EngineSettingsBuilder
        {
            CacheDirectory = Path.Combine(Path.GetFullPath(_cacheRoot()), key),
            AllowPortForwarding = false,
            AllowLocalPeerDiscovery = false,
            ListenEndPoints = new Dictionary<string, IPEndPoint>(),
            DhtEndPoint = _enableDht && !usesProxy ? new IPEndPoint(IPAddress.Any, 0) : null,
            AutoSaveLoadDhtCache = _enableDht && !usesProxy,
            AutoSaveLoadMagnetLinkMetadata = true,
            // Re-hash existing pieces on every retry, including after an unclean shutdown.
            AutoSaveLoadFastResume = false,
            UsePartialFiles = true,
        }.ToSettings();
    }

    private async Task<TorrentDownloadResult> TransferAsync(MagnetLink? magnet, Torrent? torrent,
        string workingDirectory, bool useWorkingDirectoryAsDestination, TorrentNetwork network,
        Func<TorrentTransferProgress, Task>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var payloadDirectory = useWorkingDirectoryAsDestination
            ? Path.GetFullPath(workingDirectory)
            : Path.Combine(Path.GetFullPath(workingDirectory), "torrent-data");
        Directory.CreateDirectory(payloadDirectory);
        var settings = SettingsFor(workingDirectory, network.UsesProxy);
        Directory.CreateDirectory(settings.CacheDirectory);
        using var engine = new ClientEngine(settings, network.CreateFactories());
        TorrentManager? manager = null;
        try
        {
            if (torrent == null)
            {
                if (progress != null) await progress(new TorrentTransferProgress {Stage = "FindingMetadata"});
                var metadataPath = Path.Combine(settings.CacheDirectory, "source.torrent");
                if (File.Exists(metadataPath) && new FileInfo(metadataPath).Length <= TorrentMetadata.MaxMetadataBytes)
                {
                    try { torrent = TorrentMetadata.Parse(await File.ReadAllBytesAsync(metadataPath, ct)); }
                    catch (ArgumentException) { torrent = null; }
                    if (torrent != null && !torrent.InfoHashes.Contains(magnet!.InfoHashes.V1OrV2)) torrent = null;
                }
                if (torrent == null)
                {
                    // Resolve metadata before permitting payload writes. This also lets us reject
                    // path traversal in a magnet's as-yet unknown file list before downloading it.
                    var metadata = await engine.DownloadMetadataAsync(magnet!, ct);
                    torrent = TorrentMetadata.Parse(metadata.ToArray());
                    await File.WriteAllBytesAsync(metadataPath, metadata.ToArray(), ct);
                }
            }
            TorrentMetadata.ValidatePaths(torrent);

            var torrentSettings = new TorrentSettingsBuilder
            {
                // The task already supplies a containing folder. Keep all internal relative paths
                // without inserting a second wrapper named after the torrent.
                CreateContainingDirectory = false,
                AllowDht = _enableDht && !network.UsesProxy,
                AllowPeerExchange = true,
            }.ToSettings();
            manager = await engine.AddAsync(torrent, payloadDirectory, torrentSettings);

            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.TorrentStateChanged += (_, args) =>
            {
                if (args.NewState is TorrentState.Seeding or TorrentState.Error) finished.TrySetResult();
            };
            await manager.StartAsync().WaitAsync(ct);
            while (manager.State != TorrentState.Seeding)
            {
                ct.ThrowIfCancellationRequested();
                if (manager.State == TorrentState.Error)
                    throw new IOException($"The torrent download failed: {manager.Error?.Exception.Message}", manager.Error?.Exception);

                if (progress != null)
                {
                    await progress(new TorrentTransferProgress
                    {
                        Stage = manager.State.ToString(),
                        Percentage = Math.Min(99, (int)manager.Progress),
                        CompletedBytes = Math.Min(torrent.Size, (long)(torrent.Size * manager.Progress / 100)),
                        TotalBytes = torrent.Size,
                        SpeedBytesPerSecond = manager.Monitor.DownloadRate,
                        Peers = manager.Peers.Seeds + manager.Peers.Leechs
                    });
                }

                await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMilliseconds(500), ct));
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
            if (progress != null) await progress(new TorrentTransferProgress
            {
                Stage = "Completed", Percentage = 100, CompletedBytes = torrent.Size,
                TotalBytes = torrent.Size, SpeedBytesPerSecond = 0, Peers = 0
            });
            return new TorrentDownloadResult(payloadDirectory, files);
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

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "The download timeout must be between zero and 30 days.");
    }
}
