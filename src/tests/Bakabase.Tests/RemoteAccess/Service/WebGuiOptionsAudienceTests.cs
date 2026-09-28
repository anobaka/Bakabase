using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// What a UI hub connection is sent of the options, by who holds it: over a real listener,
/// through the Service's gates and its hub filter, with the WebSocket a browser opens.
/// </summary>
/// <remarks>
/// <para>
/// The options carry third-party cookies, API keys and proxy passwords. An Enabled server
/// lets an unpaired browser on the LAN hold a hub connection so it can browse the library,
/// and every one of them used to reach it, at connect and on every change (#1455).
/// </para>
/// <para>
/// The hub here, <see cref="OptionsHub"/>, sends the options exactly as <c>WebGuiHub</c>
/// does — <see cref="WebGuiOptionsAudience.SendAllAsync"/> — and nothing else, since the rest
/// of <c>WebGuiHub</c>'s initial data needs the database. Changes go out through
/// <see cref="WebGuiOptionsAudience.PublishAsync"/>, as <c>WebGuiHubConfigurationAdapter</c>
/// sends them.
/// </para>
/// </remarks>
[TestClass]
public class WebGuiOptionsAudienceTests
{
    private const string LanAddress = "192.168.1.50";
    private const string ApiKey = "tmdb-api-key-canary";
    private const string Cookie = "SESSDATA=cookie-canary";
    private const string ProxyPassword = "proxy-password-canary";
    private const string HostPath = "/srv/host-path-canary";

    private static readonly string[] Secrets = [ApiKey, Cookie, ProxyPassword, HostPath];

    /// <summary>What a browser on another device needs to browse, and all it is sent.</summary>
    private static readonly string[] BrowsingOptions = ["appOptions", "uIOptions", "uIStyleOptions", "resourceOptions"];

    private static readonly string[] AllOptions =
        [..BrowsingOptions, "tmdbOptions", "bilibiliOptions", "networkOptions"];

    public sealed class OptionsHub(BakabaseOptionsManagerPool optionsManagerPool) : Hub<IWebGuiClient>
    {
        public Task GetInitialData() => WebGuiOptionsAudience.SendAllAsync(optionsManagerPool, Context, Clients.Caller);
    }

    private static async Task<ServiceGateHost> StartAsync(RemoteAccessMode mode)
    {
        var host = await ServiceGateHost.StartAsync([typeof(RemoteAccessController)], services =>
        {
            // As BakabaseStartup registers it.
            services.AddSingleton<RemoteAccessHubFilter>();
            services.Configure<HubOptions>(o => o.AddFilter<RemoteAccessHubFilter>());

            Options(services, new AppOptions {Language = "en-US", UiTheme = UiTheme.Dark, WwwRootPath = HostPath});
            Options(services, new UIOptions {IsMenuCollapsed = true});
            Options(services, new UIStyleOptions());
            Options(services, new ResourceOptions {HideChildren = true});
            Options(services, new TmdbOptions {ApiKey = ApiKey, Cookie = Cookie});
            Options(services, new BilibiliOptions {Cookie = Cookie});
            Options(services, new NetworkOptions
            {
                CustomProxies =
                [
                    new NetworkOptions.ProxyOptions
                    {
                        Id = "p", Address = "http://proxy.example:8080",
                        Credentials = new NetworkOptions.ProxyOptions.ProxyCredentials
                            {Username = "me", Password = ProxyPassword}
                    }
                ]
            });
            services.AddSingleton<BakabaseOptionsManagerPool>();
        }, mapHubs: endpoints => endpoints.MapHub<OptionsHub>("/hub/ui"));

        host.Remote.Mode = mode;
        return host;
    }

    private static void Options<T>(IServiceCollection services, T value) where T : class =>
        services.AddSingleton<IBOptionsManagerInternal>(new TestBOptionsManager<T>(value));

    /// <summary>A change, sent as <c>WebGuiHubConfigurationAdapter</c> sends it.</summary>
    private static Task PublishAsync<T>(ServiceGateHost host, T options) where T : class =>
        WebGuiOptionsAudience.PublishAsync(
            host.Services.GetRequiredService<IHubContext<OptionsHub, IWebGuiClient>>().Clients, typeof(T), options);

    private static async Task<PairingCredentials> PairAsync(ServiceGateHost host)
    {
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var code = await devices.IssuePairingCodeAsync();
        return (await devices.PairWithCodeAsync(code.Code, "Laptop", RemoteDevicePlatform.Windows)).Credentials!;
    }

    private static void AssertNoSecret(IEnumerable<string> records, string because)
    {
        foreach (var record in records)
        foreach (var secret in Secrets)
        {
            Assert.IsFalse(record.Contains(secret, StringComparison.Ordinal), $"{because}: sent {secret}");
        }
    }

    [TestMethod]
    public async Task An_unpaired_browser_of_an_Enabled_server_is_sent_only_what_browsing_reads()
    {
        await using var host = await StartAsync(RemoteAccessMode.Enabled);
        await using var browser = await HubClient.ConnectAsync(host, LanAddress);

        var initial = await browser.GetInitialDataAsync();

        CollectionAssert.AreEquivalent(BrowsingOptions, initial.Keys.ToArray());
        AssertNoSecret(browser.Records, "at connect");
        // What it is sent is still what the app starts in.
        Assert.AreEqual("en-US", initial["appOptions"].GetProperty("language").GetString());
        Assert.AreEqual((int) UiTheme.Dark, initial["appOptions"].GetProperty("uiTheme").GetInt32());
        Assert.IsTrue(initial["uIOptions"].GetProperty("isMenuCollapsed").GetBoolean());
        Assert.IsTrue(initial["resourceOptions"].GetProperty("hideChildren").GetBoolean());

        // A change to what it may not read never reaches it; a change to what it may, does.
        await PublishAsync(host, new TmdbOptions {ApiKey = ApiKey + "-changed"});
        await PublishAsync(host, new UIOptions {IsMenuCollapsed = false});

        var changed = await browser.ReceiveOptionsUntilAsync("uIOptions");

        CollectionAssert.AreEqual(new[] {"uIOptions"}, changed.Select(c => c.Name).ToArray());
        Assert.IsFalse(changed[0].Options.GetProperty("isMenuCollapsed").GetBoolean());
        AssertNoSecret(browser.Records, "after a change");
    }

    [TestMethod]
    public async Task This_machine_and_a_paired_device_are_sent_every_options_object()
    {
        await using var host = await StartAsync(RemoteAccessMode.Enabled);
        var laptop = await PairAsync(host);
        await using var window = await HubClient.ConnectAsync(host, null);
        await using var paired = await HubClient.ConnectAsync(host, LanAddress, laptop);

        foreach (var client in new[] {window, paired})
        {
            var initial = await client.GetInitialDataAsync();

            CollectionAssert.AreEquivalent(AllOptions, initial.Keys.ToArray());
            Assert.AreEqual(ApiKey, initial["tmdbOptions"].GetProperty("apiKey").GetString());
            Assert.AreEqual(HostPath, initial["appOptions"].GetProperty("wwwRootPath").GetString());
        }

        await PublishAsync(host, new TmdbOptions {ApiKey = ApiKey + "-changed"});

        foreach (var client in new[] {window, paired})
        {
            var changed = await client.ReceiveOptionsUntilAsync("tmdbOptions");
            Assert.AreEqual(ApiKey + "-changed", changed.Single().Options.GetProperty("apiKey").GetString());
        }
    }

    [TestMethod]
    public async Task Leaving_Unrestricted_hangs_up_on_the_browsers_it_trusted()
    {
        // An Unrestricted server's browser belongs to the operator and is sent everything. Once
        // the server is only Enabled, that browser is an unpaired one like any other.
        await using var host = await StartAsync(RemoteAccessMode.Unrestricted);
        var laptop = await PairAsync(host);
        await using var browser = await HubClient.ConnectAsync(host, LanAddress);
        await using var paired = await HubClient.ConnectAsync(host, LanAddress, laptop);

        CollectionAssert.AreEquivalent(AllOptions, (await browser.GetInitialDataAsync()).Keys.ToArray());
        CollectionAssert.AreEquivalent(AllOptions, (await paired.GetInitialDataAsync()).Keys.ToArray());

        var switched = await host.SendAsync(HttpMethod.Put, "/remote-access/mode", json: """{"mode":1}""");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, switched.StatusCode);
        Assert.AreEqual(RemoteAccessMode.Enabled, host.Remote.Mode);

        await browser.AssertClosedAsync();

        // The paired device keeps its connection and what it is sent.
        await PublishAsync(host, new TmdbOptions {ApiKey = ApiKey + "-changed"});
        Assert.AreEqual(ApiKey + "-changed",
            (await paired.ReceiveOptionsUntilAsync("tmdbOptions")).Single().Options.GetProperty("apiKey").GetString());

        // Back again, the browser is judged by the mode as it is now.
        await using var again = await HubClient.ConnectAsync(host, LanAddress);
        CollectionAssert.AreEquivalent(BrowsingOptions, (await again.GetInitialDataAsync()).Keys.ToArray());
        AssertNoSecret(again.Records, "reconnected on Enabled");
    }

    [TestMethod]
    public async Task A_connection_that_falls_back_to_long_polling_is_judged_the_same()
    {
        // Long polling hands the hub a copy of the request that opened the connection, not
        // the request itself: what the gate left on it has to survive the copy, or this
        // machine's own window would be taken for a stranger.
        await using var host = await StartAsync(RemoteAccessMode.Enabled);

        foreach (var (remoteIp, expected) in new[] {((string?) null, AllOptions), (LanAddress, BrowsingOptions)})
        {
            var because = remoteIp ?? "this machine";
            HttpRequestMessage Request(HttpMethod method, string path, string? body = null)
            {
                var request = host.Request(method, path);
                if (body != null)
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "text/plain");
                }

                if (remoteIp != null)
                {
                    request.Headers.Add(ServiceGateHost.RemoteIpHeader, remoteIp);
                }

                return request;
            }

            var negotiated = await host.SendAsync(Request(HttpMethod.Post, "/hub/ui/negotiate?negotiateVersion=1"));
            using var negotiation = JsonDocument.Parse(await negotiated.Content.ReadAsStringAsync());
            var token = negotiation.RootElement.GetProperty("connectionToken").GetString()!;
            var connection = $"/hub/ui?id={Uri.EscapeDataString(token)}";

            // The first poll only starts the connection.
            await host.SendAsync(Request(HttpMethod.Get, connection));
            await host.SendAsync(Request(HttpMethod.Post, connection,
                """{"protocol":"json","version":1}""" + '\u001e'));
            await host.SendAsync(Request(HttpMethod.Post, connection,
                """{"type":1,"invocationId":"1","target":"GetInitialData","arguments":[]}""" + '\u001e'));

            var names = new List<string>();
            var records = new List<string>();
            var completed = false;
            for (var poll = 0; poll < 20 && !completed; poll++)
            {
                var answer = await host.SendAsync(Request(HttpMethod.Get, connection));
                foreach (var record in (await answer.Content.ReadAsStringAsync())
                             .Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                {
                    records.Add(record);
                    using var json = JsonDocument.Parse(record);
                    var root = json.RootElement;
                    if (root.TryGetProperty("target", out var target) &&
                        target.GetString() == nameof(IWebGuiClient.OptionsChanged))
                    {
                        names.Add(root.GetProperty("arguments")[0].GetString()!);
                    }

                    completed |= root.TryGetProperty("invocationId", out var id) && id.GetString() == "1";
                }
            }

            Assert.IsTrue(completed, because);
            CollectionAssert.AreEquivalent(expected, names, because);
            if (remoteIp != null)
            {
                AssertNoSecret(records, because);
            }
        }
    }

    /// <summary>
    /// SignalR's JSON protocol over a raw WebSocket, as the SPA speaks it: every record kept,
    /// so a test can look for what should never have been sent.
    /// </summary>
    private sealed class HubClient : IAsyncDisposable
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly ClientWebSocket _socket;
        private readonly Queue<string> _pending = new();
        private int _invocations;

        private HubClient(ClientWebSocket socket)
        {
            _socket = socket;
        }

        public List<string> Records { get; } = [];

        /// <param name="remoteIp">Null for this machine.</param>
        /// <param name="device">Signs the handshake as this device.</param>
        public static async Task<HubClient> ConnectAsync(ServiceGateHost host, string? remoteIp,
            PairingCredentials? device = null)
        {
            var headers = new Dictionary<string, string>();
            if (remoteIp != null)
            {
                headers[ServiceGateHost.RemoteIpHeader] = remoteIp;
            }

            if (device != null)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var nonce = RemoteRequestSignature.NewNonce();
                var canonical = RemoteRequestSignature.BuildCanonicalString(device.DeviceId, "GET", "/hub/ui",
                    string.Empty, timestamp, nonce, string.Empty);
                headers["Authorization"] = RemoteRequestSignature.BuildHeader(device.DeviceId, timestamp, nonce,
                    RemoteRequestSignature.Sign(RemoteRequestSignature.FromBase64Url(device.Key), canonical));
            }

            var socket = await host.OpenSocketAsync("/hub/ui", null, headers: headers);
            Assert.AreEqual(WebSocketState.Open, socket.State, $"HTTP {socket.HttpStatusCode}");

            var client = new HubClient(socket);
            await client.SendAsync("""{"protocol":"json","version":1}""");
            Assert.AreEqual("{}", await client.NextAsync());
            return client;
        }

        /// <summary>Asks for the initial data, and answers the options it was sent, by name.</summary>
        public async Task<Dictionary<string, JsonElement>> GetInitialDataAsync()
        {
            var id = (++_invocations).ToString();
            await SendAsync($$"""{"type":1,"invocationId":"{{id}}","target":"GetInitialData","arguments":[]}""");

            var options = new Dictionary<string, JsonElement>();
            while (true)
            {
                using var record = JsonDocument.Parse(await NextAsync());
                var root = record.RootElement;
                var type = root.GetProperty("type").GetInt32();

                if (type == 3 && root.GetProperty("invocationId").GetString() == id)
                {
                    Assert.IsFalse(root.TryGetProperty("error", out var error), error.ToString());
                    return options;
                }

                if (TryReadOptions(root, out var name, out var value))
                {
                    options[name] = value;
                }
            }
        }

        /// <summary>Every options object pushed until, and including, one named <paramref name="name"/>.</summary>
        public async Task<List<(string Name, JsonElement Options)>> ReceiveOptionsUntilAsync(string name)
        {
            var received = new List<(string, JsonElement)>();
            while (true)
            {
                using var record = JsonDocument.Parse(await NextAsync());
                if (TryReadOptions(record.RootElement, out var pushed, out var value))
                {
                    received.Add((pushed, value));
                    if (pushed == name)
                    {
                        return received;
                    }
                }
            }
        }

        public async Task AssertClosedAsync()
        {
            try
            {
                while (true)
                {
                    await NextAsync();
                }
            }
            catch (Exception e) when (e is WebSocketException or ConnectionClosedException)
            {
            }
        }

        private static bool TryReadOptions(JsonElement record, out string name, out JsonElement options)
        {
            name = string.Empty;
            options = default;
            if (record.GetProperty("type").GetInt32() != 1 ||
                record.GetProperty("target").GetString() != nameof(IWebGuiClient.OptionsChanged))
            {
                return false;
            }

            var arguments = record.GetProperty("arguments");
            name = arguments[0].GetString()!;
            options = arguments[1].Clone();
            return true;
        }

        private async Task SendAsync(string record)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            await _socket.SendAsync(Encoding.UTF8.GetBytes(record + '\u001e'), WebSocketMessageType.Text, true,
                timeout.Token);
        }

        /// <summary>The next record other than a keep-alive ping.</summary>
        private async Task<string> NextAsync()
        {
            using var timeout = new CancellationTokenSource(Timeout);
            while (true)
            {
                while (_pending.Count == 0)
                {
                    using var message = new MemoryStream();
                    var buffer = new byte[16 * 1024];
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(buffer, timeout.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            throw new ConnectionClosedException();
                        }

                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    foreach (var part in Encoding.UTF8.GetString(message.ToArray())
                                 .Split('\u001e', StringSplitOptions.RemoveEmptyEntries))
                    {
                        Records.Add(part);
                        _pending.Enqueue(part);
                    }
                }

                var next = _pending.Dequeue();
                if (!next.StartsWith("""{"type":6""", StringComparison.Ordinal))
                {
                    return next;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            _socket.Dispose();
            return ValueTask.CompletedTask;
        }

        private sealed class ConnectionClosedException : Exception;
    }
}
