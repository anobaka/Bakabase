using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ThirdPartyHttpMessageHandlerConcurrencyTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FiveRequestsCanReachTheServerBeforeAnyResponseHeadersArrive(bool synchronous)
    {
        await using var server = new DelayedHeadersServer();
        using var handler = new TestHandler(new TestOptions {MaxConcurrency = 5});
        using var client = new HttpClient(handler) {Timeout = Timeout};
        Task<HttpResponseMessage> Send() => synchronous
            ? Task.Run(() => client.Send(new HttpRequestMessage(HttpMethod.Get, server.Url)))
            : client.GetAsync(server.Url);
        var sends = Enumerable.Range(0, 5).Select(_ => Send()).ToList();

        // Every response is withheld. A lock held across SendAsync lets only the first
        // request reach the server, regardless of the configured concurrency.
        var pending = new List<PendingRequest>();
        for (var i = 0; i < sends.Count; i++) pending.Add(await server.NextAsync());
        sends.Add(Send());
        var sixthArrival = server.NextAsync();
        await Task.Delay(100);
        Assert.IsFalse(sixthArrival.IsCompleted, "A sixth request exceeded the configured limit of five.");
        pending[0].Respond();
        (await sixthArrival).Respond();
        foreach (var request in pending.Skip(1)) request.Respond();
        foreach (var response in await Task.WhenAll(sends)) response.Dispose();
    }

    [TestMethod]
    public async Task RequestsQueuedBehindSlowHeadersStillObserveTheStartInterval()
    {
        const int intervalMs = 100;
        await using var server = new DelayedHeadersServer();
        using var handler = new TestHandler(new TestOptions {MaxConcurrency = 1, RequestInterval = intervalMs});
        using var client = new HttpClient(handler) {Timeout = Timeout};
        var first = client.GetAsync(server.Url);
        var pendingFirst = await server.NextAsync();
        var queued = Enumerable.Range(0, 3).Select(_ => client.GetAsync(server.Url)).ToArray();
        // All callers have already waited 100 ms before the first response releases capacity.
        // Checking the interval only before queueing makes the remaining requests burst together.
        await Task.Delay(intervalMs * 2);
        pendingFirst.Respond();
        var arrivals = new List<long>();
        for (var i = 0; i < queued.Length; i++)
        {
            var pending = await server.NextAsync();
            arrivals.Add(pending.ArrivedAt);
            pending.Respond();
        }

        for (var i = 1; i < arrivals.Count; i++)
            Assert.IsTrue(Stopwatch.GetElapsedTime(arrivals[i - 1], arrivals[i]).TotalMilliseconds >= intervalMs - 15,
                "Queued requests started in a burst instead of observing the configured interval.");
        (await first).Dispose();
        foreach (var response in await Task.WhenAll(queued)) response.Dispose();
    }

    [TestMethod]
    public async Task SlowRequestPreparationDoesNotMoveTheStartIntervalBeforeTheActualSend()
    {
        const int intervalMs = 150;
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releasePreparation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = new DelayedHeadersServer();
        using var handler = new TestHandler(new TestOptions {MaxConcurrency = 2, RequestInterval = intervalMs},
            async request =>
            {
                if (request.RequestUri!.Query == "?slow")
                {
                    preparing.TrySetResult();
                    await releasePreparation.Task;
                }
            });
        using var client = new HttpClient(handler) {Timeout = Timeout};
        var slow = client.GetAsync(server.Url + "?slow");
        await preparing.Task.WaitAsync(Timeout);
        var fast = client.GetAsync(server.Url);
        var pendingFast = await server.NextAsync();
        await Task.Delay(50);
        releasePreparation.TrySetResult();
        var pendingSlow = await server.NextAsync();
        Assert.IsTrue(Stopwatch.GetElapsedTime(pendingFast.ArrivedAt, pendingSlow.ArrivedAt).TotalMilliseconds >= intervalMs - 15,
            "The interval must be checked after request preparation, immediately before sending.");
        pendingFast.Respond();
        pendingSlow.Respond();
        (await slow).Dispose();
        (await fast).Dispose();
    }

    [TestMethod]
    public async Task LoweringThenRaisingConcurrencyUsesTheCurrentLimitWithoutBlockingTheUpdate()
    {
        await using var server = new DelayedHeadersServer();
        using var handler = new TestHandler(new TestOptions {MaxConcurrency = 2});
        using var client = new HttpClient(handler) {Timeout = Timeout};
        var first = client.GetAsync(server.Url);
        var second = client.GetAsync(server.Url);
        var pendingFirst = await server.NextAsync();
        var pendingSecond = await server.NextAsync();
        await Task.Run(() => handler.Update(new TestOptions {MaxConcurrency = 1})).WaitAsync(Timeout);

        var third = client.GetAsync(server.Url);
        var thirdArrival = server.NextAsync();
        await Task.Delay(100);
        Assert.IsFalse(thirdArrival.IsCompleted);
        pendingFirst.Respond();
        await Task.WhenAny(first, second);
        await Task.Delay(100);
        Assert.IsFalse(thirdArrival.IsCompleted, "The remaining request still occupies the reduced limit.");

        handler.Update(new TestOptions {MaxConcurrency = 2});
        var pendingThird = await thirdArrival;
        var fourth = client.GetAsync(server.Url);
        var fourthArrival = server.NextAsync();
        await Task.Delay(100);
        Assert.IsFalse(fourthArrival.IsCompleted, "Raising a limit must not create extra permits.");
        pendingSecond.Respond();
        (await fourthArrival).Respond();
        pendingThird.Respond();
        foreach (var response in await Task.WhenAll(first, second, third, fourth)) response.Dispose();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancelingAQueuedRequestDoesNotConsumeCapacity(bool waitingForInterval)
    {
        await using var server = new DelayedHeadersServer();
        using var handler = new TestHandler(new TestOptions
            {MaxConcurrency = 1, RequestInterval = waitingForInterval ? 1000 : 0});
        using var client = new HttpClient(handler) {Timeout = Timeout};
        var first = client.GetAsync(server.Url);
        var pendingFirst = await server.NextAsync();
        if (waitingForInterval)
        {
            pendingFirst.Respond();
            (await first).Dispose();
        }

        using var cancel = new CancellationTokenSource();
        var canceled = client.GetAsync(server.Url, cancel.Token);
        await Task.Delay(100);
        await cancel.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => canceled);
        if (!waitingForInterval)
        {
            pendingFirst.Respond();
            (await first).Dispose();
        }

        // Changing the interval also wakes waiters; no old delay or canceled admission remains.
        handler.Update(new TestOptions {MaxConcurrency = 1, RequestInterval = 0});
        var subsequent = client.GetAsync(server.Url);
        (await server.NextAsync()).Respond();
        (await subsequent).Dispose();
    }

    [TestMethod]
    public async Task CancelingAnInFlightRequestReleasesCapacity()
    {
        await using var server = new DelayedHeadersServer();
        using var handler = new TestHandler(new TestOptions {MaxConcurrency = 1});
        using var client = new HttpClient(handler) {Timeout = Timeout};
        using var cancel = new CancellationTokenSource();
        var canceled = client.GetAsync(server.Url, cancel.Token);
        await server.NextAsync();
        await cancel.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => canceled);

        var subsequent = client.GetAsync(server.Url);
        (await server.NextAsync()).Respond();
        (await subsequent).Dispose();
    }

    private sealed class TestOptions : IThirdPartyHttpClientOptions
    {
        public string? Cookie { get; set; }
        public string? UserAgent { get; set; }
        public string? Referer { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public int MaxConcurrency { get; set; } = 1;
        public int RequestInterval { get; set; }
    }

    private sealed class TestHandler(TestOptions options, Func<HttpRequestMessage, Task>? beforeRequesting = null)
        : AbstractThirdPartyHttpMessageHandler<TestOptions>(
            new ThirdPartyHttpRequestLogger(NullLogger<ThirdPartyHttpRequestLogger>.Instance),
            ThirdPartyId.ExHentai, new BakabaseWebProxy(new StubOptions()), options)
    {
        public void Update(TestOptions options) => Options = options;

        protected override void ConfigureHandler() => UseProxy = false;

        protected override async Task BeforeRequestingAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (beforeRequesting != null) await beforeRequesting(request);
            await base.BeforeRequestingAsync(request, ct);
        }

        private sealed class StubOptions : IBOptions<NetworkOptions>
        {
            public NetworkOptions Value { get; } = new();
        }
    }

    private sealed class PendingRequest
    {
        public long ArrivedAt { get; } = Stopwatch.GetTimestamp();
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Respond() => Ready.TrySetResult();
    }

    /// <summary>Real HTTP transport with response headers explicitly released by each test.</summary>
    private sealed class DelayedHeadersServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentBag<Task> _connections = new();
        private readonly Channel<PendingRequest> _requests = Channel.CreateUnbounded<PendingRequest>();
        private readonly Task _accept;

        public DelayedHeadersServer()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint) _listener.LocalEndpoint).Port}/";
            _accept = AcceptAsync();
        }

        public string Url { get; }
        public async Task<PendingRequest> NextAsync() =>
            await _requests.Reader.ReadAsync(_stop.Token).AsTask().WaitAsync(Timeout);

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                var socket = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(HandleAsync(socket));
            }
        }

        private async Task HandleAsync(TcpClient socket)
        {
            using (socket)
            {
                await using var stream = socket.GetStream();
                var buffer = new byte[4096];
                var headers = new StringBuilder();
                while (!headers.ToString().Contains("\r\n\r\n"))
                {
                    var read = await stream.ReadAsync(buffer, _stop.Token);
                    if (read == 0) return;
                    headers.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                var pending = new PendingRequest();
                await _requests.Writer.WriteAsync(pending, _stop.Token);
                await pending.Ready.Task.WaitAsync(_stop.Token);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), _stop.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _accept; } catch (OperationCanceledException) { } catch (SocketException) { }
            try { await Task.WhenAll(_connections); } catch (OperationCanceledException) { }
            _stop.Dispose();
        }
    }
}
