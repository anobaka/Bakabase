using System.Net;
using System.Net.Sockets;
using System.Text;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Network;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bakabase.Modules.ThirdParty.Tests.Bilibili.Download;

/// <summary>
/// The real CDN handler against a loopback socket: what actually goes on the wire, whatever the request carried
/// when it reached the handler (a library's own User-Agent, a cookie).
/// </summary>
[TestClass]
public class BilibiliCdnHttpMessageHandlerTests
{
    private static AspNetCoreOptionsManager<BilibiliOptions> OptionsManager(BilibiliOptions options) =>
        new(Path.Combine(Path.GetTempPath(), $"bili-cdn-{Guid.NewGuid():N}.json"), "bilibili",
            new StaticMonitor<BilibiliOptions>(options), NullLogger<AspNetCoreOptionsManager<BilibiliOptions>>.Instance);

    private static BakabaseWebProxy NoProxy() =>
        new(new StaticBOptions<NetworkOptions>(new NetworkOptions
        {
            Proxy = new NetworkOptions.ProxyModel {Mode = NetworkOptions.ProxyMode.DoNotUse},
        }));

    [TestMethod]
    public async Task ForcesUserAgent_AddsReferer_StripsCookie()
    {
        await using var server = LoopbackServer.Start();
        using var handler = new BilibiliCdnHttpMessageHandler<BilibiliOptions>(
            OptionsManager(new BilibiliOptions {Cookie = "SESSDATA=" + BilibiliSecrets.CookieSentinel}), NoProxy());
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url("/upgcxcode/5001-1-30080.m4s"));
        request.Headers.TryAddWithoutValidation("User-Agent", "Downloader/5.9.6");
        request.Headers.TryAddWithoutValidation("Cookie", "SESSDATA=" + BilibiliSecrets.CookieSentinel);

        using var response = await client.SendAsync(request);

        var headers = await server.Received;
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(InternalOptions.DefaultHttpUserAgent, headers["user-agent"]);
        Assert.AreEqual("https://www.bilibili.com/", headers["referer"]);
        Assert.IsFalse(headers.ContainsKey("cookie"));
    }

    [TestMethod]
    public async Task UserConfiguredUserAgent_Wins()
    {
        await using var server = LoopbackServer.Start();
        using var handler = new BilibiliCdnHttpMessageHandler<BilibiliOptions>(
            OptionsManager(new BilibiliOptions {UserAgent = "Mozilla/5.0 Custom"}), NoProxy());
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync(server.Url("/x.m4s"));

        Assert.AreEqual("Mozilla/5.0 Custom", (await server.Received)["user-agent"]);
    }

    [TestMethod]
    public async Task NamedClientFromDi_UsesTheCdnHandler()
    {
        await using var server = LoopbackServer.Start();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(OptionsManager(new BilibiliOptions {Cookie = "SESSDATA=" + BilibiliSecrets.CookieSentinel}));
        services.AddSingleton(NoProxy());
        var settings = new BilibiliCdnDownloaderSettings {MaxAttemptsPerUrl = 1};
        services.AddSingleton(settings);
        services.AddBilibiliCdn<BilibiliOptions>();
        await using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(InternalOptions.HttpClientNames.BilibiliCdn);
        using var response = await client.GetAsync(server.Url("/x.m4s"));

        var headers = await server.Received;
        Assert.AreEqual(InternalOptions.DefaultHttpUserAgent, headers["user-agent"]);
        Assert.AreEqual("https://www.bilibili.com/", headers["referer"]);
        Assert.IsFalse(headers.ContainsKey("cookie"));
        Assert.AreEqual(Timeout.InfiniteTimeSpan, client.Timeout);
        // Settings registered before the module win (the DI seam for tests).
        Assert.AreSame(settings, provider.GetRequiredService<BilibiliCdnDownloaderSettings>());
        Assert.IsNotNull(provider.GetRequiredService<BilibiliCdnDownloader>());
    }

    [TestMethod]
    public void Apply_OnABareRequest()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://upos-sz-example.bilivideo.com/a.m4s");
        request.Headers.Referrer = new Uri("https://space.bilibili.com/1");
        request.Headers.TryAddWithoutValidation("Cookie", "a=b");

        BilibiliCdnRequestHeaders.Apply(request, null);

        Assert.AreEqual(InternalOptions.DefaultHttpUserAgent, string.Join(" ", request.Headers.GetValues("User-Agent")));
        Assert.AreEqual("https://space.bilibili.com/1", request.Headers.Referrer!.ToString(), "an existing Referer stays");
        Assert.IsFalse(request.Headers.Contains("Cookie"));
    }

    private sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StaticBOptions<T>(T value) : IBOptions<T> where T : class
    {
        public T Value => value;
    }

    /// <summary>Accepts one HTTP/1.1 request, records its headers (lower-case names), answers 200.</summary>
    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task<Dictionary<string, string>> _received;

        private LoopbackServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _received = AcceptOnceAsync();
        }

        public static LoopbackServer Start() => new();

        public Task<Dictionary<string, string>> Received => _received;

        public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint) _listener.LocalEndpoint).Port}{path}";

        private async Task<Dictionary<string, string>> AcceptOnceAsync()
        {
            using var socket = await _listener.AcceptTcpClientAsync();
            await using var stream = socket.GetStream();
            var buffer = new byte[16 * 1024];
            var text = new StringBuilder();
            while (!text.ToString().Contains("\r\n\r\n"))
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                text.Append(Encoding.ASCII.GetString(buffer, 0, read));
            }

            var headers = new Dictionary<string, string>();
            foreach (var line in text.ToString().Split("\r\n").Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0)
                {
                    headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
                }
            }

            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Type: video/mp4\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
            return headers;
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            try
            {
                await _received.WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (Exception)
            {
                // Nothing connected (the test failed earlier).
            }
        }
    }
}
