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
using Bakabase.Modules.Downloader.Extensions;
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
    private readonly IHttpClientFactory? _httpClientFactory;

    public BuiltInTorrentDownloader(Func<string> cacheRoot, ILogger<BuiltInTorrentDownloader> logger,
        IHttpClientFactory httpClientFactory) : this(cacheRoot, logger, httpClientFactory, true)
    {
    }

    // Local transfer tests disable discovery outside their loopback tracker.
    internal BuiltInTorrentDownloader(string cacheRoot, ILogger<BuiltInTorrentDownloader> logger, bool enableDht,
        IHttpClientFactory? httpClientFactory = null) : this(() => cacheRoot, logger, httpClientFactory, enableDht)
    {
    }

    private BuiltInTorrentDownloader(Func<string> cacheRoot, ILogger<BuiltInTorrentDownloader> logger,
        IHttpClientFactory? httpClientFactory, bool enableDht)
    {
        _cacheRoot = cacheRoot;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _enableDht = enableDht;
    }

    public Task<TorrentDownloadResult> DownloadMagnetAsync(string magnetUri, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (!MagnetLink.TryParse(magnetUri, out var magnet))
            throw new ArgumentException("The magnet link does not contain a valid BitTorrent info hash.", nameof(magnetUri));

        return DownloadAsync(magnet, null, workingDirectory, timeout, progress, ct);
    }

    public Task<TorrentDownloadResult> DownloadTorrentAsync(byte[] metadata, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        var torrent = TorrentMetadata.Parse(metadata);
        return DownloadAsync(null, torrent, workingDirectory, timeout, progress, ct);
    }

    public async Task<TorrentDownloadResult> DownloadTorrentUrlAsync(string url, string workingDirectory,
        TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Enter an HTTP(S) URL for the torrent metadata.", nameof(url));
        ValidateTimeout(timeout);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        try
        {
            if (progress != null) await progress(0, "Reading the torrent file");
            using var http = (_httpClientFactory ?? throw new InvalidOperationException("An HTTP client factory is required."))
                .CreateClient(DownloaderServiceCollectionExtensions.HttpClientName);
            // The operation deadline covers both metadata and file transfer.
            http.Timeout = Timeout.InfiniteTimeSpan;
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > TorrentMetadata.MaxMetadataBytes)
                throw new ArgumentException("The torrent metadata exceeds 4 MiB.");
            await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
            var metadata = await TorrentMetadata.ReadBoundedAsync(input, deadline.Token);
            return await DownloadTorrentAsync(metadata, workingDirectory, timeout, progress, deadline.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"The torrent download did not finish within {timeout.TotalMinutes:0.##} minutes.");
        }
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

    private async Task<TorrentDownloadResult> DownloadAsync(MagnetLink? magnet, Torrent? torrent,
        string workingDirectory, TimeSpan timeout, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        ValidateTimeout(timeout);

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
                if (File.Exists(metadataPath) && new FileInfo(metadataPath).Length <= TorrentMetadata.MaxMetadataBytes)
                {
                    try { torrent = TorrentMetadata.Parse(await File.ReadAllBytesAsync(metadataPath, deadline.Token)); }
                    catch (ArgumentException) { torrent = null; }
                    if (torrent != null && !torrent.InfoHashes.Contains(magnet!.InfoHashes.V1OrV2)) torrent = null;
                }
                if (torrent == null)
                {
                    // Resolve metadata before permitting payload writes. This also lets us reject
                    // path traversal in a magnet's as-yet unknown file list before downloading it.
                    var metadata = await engine.DownloadMetadataAsync(magnet!, deadline.Token);
                    torrent = TorrentMetadata.Parse(metadata.ToArray());
                    await File.WriteAllBytesAsync(metadataPath, metadata.ToArray(), deadline.Token);
                }
            }
            TorrentMetadata.ValidatePaths(torrent);

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
            return new TorrentDownloadResult(payloadDirectory, files);
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

    private static void ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(timeout), "The download timeout must be between zero and 30 days.");
    }
}
