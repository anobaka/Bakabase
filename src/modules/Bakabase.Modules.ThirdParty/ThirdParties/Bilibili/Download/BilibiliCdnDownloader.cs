using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;

public sealed record BilibiliCdnDownloaderSettings
{
    /// <summary>Consecutive attempts on one URL that bring no new bytes before moving to the next URL.</summary>
    public int MaxAttemptsPerUrl { get; init; } = 3;

    /// <summary>Consecutive URL refreshes (new playurl answers) that bring no new bytes before giving up.</summary>
    public int MaxUrlRefreshes { get; init; } = 2;

    /// <summary>How long to wait for response headers.</summary>
    public TimeSpan HeaderTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long one read of the body may take before the connection counts as stalled.</summary>
    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan RetryDelayInitial { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetryDelayMax { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Upper bound for one stream, however steadily it progresses (progress resets the attempt budgets, so this
    /// is what keeps a connection that drops every few seconds from retrying forever). Exceeding it is transient:
    /// the next run resumes from the partial file.
    /// </summary>
    public TimeSpan MaxStreamDuration { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Minimum time between two progress reports of one stream (the last one is always reported).</summary>
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    public int BufferSize { get; init; } = 1024 * 1024;

    /// <summary>Cap for <see cref="BilibiliCdnDownloader.GetSmallAsync"/> bodies (danmaku, subtitles, covers).</summary>
    public long MaxSmallBodyBytes { get; init; } = 32L * 1024 * 1024;

    /// <summary>Waits between attempts; <c>null</c> = <see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/>.
    /// A seam for tests (DI-built instances included: register settings before the module does).</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }

    /// <summary>Opens the partial file for writing (<c>path</c>, <c>append</c>); <c>null</c> = a
    /// <see cref="FileStream"/>. A seam for tests that simulate a full disk.</summary>
    public Func<string, bool, Stream>? OpenPartFile { get; init; }
}

/// <summary>
/// One stream to fetch. <paramref name="IdentityKey"/> names the bytes (e.g. <c>{cid}-v{id}-c{codec}</c>), not the
/// URL: signed URLs change on every playurl answer, and a partial file is resumed across them as long as the
/// identity (and the total size the CDN reports) stays the same. <paramref name="Urls"/> are tried in order
/// (already ranked, see <see cref="BilibiliCdnUrls.Candidates"/>). <paramref name="RefreshUrls"/> asks the API
/// again for this stream's URLs; <c>null</c> (or none) means the stream is no longer offered.
/// </summary>
public sealed record BilibiliCdnTransfer(
    string DestinationPath,
    string IdentityKey,
    IReadOnlyList<string> Urls,
    Func<CancellationToken, Task<IReadOnlyList<string>?>> RefreshUrls);

/// <summary>A refresh no longer offers the stream being downloaded (the caller re-plans the page).</summary>
public sealed class BilibiliStreamChangedException(string identityKey)
    : Exception($"Stream {identityKey} is no longer offered.")
{
    public string IdentityKey { get; } = identityKey;
}

/// <summary>
/// Every CDN URL of a stream, after refreshing, was refused outright (4xx other than 408/429, or an error page
/// served as a success): the stream cannot be fetched from here (a region-locked edge, an exit IP the CDN rejects…). Not
/// transient — retrying the run would hit the same wall; the caller records the page as skipped.
/// </summary>
public sealed class BilibiliCdnDeadException(string identityKey, int? httpStatus, Exception? inner = null)
    : Exception(
        $"No CDN host would serve stream {identityKey}{(httpStatus is { } s ? $" (last answer: HTTP {s})" : "")}.",
        inner)
{
    public string IdentityKey { get; } = identityKey;
    public int? HttpStatus { get; } = httpStatus;
}

/// <summary>
/// One failed CDN request, described safely: the URL only in its <see cref="BilibiliCdnUrls.Redact"/>ed form, the
/// status, and a short redacted reason. Carried as the inner exception of the downloader's final failure; the
/// original network exception is never chained (it is not needed, and its text is not under our control).
/// </summary>
public sealed class BilibiliCdnTransferException(string redactedUrl, int? httpStatus, string detail)
    : Exception($"GET {redactedUrl} failed: {detail}")
{
    public string RedactedUrl { get; } = redactedUrl;
    public int? HttpStatus { get; } = httpStatus;
}

public sealed record BilibiliSmallBody(byte[] Bytes, IReadOnlyList<string> ContentEncodings, string? MediaType);

/// <summary>
/// Streams one file from Bilibili's CDN into place, resumably, through the
/// <see cref="InternalOptions.HttpClientNames.BilibiliCdn"/> client (no cookie, no rate limit, no request log).
/// </summary>
/// <remarks>
/// <para>Files: data in <c>{DestinationPath}.part</c>, <c>{DestinationPath}.part.json</c> =
/// <c>{"identity":…,"total":…}</c>. On success the partial file is moved to the destination and the sidecar
/// deleted; a destination that exists at entry counts as complete. Cancellation and failures keep both, so the
/// next attempt (or run) resumes with a <c>Range</c> request.</para>
/// <para>A URL is abandoned only after it actually failed — HTTP 4xx (not 408/429), a successful answer whose body
/// is an error page or error JSON, or <see cref="BilibiliCdnDownloaderSettings.MaxAttemptsPerUrl"/> attempts in a
/// row without new bytes; the local clock is never compared with a URL's <c>deadline</c> (clocks are wrong by
/// hours on real machines).</para>
/// <para>Errors: reading the response is network-class (retried, then
/// <see cref="BilibiliTemporarilyUnavailableException"/> with <see cref="BilibiliTemporaryFailureKind.CdnUnavailable"/>,
/// transient); every URL refused outright is <see cref="BilibiliCdnDeadException"/>; writing the local file is
/// <see cref="DiskWriteException"/> (fatal, never retried).</para>
/// <para>Only redacted URLs are ever logged or put into exceptions: signed URLs carry <c>mid</c>, <c>oi</c> (the
/// user's IP) and signatures.</para>
/// </remarks>
public sealed class BilibiliCdnDownloader(
    IHttpClientFactory httpClientFactory,
    BilibiliCdnDownloaderSettings settings,
    TimeProvider time,
    ILogger<BilibiliCdnDownloader> logger)
{
    private const string Endpoint = "cdn";

    private HttpClient CreateClient() => httpClientFactory.CreateClient(InternalOptions.HttpClientNames.BilibiliCdn);

    /// <param name="progress">(bytes on disk, total when known), at most every
    /// <see cref="BilibiliCdnDownloaderSettings.ProgressInterval"/> and once at the end. Bytes can go down when
    /// a partial file had to be discarded.</param>
    public async Task DownloadAsync(BilibiliCdnTransfer transfer, Action<long, long?>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var files = new PartFiles(transfer.DestinationPath, settings.OpenPartFile);
        if (File.Exists(files.DestinationPath))
        {
            return;
        }

        files.EnsureDirectory();

        var started = time.GetTimestamp();
        var reporter = new ProgressReporter(progress, time, settings.ProgressInterval);
        var client = CreateClient();
        var urls = transfer.Urls;
        var refreshesWithoutProgress = 0;
        // Progress = the partial file grew beyond anything seen before. A server that ignores Range (200) and
        // drops at the same point every time rewrites bytes but makes no progress.
        var highWater = files.ReadSidecar()?.Identity == transfer.IdentityKey ? files.PartLength() : 0;

        // A sign of life at every step that brings no bytes (attempts, waits, refreshes): hosts that answer and
        // then stall can keep a stream busy for many minutes without writing anything, and the task watchdog must
        // not take that for a hung task.
        void Heartbeat() => reporter.Report(files.PartLength(),
            files.ReadSidecar() is {Identity: var id, Total: var total} && id == transfer.IdentityKey ? total : null,
            force: true);

        while (true)
        {
            var lastFailures = new List<AttemptResult>();
            foreach (var url in urls)
            {
                var attemptsWithoutProgress = 0;
                AttemptResult result;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (time.GetElapsedTime(started) > settings.MaxStreamDuration)
                    {
                        logger.LogWarning("CDN stream {Identity} did not finish within {Limit}; giving up for now.",
                            transfer.IdentityKey, settings.MaxStreamDuration);
                        throw new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.CdnUnavailable,
                            Endpoint, null,
                            new BilibiliCdnTransferException(BilibiliCdnUrls.Redact(url), null,
                                $"the stream did not finish within {settings.MaxStreamDuration}"));
                    }

                    Heartbeat();
                    result = await TransferOnceAsync(client, url, transfer.IdentityKey, files, reporter, ct);
                    if (result.Kind == AttemptKind.Completed)
                    {
                        logger.LogInformation("CDN stream {Identity} finished: {Bytes} bytes in {Elapsed}.",
                            transfer.IdentityKey, result.Length, time.GetElapsedTime(started));
                        return;
                    }

                    logger.LogDebug("CDN stream {Identity}: {Url} → {Kind} (HTTP {Status}): {Detail}",
                        transfer.IdentityKey, BilibiliCdnUrls.Redact(url), result.Kind, result.HttpStatus,
                        result.Detail);

                    if (result.Kind == AttemptKind.RangeMismatch)
                    {
                        files.Discard();
                    }

                    var length = files.PartLength();
                    if (length > highWater)
                    {
                        highWater = length;
                        attemptsWithoutProgress = 0;
                        refreshesWithoutProgress = 0;
                    }
                    else
                    {
                        attemptsWithoutProgress++;
                    }

                    if (result.Kind == AttemptKind.Dead || attemptsWithoutProgress >= settings.MaxAttemptsPerUrl)
                    {
                        break;
                    }

                    if (result.Kind == AttemptKind.Network)
                    {
                        Heartbeat();
                        await DelayAsync(TransientNetworkError.GetBackoffDelay(Math.Max(0, attemptsWithoutProgress - 1),
                            settings.RetryDelayInitial, settings.RetryDelayMax), ct);
                    }
                }

                lastFailures.Add(result with {RedactedUrl = BilibiliCdnUrls.Redact(url)});
            }

            if (refreshesWithoutProgress < settings.MaxUrlRefreshes)
            {
                refreshesWithoutProgress++;
                Heartbeat();
                var fresh = await transfer.RefreshUrls(ct);
                Heartbeat();
                if (fresh == null || fresh.Count == 0)
                {
                    throw new BilibiliStreamChangedException(transfer.IdentityKey);
                }

                logger.LogDebug("CDN stream {Identity}: refreshed its URLs ({Count}).", transfer.IdentityKey,
                    fresh.Count);
                urls = fresh;
                continue;
            }

            throw Exhausted(transfer.IdentityKey, lastFailures);
        }
    }

    /// <summary>
    /// One GET of a small file (danmaku XML, subtitle JSON, cover) with the CDN headers and no cookie. Returns the
    /// raw bytes and <c>Content-Encoding</c> (callers decode); <c>null</c> on 404/410. Other failures throw
    /// <see cref="HttpRequestException"/> (a timeout too — never an <see cref="OperationCanceledException"/>
    /// unless <paramref name="ct"/> was cancelled) or <see cref="InvalidDataException"/> above the size cap.
    /// </summary>
    public async Task<BilibiliSmallBody?> GetSmallAsync(string url, CancellationToken ct)
    {
        var redacted = BilibiliCdnUrls.Redact(url);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        BilibiliCdnRequestHeaders.Apply(request, null);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(settings.HeaderTimeout);
        HttpResponseMessage response;
        try
        {
            response = await CreateClient()
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (Exception e) when (IsNetworkFailure(e, ct))
        {
            throw SmallBodyFailure(redacted, e);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"GET {redacted} answered HTTP {(int) response.StatusCode}.", null,
                    response.StatusCode);
            }

            var cap = settings.MaxSmallBodyBytes;
            if (response.Content.Headers.ContentLength > cap)
            {
                throw new InvalidDataException($"GET {redacted}: the body is larger than {cap} bytes.");
            }

            using var buffer = new MemoryStream();
            try
            {
                timeout.CancelAfter(settings.StallTimeout);
                await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
                var chunk = new byte[81920];
                while (true)
                {
                    timeout.CancelAfter(settings.StallTimeout);
                    var read = await body.ReadAsync(chunk, timeout.Token);
                    if (read == 0)
                    {
                        break;
                    }

                    if (buffer.Length + read > cap)
                    {
                        throw new InvalidDataException($"GET {redacted}: the body is larger than {cap} bytes.");
                    }

                    buffer.Write(chunk, 0, read);
                }
            }
            catch (Exception e) when (IsNetworkFailure(e, ct))
            {
                throw SmallBodyFailure(redacted, e);
            }

            return new BilibiliSmallBody(buffer.ToArray(), response.Content.Headers.ContentEncoding.ToList(),
                response.Content.Headers.ContentType?.MediaType);
        }
    }

    /// <summary>A failure of the connection (or our own timeout), as opposed to the caller cancelling.</summary>
    private static bool IsNetworkFailure(Exception e, CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        e is OperationCanceledException or HttpRequestException or IOException or SocketException;

    /// <summary>
    /// Rebuilt with a redacted text and without the original exception, whose message is not under our control.
    /// </summary>
    private static HttpRequestException SmallBodyFailure(string redactedUrl, Exception e) => e switch
    {
        OperationCanceledException => new HttpRequestException($"GET {redactedUrl} timed out.",
            new TimeoutException()),
        HttpRequestException hre => new HttpRequestException(hre.HttpRequestError,
            $"GET {redactedUrl} failed: {Describe(e)}", null, hre.StatusCode),
        _ => new HttpRequestException(HttpRequestError.ResponseEnded, $"GET {redactedUrl} failed: {Describe(e)}"),
    };

    private async Task<AttemptResult> TransferOnceAsync(HttpClient client, string url, string identity,
        PartFiles files, ProgressReporter reporter, CancellationToken ct)
    {
        var sidecar = files.ReadSidecar();
        if (sidecar?.Identity != identity)
        {
            // Another stream's bytes (or bytes of unknown origin): never append to them.
            files.Discard();
            sidecar = null;
        }

        long existing = files.PartLength();
        var knownTotal = sidecar?.Total;
        if (knownTotal is { } kt && existing > kt)
        {
            files.Discard();
            existing = 0;
            knownTotal = null;
        }

        if (knownTotal is { } complete && existing == complete && existing > 0)
        {
            files.Complete();
            reporter.Report(existing, complete, force: true);
            return AttemptResult.Done(existing);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existing > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existing, null);
        }

        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        BilibiliCdnRequestHeaders.Apply(request, null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(settings.HeaderTimeout);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AttemptResult.Network(null, $"no response within {settings.HeaderTimeout}");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or SocketException)
        {
            ct.ThrowIfCancellationRequested();
            return AttemptResult.Network(null, Describe(e));
        }

        using (response)
        {
            var status = (int) response.StatusCode;
            var append = true;
            long? total;
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    if (existing > 0)
                    {
                        // The range was ignored: the body starts at byte 0.
                        append = false;
                        existing = 0;
                    }

                    total = response.Content.Headers.ContentLength;
                    break;
                case HttpStatusCode.PartialContent:
                {
                    var range = response.Content.Headers.ContentRange;
                    if (range is not {HasRange: true} || !string.Equals(range.Unit, "bytes",
                            StringComparison.OrdinalIgnoreCase) || range.From != existing)
                    {
                        return AttemptResult.Mismatch(status, "Content-Range does not continue the partial file");
                    }

                    if (knownTotal is { } expected && range.Length is { } length && length != expected)
                    {
                        return AttemptResult.Mismatch(status, "the stream's total size changed");
                    }

                    total = range.Length ?? knownTotal;
                    break;
                }
                case HttpStatusCode.RequestedRangeNotSatisfiable:
                    var reportedTotal = response.Content.Headers.ContentRange?.Length;
                    if (existing > 0 && (knownTotal ?? reportedTotal) == existing)
                    {
                        files.Complete();
                        reporter.Report(existing, existing, force: true);
                        return AttemptResult.Done(existing);
                    }

                    return AttemptResult.Mismatch(status, "the range is not satisfiable");
                default:
                    return TransientNetworkError.IsTransientStatusCode(response.StatusCode)
                        ? AttemptResult.Network(status, $"HTTP {status}")
                        : AttemptResult.Dead(status, $"HTTP {status}");
            }

            // Only a successful answer gets here: an error status is classified by the status alone (a 503 with
            // an HTML page is as transient as one without).
            if (DescribeErrorBody(response.Content.Headers.ContentType?.MediaType) is { } errorBody)
            {
                // An error page or error JSON, sometimes served as 200 by PCDN hosts.
                return AttemptResult.Dead(status, errorBody);
            }

            if (total == 0)
            {
                // Before anything is written: an empty answer must not replace the partial file.
                return AttemptResult.Network(status, "an empty body");
            }

            files.WriteSidecar(identity, total);
            return await CopyBodyAsync(response, status, files, existing, append, total, reporter, timeout, ct);
        }
    }

    /// <summary>What a 2xx body is when its type says it is not media (an error page or error JSON), or null.</summary>
    private static string? DescribeErrorBody(string? mediaType)
    {
        if (string.IsNullOrEmpty(mediaType))
        {
            return null;
        }

        var type = mediaType.Trim().ToLowerInvariant();
        return type.StartsWith("text/") || type is "application/json" or "application/xml" ||
               type.EndsWith("+json") || type.EndsWith("+xml")
            ? $"a {type} body instead of media"
            : null;
    }

    private async Task<AttemptResult> CopyBodyAsync(HttpResponseMessage response, int status, PartFiles files,
        long existing, bool append, long? total, ProgressReporter reporter, CancellationTokenSource timeout,
        CancellationToken ct)
    {
        Stream body;
        try
        {
            timeout.CancelAfter(settings.StallTimeout);
            body = await response.Content.ReadAsStreamAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return AttemptResult.Network(status, "stalled");
        }
        catch (Exception e) when (e is HttpRequestException or IOException or SocketException)
        {
            ct.ThrowIfCancellationRequested();
            return AttemptResult.Network(status, Describe(e));
        }

        await using (body)
        {
            // Opened at the first byte, so a body that turns out empty never truncates the partial file.
            Stream? output = null;
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(4096, settings.BufferSize));
            long written = 0;
            AttemptResult? failure = null;
            try
            {
                while (true)
                {
                    int read;
                    // Reads and writes fail differently: a read error is the network (retry), a write error is
                    // the local disk (fatal — retrying would only download the same bytes again).
                    try
                    {
                        timeout.CancelAfter(settings.StallTimeout);
                        read = await body.ReadAsync(buffer.AsMemory(0, Math.Max(4096, settings.BufferSize)),
                            timeout.Token);
                        timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        failure = AttemptResult.Network(status, $"no data for {settings.StallTimeout}");
                        break;
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException or SocketException)
                    {
                        ct.ThrowIfCancellationRequested();
                        failure = AttemptResult.Network(status, Describe(e));
                        break;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    output ??= files.OpenPart(append);
                    try
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        throw DiskWriteException.From(files.PartPath, e);
                    }

                    written += read;
                    reporter.Report(existing + written, total);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                if (output != null)
                {
                    await files.CloseAsync(output);
                }
            }

            if (failure != null)
            {
                return failure;
            }

            var length = existing + written;
            if (length == 0)
            {
                return AttemptResult.Network(status, "an empty body");
            }

            if (total is { } t && length != t)
            {
                return length < t
                    ? AttemptResult.Network(status, $"the body ended after {length} of {t} bytes")
                    : AttemptResult.Mismatch(status, $"the body is longer ({length}) than announced ({t})");
            }

            files.Complete();
            reporter.Report(length, total ?? length, force: true);
            return AttemptResult.Done(length);
        }
    }

    private Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        if (delay <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        return settings.Delay != null ? settings.Delay(delay, ct) : Task.Delay(delay, time, ct);
    }

    private static Exception Exhausted(string identity, IReadOnlyList<AttemptResult> lastFailures)
    {
        var last = lastFailures.LastOrDefault();
        var inner = last == null
            ? null
            : new BilibiliCdnTransferException(last.RedactedUrl ?? "(no url)", last.HttpStatus, last.Detail);

        // Every URL refused outright → nothing to wait for. Anything network-like (timeouts, resets, 5xx, a
        // short body) → the CDN may well answer later: transient.
        if (lastFailures.Count == 0 || lastFailures.All(f => f.Kind == AttemptKind.Dead))
        {
            return new BilibiliCdnDeadException(identity, last?.HttpStatus, inner);
        }

        var lastNetwork = lastFailures.Last(f => f.Kind != AttemptKind.Dead);
        return new BilibiliTemporarilyUnavailableException(BilibiliTemporaryFailureKind.CdnUnavailable, Endpoint,
            null, inner, lastNetwork.HttpStatus);
    }

    private static string Describe(Exception e)
    {
        var kind = e is HttpRequestException hre ? $"{e.GetType().Name}/{hre.HttpRequestError}" : e.GetType().Name;
        return $"{kind}: {BilibiliDiagnostics.RedactText(e.Message)}";
    }

    private enum AttemptKind
    {
        Completed,
        Network,
        Dead,
        RangeMismatch,
    }

    private sealed record AttemptResult(AttemptKind Kind, int? HttpStatus, string Detail, long Length = 0)
    {
        public string? RedactedUrl { get; init; }
        public static AttemptResult Done(long length) => new(AttemptKind.Completed, null, "", length);
        public static AttemptResult Network(int? status, string detail) => new(AttemptKind.Network, status, detail);
        public static AttemptResult Dead(int? status, string detail) => new(AttemptKind.Dead, status, detail);
        public static AttemptResult Mismatch(int? status, string detail) => new(AttemptKind.RangeMismatch, status, detail);
    }

    private sealed class ProgressReporter(Action<long, long?>? progress, TimeProvider time, TimeSpan interval)
    {
        private long _lastReport = long.MinValue;

        public void Report(long done, long? total, bool force = false)
        {
            if (progress == null)
            {
                return;
            }

            var now = time.GetTimestamp();
            if (!force && _lastReport != long.MinValue && time.GetElapsedTime(_lastReport, now) < interval)
            {
                return;
            }

            _lastReport = now;
            progress(done, total);
        }
    }

    private sealed record Sidecar(
        [property: JsonPropertyName("identity")] string? Identity,
        [property: JsonPropertyName("total")] long? Total);

    /// <summary>The partial file and its sidecar. Every write goes through here and fails as a disk error.</summary>
    private sealed class PartFiles(string destinationPath, Func<string, bool, Stream>? openPartFile)
    {
        public string DestinationPath { get; } = Path.GetFullPath(destinationPath);
        public string PartPath => DestinationPath + ".part";
        private string SidecarPath => DestinationPath + ".part.json";

        public void EnsureDirectory() =>
            DiskWriteException.Guard(DestinationPath, () => Directory.CreateDirectory(Path.GetDirectoryName(DestinationPath)!));

        public long PartLength()
        {
            var info = new FileInfo(PartPath);
            return info.Exists ? info.Length : 0;
        }

        public Sidecar? ReadSidecar()
        {
            try
            {
                return File.Exists(SidecarPath)
                    ? JsonSerializer.Deserialize<Sidecar>(File.ReadAllText(SidecarPath))
                    : null;
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void WriteSidecar(string identity, long? total) => DiskWriteException.Guard(SidecarPath, () =>
        {
            var temp = SidecarPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Sidecar(identity, total)));
            File.Move(temp, SidecarPath, true);
        });

        public void Discard() => DiskWriteException.Guard(PartPath, () =>
        {
            File.Delete(PartPath);
            File.Delete(SidecarPath);
        });

        public Stream OpenPart(bool append)
        {
            Stream? stream = null;
            DiskWriteException.Guard(PartPath, () => stream = openPartFile != null
                ? openPartFile(PartPath, append)
                : new FileStream(PartPath, new FileStreamOptions
                {
                    Mode = append ? FileMode.Append : FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.Read,
                    // The downloader passes 1 MiB blocks; FileStream's own buffer would only add a copy.
                    BufferSize = 0,
                    Options = FileOptions.Asynchronous,
                }));
            return stream!;
        }

        public Task CloseAsync(Stream stream) => DiskWriteException.GuardAsync(PartPath, async () =>
        {
            await stream.FlushAsync();
            await stream.DisposeAsync();
        });

        public void Complete() => DiskWriteException.Guard(DestinationPath, () =>
        {
            File.Move(PartPath, DestinationPath, true);
            File.Delete(SidecarPath);
        });
    }
}
