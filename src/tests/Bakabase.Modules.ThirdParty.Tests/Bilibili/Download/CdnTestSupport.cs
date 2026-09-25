using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili.Download;

/// <summary>What a fake CDN saw of one request.</summary>
internal sealed record CdnRequest(
    string Url,
    long? RangeFrom,
    string? UserAgent,
    string? Referer,
    bool HasCookie,
    string? AcceptEncoding);

/// <summary>
/// A scripted CDN: per URL, a queue of responders (the last one repeats). Records every request.
/// </summary>
internal sealed class ScriptedCdn : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>>> _scripts = new();
    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>> _last = new();

    public ConcurrentQueue<CdnRequest> Requests { get; } = new();

    public IReadOnlyList<CdnRequest> RequestsTo(string url) => Requests.Where(r => r.Url == url).ToList();

    public ScriptedCdn On(string url, params Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>[] responders)
    {
        var queue = _scripts.GetOrAdd(url, _ => new ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>>());
        foreach (var r in responders)
        {
            queue.Enqueue(r);
        }

        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var url = request.RequestUri!.ToString();
        Requests.Enqueue(new CdnRequest(url, request.Headers.Range?.Ranges.FirstOrDefault()?.From,
            request.Headers.TryGetValues("User-Agent", out var ua) ? string.Join(" ", ua) : null,
            request.Headers.Referrer?.ToString(),
            request.Headers.Contains("Cookie"),
            request.Headers.TryGetValues("Accept-Encoding", out var ae) ? string.Join(",", ae) : null));

        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage>? responder = null;
        if (_scripts.TryGetValue(url, out var queue) && queue.TryDequeue(out var next))
        {
            responder = next;
            _last[url] = next;
        }
        else if (_last.TryGetValue(url, out var last))
        {
            responder = last;
        }

        return Task.FromResult(responder?.Invoke(request, ct) ?? new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("no route"),
        });
    }
}

/// <summary>Responders for <see cref="ScriptedCdn"/>.</summary>
internal static class Cdn
{
    public static byte[] Payload(int length, int seed = 7)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    /// <summary>Honours Range (206) or serves everything (200); <paramref name="failAt"/> is an absolute offset at
    /// which the body breaks off (IOException); <paramref name="stallAt"/> one at which it stops sending.</summary>
    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Serve(byte[] content,
        long? failAt = null, long? stallAt = null, bool ignoreRange = false, long? rangeStartOverride = null,
        long? totalOverride = null, Action? onReachedFailPoint = null) =>
        (request, _) =>
        {
            var from = ignoreRange ? 0 : request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            var partial = !ignoreRange && request.Headers.Range != null;
            if (partial && from >= content.Length)
            {
                var rsp416 = new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    Content = new ByteArrayContent([]),
                };
                rsp416.Content.Headers.ContentRange = new ContentRangeHeaderValue(content.Length);
                return rsp416;
            }

            var body = new ScriptedStream(content, (int) from, failAt, stallAt, onReachedFailPoint);
            var response = new HttpResponseMessage(partial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(body),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
            response.Content.Headers.ContentLength = content.Length - from;
            if (partial)
            {
                var start = rangeStartOverride ?? from;
                response.Content.Headers.ContentRange =
                    new ContentRangeHeaderValue(start, start + (content.Length - from) - 1,
                        totalOverride ?? content.Length);
            }

            return response;
        };

    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Status(HttpStatusCode status) =>
        (_, _) => new HttpResponseMessage(status) {Content = new ByteArrayContent([])};

    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Html(
        HttpStatusCode status = HttpStatusCode.OK) =>
        (_, _) => new HttpResponseMessage(status)
        {
            Content = new StringContent("<html>error</html>", Encoding.UTF8, "text/html"),
        };

    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Throw(Exception e) =>
        (_, _) => throw e;

    public static Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> Bytes(byte[] content,
        string mediaType = "application/octet-stream", string? contentEncoding = null, bool announceLength = true) =>
        (_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                // Non-seekable when the length is not announced, so HttpContent cannot compute it either.
                Content = new StreamContent(announceLength
                    ? new MemoryStream(content)
                    : new ScriptedStream(content, 0, null, null, null)),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            if (announceLength)
            {
                response.Content.Headers.ContentLength = content.Length;
            }

            if (contentEncoding != null)
            {
                response.Content.Headers.ContentEncoding.Add(contentEncoding);
            }

            return response;
        };

    private sealed class ScriptedStream(byte[] content, int start, long? failAt, long? stallAt, Action? onReachedFailPoint)
        : Stream
    {
        private int _position = start;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (stallAt is { } stall && _position >= stall)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (failAt is { } fail && _position >= fail)
            {
                onReachedFailPoint?.Invoke();
                ct.ThrowIfCancellationRequested();
                throw new IOException("The response ended prematurely (simulated).");
            }

            var limit = content.Length;
            if (failAt is { } f && f < limit)
            {
                limit = (int) f;
            }

            if (stallAt is { } s && s < limit)
            {
                limit = (int) s;
            }

            // Small chunks, so progress and failures happen mid-body.
            var count = Math.Min(Math.Min(buffer.Length, limit - _position), 4096);
            if (count <= 0)
            {
                return 0;
            }

            content.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            await Task.Yield();
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>A clock set <paramref name="offset"/> away from the real one; optionally jumping on each timestamp.</summary>
internal sealed class ShiftedTimeProvider(TimeSpan offset, TimeSpan stepPerTimestamp = default) : TimeProvider
{
    private long _extraTicks;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + offset;

    public override long GetTimestamp()
    {
        var extra = Interlocked.Add(ref _extraTicks, stepPerTimestamp.Ticks) - stepPerTimestamp.Ticks;
        return System.GetTimestamp() + (long) (extra * (double) System.TimestampFrequency / TimeSpan.TicksPerSecond);
    }
}

/// <summary>A write stream that fails like a full disk after <paramref name="failAfter"/> bytes.</summary>
internal sealed class DiskFullStream(Stream inner, long failAfter) : Stream
{
    private long _written;

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (_written + buffer.Length > failAfter)
        {
            throw new IOException("There is not enough space on the disk.", unchecked((int) 0x80070070));
        }

        _written += buffer.Length;
        await inner.WriteAsync(buffer, ct);
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => inner.FlushAsync(ct);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => inner.DisposeAsync();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
