using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Downloader;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Downloader.Components;

public sealed class HttpDownloader(Func<string> cacheDirectory, IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    : IHttpDownloader
{
    private readonly ILogger<HttpDownloader> _logger = loggerFactory.CreateLogger<HttpDownloader>();

    public async Task<string> DownloadAsync(HttpDownloadRequest request, Func<int, string?, Task>? progress,
        CancellationToken cancellationToken)
    {
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(request.Timeout);
        using var host = httpClientFactory.CreateClient(request.HttpClientName ?? DownloaderServiceCollectionExtensions.HttpClientName);
        var directory = Path.GetFullPath(request.Directory);
        _logger.LogDebug("Starting HTTP download into {Directory} with up to {Connections} connections", directory, request.ParallelConnections);
        try
        {
            var probe = await ProbeAsync(host, request.Url, deadline.Token);
            var target = Path.Combine(directory, request.FileName ?? probe.FileName);
            if (File.Exists(target) && await MatchesChecksumAsync(target, probe.Identity, deadline.Token))
            {
                if (progress != null) await progress(100, "Download verified");
                return target;
            }
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Url + "\n" + target + "\n" + request.HttpClientName)));
            var cache = Path.Combine(Path.GetFullPath(cacheDirectory()), "http", key);
            Directory.CreateDirectory(cache);
            await using var lease = await AcquireLeaseAsync(cache, deadline.Token);
            var allowRanges = probe.Identity.CanResume;
            // A changed source or an ignored range gets one clean retry. The library owns normal
            // connection retries; this loop only discards an incompatible download representation.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await DownloadWithLibraryAsync(host, request, probe.Identity, cache, allowRanges, progress, deadline.Token);
                    break;
                }
                catch (HttpDownloadProtocolException ex) when (attempt == 0 && ex.Failure is HttpGuardFailure.RangeIgnored or HttpGuardFailure.RemoteChanged)
                {
                    HttpDownloadCheckpoint.Clear(cache);
                    probe = await ProbeAsync(host, request.Url, deadline.Token);
                    allowRanges = ex.Failure != HttpGuardFailure.RangeIgnored && probe.Identity.CanResume;
                }
                catch (HttpDownloadProtocolException ex)
                {
                    throw new IOException(ex.Message, ex);
                }
            }
            var payload = Path.Combine(cache, "payload");
            if (probe.Identity.Length is { } length && new FileInfo(payload).Length != length)
                throw new IOException("The downloaded file length does not match the server response.");
            if (probe.Identity.Md5Base64 != null && !await MatchesChecksumAsync(payload, probe.Identity, deadline.Token))
            {
                HttpDownloadCheckpoint.Clear(cache);
                throw new IOException("Failed to check MD5 for the downloaded file.");
            }
            Directory.CreateDirectory(directory);
            var staged = Path.Combine(directory, ".bakabase-download-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var source = File.OpenRead(payload))
                await using (var destination = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(destination, deadline.Token);
                File.Move(staged, target, true);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
            HttpDownloadCheckpoint.Clear(cache);
            if (progress != null) await progress(100, "Download verified");
            _logger.LogDebug("HTTP download completed: {File}", target);
            return target;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested &&
            (deadline.IsCancellationRequested || HasTimeoutCause(ex)))
        {
            throw new TimeoutException(deadline.IsCancellationRequested
                ? $"The download did not finish within {request.Timeout}."
                : "The HTTP client timed out while downloading the file.", ex);
        }
    }

    private async Task DownloadWithLibraryAsync(HttpClient host, HttpDownloadRequest request, HttpRemoteIdentity identity,
        string cache, bool allowRanges, Func<int, string?, Task>? progress, CancellationToken ct)
    {
        var package = allowRanges ? await HttpDownloadCheckpoint.ReadAsync(cache, identity, ct) : null;
        if (package == null) HttpDownloadCheckpoint.Clear(cache);
        using var guard = new HttpDownloadGuard(host, identity, allowRanges);
        using var client = new HttpClient(guard, disposeHandler: false) {Timeout = Timeout.InfiniteTimeSpan};
        var config = new DownloadConfiguration
        {
            ChunkCount = allowRanges ? request.ParallelConnections : 1,
            ParallelCount = allowRanges ? request.ParallelConnections : 1,
            ParallelDownload = allowRanges && request.ParallelConnections > 1,
            MinimumSizeOfChunking = 256 * 1024,
            MinimumChunkSize = 64 * 1024,
            MaxTryAgainOnFailure = request.MaxRetries,
            MaximumBytesPerSecond = request.MaximumBytesPerSecond,
            MaximumMemoryBufferBytes = 4 * 1024 * 1024,
            ClearPackageOnCompletionWithFailure = false,
            // The library persists its own positions as well. Across process boundaries we resume
            // only the independently hashed checkpoint, never trust a raw preallocated file length.
            EnableAutoResumeDownload = allowRanges,
            CustomHttpClientFactory = () => client
        };
        if (package != null)
        {
            // Persisted offsets survive; retry limits and failure counters belong to this attempt.
            package.Chunks = package.Chunks.Select(chunk => new Chunk(chunk.Start, chunk.End)
            {
                Id = chunk.Id,
                Position = chunk.Position,
                MaxTryAgainOnFailure = config.MaxTryAgainOnFailure,
                Timeout = config.BlockTimeout
            }).ToArray();
        }
        // The library logs its normal writer-disposal cancellation as Error. Keep its internal
        // diagnostics out of host error reporting; actual failures propagate via status/events below.
        await using var downloader = new DownloadService(config);
        Exception? completedError = null;
        var percentage = 0;
        downloader.DownloadStarted += (_, _) => guard.TransferStarted = true;
        downloader.DownloadProgressChanged += (_, args) => Interlocked.Exchange(ref percentage, Math.Clamp((int)args.ProgressPercentage, 0, 99));
        downloader.DownloadFileCompleted += (_, args) => completedError = args.Error;
        Task transfer = package == null
            ? downloader.DownloadFileTaskAsync(request.Url, Path.Combine(cache, "payload"), ct)
            : downloader.DownloadFileTaskAsync(package, request.Url, ct);
        Exception? thrown = null;
        try
        {
            while (!transfer.IsCompleted)
            {
                await Task.WhenAny(transfer, Task.Delay(200));
                if (!transfer.IsCompleted && progress != null) await progress(Volatile.Read(ref percentage), "Downloading");
            }
            await transfer;
        }
        catch (Exception ex)
        {
            thrown = ex;
            await downloader.CancelTaskAsync();
            try { await transfer; } catch { /* The original error is retained below. */ }
        }
        // Status/events are authoritative: this library can complete its Task without throwing
        // even when the transfer failed or was stopped. Its file writer must be closed first.
        await downloader.Package.CloseAsync();
        if (guard.FirstFailure is { } protocolFailure)
        {
            HttpDownloadCheckpoint.Clear(cache);
            throw protocolFailure;
        }
        if (downloader.Status != DownloadStatus.Completed || thrown != null)
        {
            if (allowRanges) await HttpDownloadCheckpoint.SaveAsync(cache, identity, downloader.Package);
            else HttpDownloadCheckpoint.Clear(cache);
            ct.ThrowIfCancellationRequested();
            if ((thrown ?? completedError) is { } error)
                ExceptionDispatchInfo.Capture(error).Throw();
            throw new IOException($"The HTTP download ended with status {downloader.Status}.");
        }
    }

    private static async Task<FileStream> AcquireLeaseAsync(string cache, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(Path.Combine(cache, "transfer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(100, ct); }
        }
    }

    private static bool HasTimeoutCause(Exception exception) => exception is TimeoutException ||
        exception.InnerException is { } inner && HasTimeoutCause(inner);

    private static async Task<bool> MatchesChecksumAsync(string file, HttpRemoteIdentity identity, CancellationToken ct)
    {
        if (identity.Md5Base64 == null || identity.Length is { } length && new FileInfo(file).Length != length) return false;
        await using var stream = File.OpenRead(file);
        var hash = await MD5.HashDataAsync(stream, ct);
        return Convert.ToBase64String(hash) == identity.Md5Base64;
    }

    private sealed record Probe(HttpRemoteIdentity Identity, string FileName);

    private static async Task<Probe> ProbeAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var head = new HttpRequestMessage(HttpMethod.Head, url);
        head.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new HttpRequestException("The server refused a HEAD request.");
            }
        }
        catch (HttpRequestException)
        {
            using var rangeProbe = new HttpRequestMessage(HttpMethod.Get, url);
            rangeProbe.Headers.Range = new RangeHeaderValue(0, 0);
            rangeProbe.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            response = await client.SendAsync(rangeProbe, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        using (response)
        {
            response.EnsureSuccessStatusCode();
            var range = response.Content.Headers.ContentRange;
            if (response.StatusCode == HttpStatusCode.PartialContent &&
                (range?.Unit != "bytes" || range.From != 0 || range.To != 0 || !range.HasLength))
                throw new IOException("The server returned an invalid range during the file probe.");
            var encoded = response.Content.Headers.ContentEncoding.Any(e => !e.Equals("identity", StringComparison.OrdinalIgnoreCase));
            var length = encoded ? null : range?.Length ?? response.Content.Headers.ContentLength;
            var etag = response.Headers.ETag is {IsWeak: false} tag ? tag.ToString() : null;
            var modified = encoded ? null : response.Content.Headers.LastModified;
            var checksum = !encoded && response.StatusCode != HttpStatusCode.PartialContent ? response.Content.Headers.ContentMD5 : null;
            var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
            var identity = new HttpRemoteIdentity(finalUrl, length, encoded ? null : etag, modified,
                checksum == null ? null : Convert.ToBase64String(checksum));
            var name = response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName;
            name = name?.Trim('"', '\'', ' ');
            if (string.IsNullOrWhiteSpace(name)) name = Uri.UnescapeDataString(Path.GetFileName(new Uri(finalUrl).AbsolutePath));
            foreach (var invalid in Path.GetInvalidFileNameChars().Concat("/\\:*?\"<>|")) name = name?.Replace(invalid, '_');
            name = name?.Trim('.', ' ');
            return new Probe(identity, string.IsNullOrWhiteSpace(name) ? "download" : name);
        }
    }

    private static void Validate(HttpDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("The download URL must use HTTP or HTTPS.", nameof(request));
        if (request.Timeout <= TimeSpan.Zero && request.Timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(request), "The download timeout must be positive.");
        if (request.ParallelConnections is < 1 or > 16 || request.MaxRetries is < 0 or > 10 || request.MaximumBytesPerSecond < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Use 1–16 connections, 0–10 retries and a non-negative speed limit.");
        if (request.FileName is { } name && (string.IsNullOrWhiteSpace(name) || name is "." or ".." ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny("/\\:*?\"<>|".ToCharArray()) >= 0))
            throw new ArgumentException("FileName must be a single valid file name.", nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Directory);
    }
}
