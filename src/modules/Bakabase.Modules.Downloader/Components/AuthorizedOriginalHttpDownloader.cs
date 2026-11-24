using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Components;

internal sealed class AuthorizedOriginalHttpDownloader(IHttpClientFactory clients,
    IEnumerable<DownloaderServiceCollectionExtensions.AuthorizedClient> authorizedClients,
    IStorageWriteGuard? storage = null) : IAuthorizedOriginalHttpDownloader
{
    private readonly HashSet<string> _clients = authorizedClients.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

    public Task<AuthorizedOriginalCheckpoint> InspectAsync(AuthorizedOriginalIdentity identity,
        string checkpointDirectory, CancellationToken ct) => Inspect(identity, checkpointDirectory, false, ct);

    public Task<AuthorizedOriginalCheckpoint> InspectHandedOffAsync(AuthorizedOriginalIdentity identity,
        string checkpointDirectory, CancellationToken ct) => Inspect(identity, checkpointDirectory, true, ct);

    private static async Task<AuthorizedOriginalCheckpoint> Inspect(AuthorizedOriginalIdentity identity,
        string checkpointDirectory, bool handedOff, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var store = new AuthorizedOriginalCheckpointStore(identity, checkpointDirectory);
        try
        {
            if (!store.HasEntries)
            {
                if (handedOff) throw AuthorizedOriginalCheckpointStore.Invalid();
                return new(false, 0, false);
            }
            using var lease = store.Lease(create: false);
            var state = await store.ReadAsync(ct, handedOff);
            return new(state != null, state?.VerifiedBytes ?? 0, handedOff || state?.Published == true);
        }
        catch (Exception ex) when (ex is IOException and not AuthorizedOriginalDownloadException or UnauthorizedAccessException)
        { throw AuthorizedOriginalCheckpointStore.Invalid(); }
    }

    public Task<string> DownloadAsync(AuthorizedOriginalIdentity identity, string checkpointDirectory,
        string httpClientName, Func<CancellationToken, Task<Uri>> resolveOriginal,
        Func<HttpTransferProgress, Task>? progress, CancellationToken ct)
    {
        if (!_clients.Contains(httpClientName)) throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_LOCATION_UNAVAILABLE");
        var store = new AuthorizedOriginalCheckpointStore(identity, checkpointDirectory);
        return storage == null ? Transfer(ct) : storage.RunAsync([store.DirectoryPath], StorageWriteScope.Current, Transfer, ct);

        async Task<string> Transfer(CancellationToken token)
        {
            using var client = clients.CreateClient(httpClientName);
            var origin = new OriginalConnection(client, resolveOriginal, identity.Length);
            store.EnsureDirectory();
            using var lease = store.Lease(create: true);
            var state = await store.ReadAsync(token);
            var tag = await origin.ProbeAsync(null, token);
            var version = AuthorizedOriginalCheckpointStore.Fingerprint(tag.ToString());
            if (state != null && !state.Version.AsSpan().SequenceEqual(version)) throw Changed();
            state ??= await store.InitializeAsync(version, token);
            var offset = state.VerifiedBytes;
            var progressClock = Stopwatch.StartNew();
            var lastReported = offset;
            if (progress != null) await progress(Snapshot(offset, identity.Length, 0));
            using var observation = FileIoObservationScope.Begin(read: false, write: true);
            using var network = FileIoObservationScope.BeginNetworkReceive(FileIoObservationScope.NetworkDownloadId);

            await using (var file = new FileStream(state.Published ? store.Payload : store.Partial,
                             state.Published ? FileMode.Open : FileMode.OpenOrCreate,
                             state.Published ? FileAccess.Read : FileAccess.ReadWrite, FileShare.Read))
            {
                await store.VerifyAsync(file, state, token);
                if (!state.Published)
                {
                    file.SetLength(offset);
                    file.Position = offset;
                    using var ledger = store.OpenWriter(state);
                    while (offset < identity.Length)
                    {
                        var end = offset + Math.Min(AuthorizedOriginalCheckpointStore.SegmentBytes, identity.Length - offset) - 1;
                        using var response = await origin.OpenRangeAsync(offset, end, tag, token);
                        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                        var remaining = end - offset + 1;
                        var buffer = new byte[81920];
                        await using var stream = await OpenBodyAsync(response.Content, token);
                        using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                        while (remaining > 0)
                        {
                            var size = (int)Math.Min(remaining, buffer.Length);
                            var count = await ReadBodyAsync(stream, buffer.AsMemory(0, size), readDeadline, token);
                            if (count == 0) throw Failed();
                            network?.AddReceivedBytes(count);
                            await file.WriteAsync(buffer.AsMemory(0, count), token);
                            observation?.Add(0, count);
                            hash.AppendData(buffer, 0, count);
                            remaining -= count;
                            if (progress != null && progressClock.ElapsedMilliseconds >= 500)
                            {
                                await progress(Snapshot(file.Position, identity.Length,
                                    (file.Position - lastReported) / progressClock.Elapsed.TotalSeconds));
                                lastReported = file.Position;
                                progressClock.Restart();
                            }
                        }
                        if (await ReadBodyAsync(stream, buffer.AsMemory(0, 1), readDeadline, token) != 0) throw InvalidRange();
                        file.Flush(flushToDisk: true);
                        await ledger.WriteAsync(hash.GetHashAndReset(), token);
                        ledger.Flush(flushToDisk: true);
                        offset = end + 1;
                    }
                }
                // Verify authorization/immutable identity again while holding the payload open.
                // This also applies when all content came from the prior process's journal.
                await origin.ProbeAsync(tag, token);
                if (file.Length != identity.Length) throw AuthorizedOriginalCheckpointStore.Invalid();
            }
            if (!state.Published) store.Publish();
            if (progress != null) await progress(new HttpTransferProgress
                { Stage = "Download verified", Percentage = 100, CompletedBytes = identity.Length, TotalBytes = identity.Length });
            return store.Payload;
        }
    }

    private static HttpTransferProgress Snapshot(long received, long length, double speed) => new()
    {
        Percentage = length == 0 ? 0 : Math.Min(99, (int)(100.0 * received / length)),
        CompletedBytes = received, TotalBytes = length, SpeedBytesPerSecond = speed
    };

    private static async Task<Stream> OpenBodyAsync(HttpContent content, CancellationToken ct)
    {
        try { return await content.ReadAsStreamAsync(ct); }
        catch (Exception) { ct.ThrowIfCancellationRequested(); throw Failed(); }
    }

    private static async Task<int> ReadBodyAsync(Stream stream, Memory<byte> buffer,
        CancellationTokenSource deadline, CancellationToken ct)
    {
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try { return await stream.ReadAsync(buffer, deadline.Token); }
        catch (Exception) { ct.ThrowIfCancellationRequested(); throw Failed(); }
    }

    private sealed class OriginalConnection(HttpClient client, Func<CancellationToken, Task<Uri>> resolve, long length)
    {
        private Uri? _location;

        internal async Task<EntityTagHeaderValue> ProbeAsync(EntityTagHeaderValue? expected, CancellationToken ct)
        {
            _location = null;
            using var response = await RequestAsync(length == 0 ? HttpMethod.Head : HttpMethod.Get,
                length == 0 ? null : new RangeHeaderValue(0, 0), expected, ct);
            var tag = response.Headers.ETag;
            if (expected != null && !expected.Equals(tag)) throw Changed();
            if (tag == null || tag.IsWeak || tag.Tag == "*") throw Unsupported();
            return tag;
        }

        internal Task<HttpResponseMessage> OpenRangeAsync(long start, long end, EntityTagHeaderValue expected,
            CancellationToken ct) => RequestAsync(HttpMethod.Get, new RangeHeaderValue(start, end), expected, ct);

        private async Task<HttpResponseMessage> RequestAsync(HttpMethod method, RangeHeaderValue? range,
            EntityTagHeaderValue? tag, CancellationToken ct)
        {
            for (var attempt = 0; ; attempt++)
            {
                _location ??= await ResolveAsync(ct);
                using var request = new HttpRequestMessage(method, _location);
                request.Headers.Range = range;
                request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
                if (tag != null)
                {
                    request.Headers.IfMatch.Add(tag);
                    if (range != null) request.Headers.IfRange = new RangeConditionHeaderValue(tag);
                }
                HttpResponseMessage response;
                using var headerDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                headerDeadline.CancelAfter(TimeSpan.FromSeconds(90));
                try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headerDeadline.Token); }
                catch (Exception) { ct.ThrowIfCancellationRequested(); throw Failed(); }
                try
                {
                    if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Gone or HttpStatusCode.NotFound)
                    {
                        if (attempt > 0) throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_LOCATION_UNAVAILABLE");
                        response.Dispose();
                        _location = null;
                        continue;
                    }
                    if (response.StatusCode == HttpStatusCode.PreconditionFailed) throw Changed();
                    if (range != null && response.StatusCode == HttpStatusCode.OK)
                    {
                        if (tag != null && response.Headers.ETag is { } received && !tag.Equals(received)) throw Changed();
                        throw Unsupported();
                    }
                    if (!response.IsSuccessStatusCode) throw Failed();
                    if (tag != null && !tag.Equals(response.Headers.ETag)) throw Changed();
                    if ((range != null ? response.Content.Headers.ContentRange?.Length : response.Content.Headers.ContentLength) is { } actual && actual != length)
                        throw Changed();
                    try { HttpDownloadGuard.ValidateRange(range, response, length, transferStarted: true); }
                    catch (HttpDownloadProtocolException) { throw InvalidRange(); }
                    if (range == null && (response.StatusCode != HttpStatusCode.OK || response.Content.Headers.ContentLength != 0))
                        throw InvalidRange();
                    return response;
                }
                catch { response.Dispose(); throw; }
            }
        }

        private async Task<Uri> ResolveAsync(CancellationToken ct)
        {
            try
            {
                var uri = await resolve(ct);
                PublicCloudHttp.EnsureSafeUri(uri);
                if (uri.Scheme != Uri.UriSchemeHttps || uri.Fragment.Length != 0) throw new ArgumentException();
                return uri;
            }
            catch (AuthorizedOriginalDownloadException) { throw; }
            catch (Exception)
            {
                ct.ThrowIfCancellationRequested();
                throw new AuthorizedOriginalDownloadException("ORIGINAL_HTTP_LOCATION_UNAVAILABLE");
            }
        }
    }

    private static AuthorizedOriginalDownloadException Unsupported() => new("ORIGINAL_HTTP_RESUME_UNSUPPORTED");
    private static AuthorizedOriginalDownloadException Changed() => new("ORIGINAL_HTTP_SOURCE_CHANGED");
    private static AuthorizedOriginalDownloadException InvalidRange() => new("ORIGINAL_HTTP_RANGE_INVALID");
    private static AuthorizedOriginalDownloadException Failed() => new("ORIGINAL_HTTP_TRANSFER_FAILED");
}
