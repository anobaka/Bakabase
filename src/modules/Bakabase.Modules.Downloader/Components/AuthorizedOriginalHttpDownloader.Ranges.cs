using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Components;

internal sealed partial class AuthorizedOriginalHttpDownloader
{
    private sealed class RangeWriter(FileStream file, FileStream ledger, OriginalConnection origin,
        EntityTagHeaderValue tag, AuthorizedOriginalRangeBudget.Client policy, long length,
        Func<HttpTransferProgress, Task>? progress)
    {
        private long _committed = file.Position;
        private readonly RangeProgress _reporter = new(file.Position, length, progress);

        internal async Task WriteAsync(CancellationToken ct)
        {
            using var disk = FileIoObservationScope.Begin(read: false, write: true);
            using var network = FileIoObservationScope.BeginNetworkReceive(FileIoObservationScope.NetworkDownloadId);
            while (_committed < length)
            {
                var width = origin.Throttled ? 1 : policy.WindowSize(length);
                var remaining = 1 + (length - _committed - 1) / AuthorizedOriginalCheckpointStore.SegmentBytes;
                var extras = policy.TakeExtra((int)Math.Min(width, remaining) - 1);
                RateLimited? throttled = null;
                try
                {
                    if (extras == 0)
                    {
                        var size = SegmentLength(_committed);
                        var digest = await ReceiveSegmentAsync(origin, tag, _committed, size, file, network, _reporter, disk, ct);
                        await CommitAsync(digest, size, ct);
                    }
                    else await WriteWindowAsync(extras + 1, network, disk, ct);
                }
                catch (RateLimited limit) { throttled = limit; }
                finally { policy.Release(extras); }

                if (throttled != null)
                {
                    await origin.BackOffAsync(throttled, ct);
                    _reporter.Reset(_committed);
                }
            }
        }

        private int SegmentLength(long offset) =>
            (int)Math.Min(AuthorizedOriginalCheckpointStore.SegmentBytes, length - offset);

        private async Task CommitAsync(byte[] digest, int size, CancellationToken ct)
        {
            file.Flush(flushToDisk: true);
            await ledger.WriteAsync(digest, ct);
            ledger.Flush(flushToDisk: true);
            _committed += size;
        }

        private async Task WriteWindowAsync(int count, INetworkReceiveObservation? network,
            IFileIoObservation? disk, CancellationToken ct)
        {
            using var wave = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var segments = new List<(byte[] Buffer, int Size, Task<byte[]> Read)>(count);
            var offset = _committed;
            try
            {
                for (var i = 0; i < count; i++)
                {
                    var size = SegmentLength(offset);
                    var buffer = policy.Rent();
                    segments.Add((buffer, size, FetchAsync(offset, size, buffer)));
                    offset += size;
                }
                // A finished later range is speculative until every preceding one is committed.
                foreach (var segment in segments)
                {
                    var digest = await segment.Read;
                    await file.WriteAsync(segment.Buffer.AsMemory(0, segment.Size), ct);
                    disk?.Add(0, segment.Size);
                    await CommitAsync(digest, segment.Size, ct);
                }
            }
            catch (Exception first)
            {
                await wave.CancelAsync();
                try { await Task.WhenAll(segments.Select(s => s.Read)); } catch { /* Join every reader before returning its buffer. */ }
                ct.ThrowIfCancellationRequested();
                var errors = segments.Select(s => s.Read.Exception?.InnerException).ToArray();
                var invalid = errors.OfType<AuthorizedOriginalDownloadException>().FirstOrDefault(error =>
                    error.Code is "ORIGINAL_HTTP_SOURCE_CHANGED" or "ORIGINAL_HTTP_RANGE_INVALID" or "ORIGINAL_HTTP_RESUME_UNSUPPORTED");
                if (invalid != null) throw new AuthorizedOriginalDownloadException(invalid.Code);
                if (first is RateLimited)
                    throw errors.OfType<RateLimited>().OrderByDescending(e => e.Delay).First();
                throw;
            }
            finally
            {
                foreach (var segment in segments) policy.Return(segment.Buffer);
            }

            async Task<byte[]> FetchAsync(long start, int size, byte[] buffer)
            {
                using var target = new MemoryStream(buffer, 0, size, writable: true);
                return await ReceiveSegmentAsync(origin, tag, start, size, target, network, _reporter, null, wave.Token);
            }
        }
    }

    private static async Task<byte[]> ReceiveSegmentAsync(OriginalConnection origin, EntityTagHeaderValue tag,
        long start, int length, Stream destination, INetworkReceiveObservation? network, RangeProgress progress,
        IFileIoObservation? disk, CancellationToken ct)
    {
        using var response = await origin.OpenRangeAsync(start, start + (length - 1), tag, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var stream = await OpenBodyAsync(response.Content, ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var buffer = new byte[81920];
        var remaining = length;
        while (remaining > 0)
        {
            var read = await ReadBodyAsync(stream, buffer.AsMemory(0, Math.Min(remaining, buffer.Length)), deadline, ct);
            if (read == 0) throw Failed();
            network?.AddReceivedBytes(read);
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            disk?.Add(0, read);
            hash.AppendData(buffer, 0, read);
            remaining -= read;
            await progress.ReceivedAsync(read);
        }
        if (await ReadBodyAsync(stream, buffer.AsMemory(0, 1), deadline, ct) != 0) throw InvalidRange();
        return hash.GetHashAndReset();
    }

    private sealed class RangeProgress(long start, long length, Func<HttpTransferProgress, Task>? progress)
    {
        private long _received = start, _lastBytes = start, _lastTick = Stopwatch.GetTimestamp();
        private int _reporting;
        internal void Reset(long offset)
        {
            _received = _lastBytes = offset;
            _lastTick = Stopwatch.GetTimestamp();
        }

        internal async ValueTask ReceivedAsync(int count)
        {
            if (progress == null) return;
            Interlocked.Add(ref _received, count);
            var now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(Volatile.Read(ref _lastTick), now).TotalMilliseconds < 500 ||
                Interlocked.CompareExchange(ref _reporting, 1, 0) != 0) return;
            try
            {
                var bytes = Interlocked.Read(ref _received);
                var elapsed = Stopwatch.GetElapsedTime(_lastTick, now);
                if (elapsed.TotalMilliseconds < 500) return;
                var update = Snapshot(bytes, length, (bytes - _lastBytes) / elapsed.TotalSeconds);
                _lastBytes = bytes;
                Volatile.Write(ref _lastTick, now);
                await progress(update);
            }
            finally { Volatile.Write(ref _reporting, 0); }
        }
    }

}
