using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Bakabase.Modules.Downloader.Abstractions;
using Bakabase.Modules.Downloader.Extensions;
using Bakabase.Modules.Downloader.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Downloader.Tests;

[TestClass]
public sealed class StandaloneDownloaderTests
{
    private string _directory = null!;

    [TestInitialize]
    public void Setup() => _directory = Path.Combine(Path.GetTempPath(), "BakabaseStandaloneDownload_" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private ServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddDownloader(_ => Path.Combine(_directory, "cache"));
        return services;
    }

    [TestMethod]
    public async Task StandaloneConsumerDownloadsAFileAndResolvesEveryTransportWithoutApplicationServices()
    {
        await using var server = new FileServer();
        using var provider = Services().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        Assert.IsNotNull(provider.GetRequiredService<ITorrentDownloader>());
        Assert.IsNotNull(provider.GetRequiredService<IAria2Downloader>());
        var progress = new List<int>();
        var path = await provider.GetRequiredService<IHttpDownloader>().DownloadAsync(
            new HttpDownloadRequest(server.Url, Path.Combine(_directory, "nested", "downloads")),
            (percentage, _) => { progress.Add(percentage); return Task.CompletedTask; }, CancellationToken.None);

        Assert.AreEqual("server-file.txt", Path.GetFileName(path));
        Assert.AreEqual(FileServer.Payload, await File.ReadAllTextAsync(path));
        Assert.AreEqual(100, progress.Last());
    }

    [TestMethod]
    public async Task ComponentInstallerCanChooseAFileNameAndItsHostConfiguredHttpClient()
    {
        await using var server = new FileServer();
        var services = Services();
        services.AddHttpClient("component-install", client =>
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-test-token");
        });
        using var provider = services.BuildServiceProvider();
        var path = await provider.GetRequiredService<IHttpDownloader>().DownloadAsync(
            new HttpDownloadRequest(server.Url, _directory)
            {
                FileName = "component.zip",
                HttpClientName = "component-install"
            }, null, CancellationToken.None);

        Assert.AreEqual(Path.Combine(_directory, "component.zip"), path);
        Assert.AreEqual(FileServer.Payload, await File.ReadAllTextAsync(path));
        Assert.IsTrue(server.Authorizations.Count >= 2);
        Assert.IsTrue(server.Authorizations.All(value => value == "Bearer local-test-token"));
    }

    [TestMethod]
    public async Task TimeoutInterruptsTheTransportAndIsReportedAsTimeout()
    {
        var handler = new WaitingHandler();
        var services = Services();
        services.AddHttpClient(DownloaderServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => provider.GetRequiredService<IHttpDownloader>()
            .DownloadAsync(new HttpDownloadRequest("https://download.test/file", _directory)
            {
                Timeout = TimeSpan.FromMilliseconds(100)
            }, null, CancellationToken.None));
        Assert.IsTrue(handler.Canceled);
    }

    [TestMethod]
    public async Task AShorterHostClientTimeoutIsStillReportedAsTimeout()
    {
        var handler = new WaitingHandler();
        var services = Services();
        services.AddHttpClient("short-timeout", client => client.Timeout = TimeSpan.FromMilliseconds(100))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => provider.GetRequiredService<IHttpDownloader>()
            .DownloadAsync(new HttpDownloadRequest("https://download.test/file", _directory)
            {
                HttpClientName = "short-timeout",
                Timeout = TimeSpan.FromMinutes(1)
            }, null, CancellationToken.None));
        Assert.IsTrue(handler.Canceled);
    }

    [TestMethod]
    public async Task Aria2AlsoPreservesAHostClientTimeoutAsTimeout()
    {
        var handler = new WaitingHandler();
        var services = Services();
        services.AddHttpClient(DownloaderServiceCollectionExtensions.HttpClientName,
                client => client.Timeout = TimeSpan.FromMilliseconds(100))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => provider.GetRequiredService<IAria2Downloader>()
            .DownloadMagnetAsync("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
                _directory, new Aria2DownloadOptions(), null, CancellationToken.None));
        Assert.IsTrue(handler.Canceled);
    }

    [TestMethod]
    public async Task CallerCancellationInterruptsTheTransportWithoutBecomingTimeout()
    {
        var handler = new WaitingHandler();
        var services = Services();
        services.AddHttpClient(DownloaderServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var download = provider.GetRequiredService<IHttpDownloader>().DownloadAsync(
            new HttpDownloadRequest("https://download.test/file", _directory), null, cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try { await download; Assert.Fail("Caller cancellation must propagate."); }
        catch (OperationCanceledException) { }
        Assert.IsTrue(handler.Canceled);
    }

    [DataTestMethod]
    [DataRow("../outside.txt")]
    [DataRow("..\\outside.txt")]
    [DataRow("/outside.txt")]
    public async Task ExplicitFileNameCannotEscapeTheCallerDirectory(string name)
    {
        using var provider = Services().BuildServiceProvider();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GetRequiredService<IHttpDownloader>()
            .DownloadAsync(new HttpDownloadRequest("https://download.test/file", _directory)
            {
                FileName = name
            }, null, CancellationToken.None));
        Assert.IsFalse(Directory.Exists(_directory));
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Canceled { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, ct); }
            catch (OperationCanceledException) { Canceled = true; throw; }
            throw new InvalidOperationException("The request should have been canceled.");
        }
    }

    private sealed class FileServer : IAsyncDisposable
    {
        public const string Payload = "A component downloaded without workflows, resources or an application host.";
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public string Url { get; }
        public ConcurrentBag<string?> Authorizations { get; } = [];

        public FileServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/download";
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
                Authorizations.Add(context.Request.Headers["Authorization"]);
                var body = Encoding.UTF8.GetBytes(Payload);
                context.Response.ContentLength64 = body.Length;
                context.Response.Headers["Content-Disposition"] = "attachment; filename=server-file.txt";
                if (context.Request.HttpMethod != "HEAD")
                    await context.Response.OutputStream.WriteAsync(body, _stop.Token);
                context.Response.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Close();
            await _loop;
            _stop.Dispose();
        }
    }
}
