using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bakabase.Modules.ThirdParty.Abstractions.Logging;
using Bakabase.Modules.ThirdParty.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.ThirdParty.Tests;

[TestClass]
public class ExHentaiHttpLogPrivacyTests
{
    private const string Secret = "synthetic-reload-token-keep-private";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task RedactionKeepsFailureDiagnosticsAndOriginalExceptionWhileDefaultLogsStayUnchanged(
        bool synchronous, bool redact)
    {
        var records = new LogCollector();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(records));
        var logger = new ThirdPartyHttpRequestLogger(factory.CreateLogger<ThirdPartyHttpRequestLogger>());
        var failure = new HttpRequestException(HttpRequestError.ConnectionError, "Request failed: " + Secret,
            new IOException("Inner transport error: " + Secret), HttpStatusCode.BadGateway);
        Task Send() => synchronous ? Task.Run(() =>
        {
            if (redact)
                logger.Capture(ThirdPartyId.ExHentai, () => throw failure, "safe-key",
                    buildCustomMessage: (_, _) => Secret, redactExceptionDetails: true);
            else logger.Capture(ThirdPartyId.ExHentai, () => throw failure, "safe-key");
        }) : redact
            ? logger.CaptureAsync(ThirdPartyId.ExHentai, () => Task.FromException<HttpResponseMessage>(failure),
                "safe-key", buildCustomMessage: (_, _) => Secret, redactExceptionDetails: true)
            : logger.CaptureAsync(ThirdPartyId.ExHentai, () => Task.FromException<HttpResponseMessage>(failure),
                "safe-key");

        var thrown = await Assert.ThrowsExceptionAsync<HttpRequestException>(Send);
        Assert.AreSame(failure, thrown, "Redaction must not change retry classification or the caller's error.");
        Assert.AreEqual(HttpRequestError.ConnectionError, thrown.HttpRequestError);
        Assert.AreEqual(HttpStatusCode.BadGateway, thrown.StatusCode);
        var completed = logger.Logs[ThirdPartyId.ExHentai].Single();
        Assert.AreEqual(ThirdPartyRequestResultType.Failed, completed.Result);
        if (redact)
        {
            StringAssert.Contains(completed.Message!, "ConnectionError");
            StringAssert.Contains(completed.Message!, "HTTP 502");
            AssertPrivate(logger, records);
        }
        else
        {
            StringAssert.Contains(completed.Message!, Secret);
            Assert.IsTrue(records.Entries.Any(entry => ReferenceEquals(entry.Exception, failure)),
                "Requests without a redacted key must retain their existing detailed diagnostic logging.");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealHandlerFailureDoesNotLogReloadTokenOrItsTransportException(bool synchronous)
    {
        await using var server = new OneRequestServer("HTTP/1.1 invalid " + Secret + "\r\n\r\n");
        var records = new LogCollector();
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(records));
        var logger = new ThirdPartyHttpRequestLogger(factory.CreateLogger<ThirdPartyHttpRequestLogger>());
        using var handler = new SourceHandler(logger);
        using var client = new HttpClient(handler) {Timeout = Timeout};
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + "s/hash/123-1?nl=" + Secret)
        {
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Options.Set(ThirdPartyRequestOptions.RequestLogKey, server.Url + "s/hash/123-1?nl=[redacted]");
        var originalUri = request.RequestUri;
        var sending = synchronous ? Task.Run(() => client.Send(request)) : client.SendAsync(request);
        var failure = await Assert.ThrowsExceptionAsync<HttpRequestException>(() => sending.WaitAsync(Timeout));
        StringAssert.Contains(await server.RequestLine.Task.WaitAsync(Timeout), "?nl=" + Secret);
        Assert.AreEqual(originalUri, request.RequestUri, "Only the diagnostic key may be redacted.");
        Assert.AreEqual(HttpRequestError.InvalidResponse, failure.HttpRequestError,
            "The caller retains the transport's invalid-response classification.");
        Assert.AreEqual(ThirdPartyRequestResultType.Failed, logger.Logs[ThirdPartyId.ExHentai].Single().Result);
        AssertPrivate(logger, records);
    }

    [TestMethod]
    public async Task FactoryRemovesOnlyExHentaiUriLoggingAndKeepsItsSourceRequestLogs()
    {
        var records = new LogCollector();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(records));
        services.AddThirdParty<BilibiliOptions, BangumiOptions, DLsiteOptions, ExHentaiOptions, PixivOptions,
            SoulPlusOptions, TmdbOptions>();
        foreach (var name in new[] {InternalOptions.HttpClientNames.ExHentai, InternalOptions.HttpClientNames.Bangumi})
        {
            // Isolate the named factory logging policy from accounts/config files. Use the real
            // source handler below, so its request accounting and redacted-key handling still run.
            services.PostConfigure<HttpClientFactoryOptions>(name, options =>
            {
                options.HttpMessageHandlerBuilderActions.Clear();
                options.HttpMessageHandlerBuilderActions.Add(builder =>
                    builder.PrimaryHandler = new SourceHandler(builder.Services.GetRequiredService<ThirdPartyHttpRequestLogger>()));
            });
        }
        using var provider = services.BuildServiceProvider();
        var clients = provider.GetRequiredService<IHttpClientFactory>();
        var logger = provider.GetRequiredService<ThirdPartyHttpRequestLogger>();
        await using (var server = new OneRequestServer("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"))
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.ExHentai);
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url + "image?nl=" + Secret);
            request.Options.Set(ThirdPartyRequestOptions.RequestLogKey, server.Url + "image?nl=[redacted]");
            using var response = await client.SendAsync(request).WaitAsync(Timeout);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            StringAssert.Contains(await server.RequestLine.Task.WaitAsync(Timeout), Secret);
        }
        Assert.AreEqual(1, logger.Logs[ThirdPartyId.ExHentai].Length, "The source request log must remain enabled.");
        Assert.IsFalse(records.Entries.Any(entry => entry.Category.StartsWith(
            "System.Net.Http.HttpClient." + InternalOptions.HttpClientNames.ExHentai + ".", StringComparison.Ordinal)));
        AssertPrivate(logger, records);

        await using (var server = new OneRequestServer("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"))
        {
            using var client = clients.CreateClient(InternalOptions.HttpClientNames.Bangumi);
            using var response = await client.GetAsync(server.Url).WaitAsync(Timeout);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }
        Assert.IsTrue(records.Entries.Any(entry => entry.Category.StartsWith(
            "System.Net.Http.HttpClient." + InternalOptions.HttpClientNames.Bangumi + ".", StringComparison.Ordinal)),
            "Other third-party clients must keep their factory logging.");
    }

    private static void AssertPrivate(ThirdPartyHttpRequestLogger logger, LogCollector records)
    {
        foreach (var log in logger.Logs[ThirdPartyId.ExHentai])
        {
            Assert.IsFalse(log.Key?.Contains(Secret, StringComparison.Ordinal) == true);
            Assert.IsFalse(log.Message?.Contains(Secret, StringComparison.Ordinal) == true);
        }
        Assert.IsFalse(records.Entries.Any(entry => entry.Message.Contains(Secret, StringComparison.Ordinal)));
        Assert.IsTrue(records.Entries.All(entry => entry.Exception == null),
            "Providers must not receive a transport exception that could reveal the real request URI.");
    }

    private sealed class SourceOptions : IThirdPartyHttpClientOptions
    {
        public string? Cookie => null;
        public string? UserAgent => null;
        public string? Referer => null;
        public Dictionary<string, string>? Headers => null;
        public int MaxConcurrency => 1;
        public int RequestInterval => 0;
    }

    private sealed class SourceHandler(ThirdPartyHttpRequestLogger logger)
        : AbstractThirdPartyHttpMessageHandler<SourceOptions>(logger, ThirdPartyId.ExHentai,
            new BakabaseWebProxy(new NetworkOptionsProvider()), new SourceOptions())
    {
        protected override void ConfigureHandler() => UseProxy = false;
    }

    private sealed class NetworkOptionsProvider : IBOptions<NetworkOptions>
    {
        public NetworkOptions Value { get; } = new();
    }

    private sealed class LogCollector : ILoggerProvider
    {
        public ConcurrentQueue<(string Category, string Message, Exception? Exception)> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CollectorLogger(this, categoryName);
        public void Dispose() { }

        private sealed class CollectorLogger(LogCollector collector, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => collector.Entries.Enqueue((category, formatter(state, exception), exception));
        }
    }

    private sealed class OneRequestServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serve;
        public string Url { get; }
        public TaskCompletionSource<string> RequestLine { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OneRequestServer(string response)
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
            _serve = Serve(response);
        }

        private async Task Serve(string response)
        {
            using var connection = await _listener.AcceptTcpClientAsync(_stop.Token);
            await using var stream = connection.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            var first = await reader.ReadLineAsync(_stop.Token);
            while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
            RequestLine.TrySetResult(first!);
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), _stop.Token);
            await stream.FlushAsync(_stop.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            try { await _serve; }
            catch (OperationCanceledException) { }
            _stop.Dispose();
        }
    }
}
