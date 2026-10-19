using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Bakabase.Modules.HttpDownloads.Tests;

internal static class HttpTestServer
{
    internal sealed record PayloadRequest(long Start, long End);

    internal sealed class Server : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _handlers = [];
        private readonly Task _loop;
        private int _active;
        public byte[] Body = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
        public string? ETag = "\"one-stable-version\"";
        public bool Slow;
        public bool IgnoreRanges;
        public bool FailRequests;
        public string? ContentDisposition;
        public int MaxActive;
        public long BytesWritten;
        public ConcurrentQueue<PayloadRequest> Requests { get; } = new();
        public string Url { get; }

        public Server()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/file.bin";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _loop = Serve();
        }

        private async Task Serve()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync().WaitAsync(_stop.Token); }
                catch (Exception) when (_stop.IsCancellationRequested) { break; }
                _handlers.Add(Reply(context));
            }
        }

        private async Task Reply(HttpListenerContext context)
        {
            var active = false;
            try
            {
                var body = Body;
                var response = context.Response;
                if (FailRequests) { response.StatusCode = 403; response.Close(); return; }
                if (ContentDisposition != null) response.Headers["Content-Disposition"] = ContentDisposition;
                response.Headers["Accept-Ranges"] = "bytes";
                if (ETag != null) response.Headers["ETag"] = ETag;
                response.ContentLength64 = body.Length;
                if (context.Request.HttpMethod == "HEAD") { response.Close(); return; }
                var from = 0;
                var to = body.Length - 1;
                if (!IgnoreRanges && context.Request.Headers["Range"] is { } range)
                {
                    var offsets = range[6..].Split('-');
                    from = int.Parse(offsets[0]);
                    if (!string.IsNullOrEmpty(offsets[1])) to = int.Parse(offsets[1]);
                    response.StatusCode = 206;
                    response.Headers["Content-Range"] = $"bytes {from}-{to}/{body.Length}";
                }
                response.ContentLength64 = to - from + 1;
                if (to - from > 1)
                {
                    active = true;
                    Requests.Enqueue(new PayloadRequest(from, to));
                    var count = Interlocked.Increment(ref _active);
                    int previous;
                    do { previous = MaxActive; if (previous >= count) break; }
                    while (Interlocked.CompareExchange(ref MaxActive, count, previous) != previous);
                }
                for (var position = from; position <= to; position += 16 * 1024)
                {
                    var length = Math.Min(16 * 1024, to - position + 1);
                    await response.OutputStream.WriteAsync(body.AsMemory(position, length), _stop.Token);
                    await response.OutputStream.FlushAsync(_stop.Token);
                    if (active) Interlocked.Add(ref BytesWritten, length);
                    if (Slow && active) await Task.Delay(20, _stop.Token);
                }
                response.Close();
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or OperationCanceledException)
            {
                context.Response.Abort();
            }
            finally { if (active) Interlocked.Decrement(ref _active); }
        }

        public async Task DrainPayloads()
        {
            using var guard = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Volatile.Read(ref _active) > 0) await Task.Delay(10, guard.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            await _loop;
            await Task.WhenAll(_handlers);
            _stop.Dispose();
        }
    }
}
