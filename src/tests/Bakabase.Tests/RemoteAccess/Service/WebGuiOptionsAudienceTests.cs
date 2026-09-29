using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Modules.RemoteAccess.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
/// The options are held by real options managers, and remote access is read from its own
/// options by the real service, so every writer of them is seen by the gate, the hub filter
/// and <see cref="RemoteAccessConnectionMonitor"/> alike. A change is sent by the real
/// <see cref="WebGuiHubConfigurationAdapter"/>. The hub, <see cref="OptionsHub"/>, stands in
/// for <c>WebGuiHub</c>, whose other initial data needs the database: it sends the options
/// exactly as <c>WebGuiHub</c> does — <see cref="WebGuiOptionsAudience.SendAllAsync"/> — and
/// <see cref="Nothing_else_sends_options_to_the_UI_hub"/> holds <c>WebGuiHub</c> to that.
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
        [..BrowsingOptions, "tmdbOptions", "bilibiliOptions", "networkOptions", "remoteAccessOptions"];

    private readonly string _settingsDirectory =
        Path.Combine(Path.GetTempPath(), "bakabase-options-audience", Guid.NewGuid().ToString("N"));

    public sealed class OptionsHub(BakabaseOptionsManagerPool optionsManagerPool) : Hub<IWebGuiClient>
    {
        public Task GetInitialData() => WebGuiOptionsAudience.SendAllAsync(optionsManagerPool, Context, Clients.Caller);
    }

    /// <summary>Stands in for the progressor hub, which sends no options.</summary>
    public sealed class ProgressorHub : Hub
    {
        public string Ping() => "pong";
    }

    /// <summary>
    /// <c>WebGuiHub</c>'s context, reaching <see cref="OptionsHub"/>'s connections, so the
    /// adapter sends to them as it sends to <c>WebGuiHub</c>'s.
    /// </summary>
    private sealed class UiHubContext(IHubContext<OptionsHub, IWebGuiClient> hub)
        : IHubContext<WebGuiHub, IWebGuiClient>
    {
        public IHubClients<IWebGuiClient> Clients => hub.Clients;
        public IGroupManager Groups => hub.Groups;
    }

    /// <summary>
    /// The configuration an options file is bound from; <see cref="Reload"/> is the file
    /// changing on disk.
    /// </summary>
    private sealed class SettingsFile<T>(T value) : IOptionsMonitor<T>
    {
        private Action<T, string?>? _listeners;

        public T CurrentValue { get; private set; } = value;
        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener)
        {
            _listeners += listener;
            return null;
        }

        public void Reload(T value)
        {
            CurrentValue = value;
            _listeners?.Invoke(value, null);
        }
    }

    private sealed class NoListeningAddresses : IListeningAddressProvider
    {
        public IReadOnlyList<string> GetListeningAddresses() => [];
    }

    private sealed class NoPeerDiscovery : INodePeerDiscovery
    {
        public Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeDiscoveryCandidate>>([]);
    }

    [TestCleanup]
    public void DeleteSettings()
    {
        try
        {
            Directory.Delete(_settingsDirectory, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private async Task<ServiceGateHost> StartAsync(RemoteAccessMode mode, bool withProgressor = false,
        bool requirePairing = false)
    {
        var host = await ServiceGateHost.StartAsync([typeof(RemoteAccessController), typeof(FederationPeerController)],
            services =>
            {
                // As BakabaseStartup registers them.
                services.AddSingleton<RemoteAccessHubFilter>();
                services.Configure<HubOptions>(o => o.AddFilter<RemoteAccessHubFilter>());
                services.AddHostedService<RemoteAccessConnectionMonitor>();
                services.AddSingleton<WebGuiHubConfigurationAdapter>();
                services.AddSingleton<IHubContext<WebGuiHub, IWebGuiClient>>(sp =>
                    new UiHubContext(sp.GetRequiredService<IHubContext<OptionsHub, IWebGuiClient>>()));

                // Remote access as the Service reads it: from its options.
                AddSettings(services,
                    new RemoteAccessOptions {Mode = mode, RequirePairing = requirePairing, ServerId = "this-server"});
                services.AddSingleton(new RemoteAccessDefaults(RemoteAccessMode.Disabled));
                services.AddSingleton<IRemoteAccessService>(sp => new RemoteAccessService(
                    sp.GetRequiredService<IBOptionsManager<RemoteAccessOptions>>(),
                    sp.GetRequiredService<RemoteAccessDefaults>(), new RemoteAccessHostInfo("0.0.0"),
                    new NoListeningAddresses(), NullLogger<RemoteAccessService>.Instance));

                // What the devices page's sharing switch needs besides the host's federation.
                services.AddSingleton<INodePeerDiscovery, NoPeerDiscovery>();
                services.AddSingleton<FederationPairingFlow>();

                AddSettings(services, new AppOptions {Language = "en-US", UiTheme = UiTheme.Dark, WwwRootPath = HostPath});
                AddSettings(services, new UIOptions {IsMenuCollapsed = true});
                AddSettings(services, new UIStyleOptions());
                AddSettings(services, new ResourceOptions {HideChildren = true});
                AddSettings(services, new TmdbOptions {ApiKey = ApiKey, Cookie = Cookie});
                AddSettings(services, new BilibiliOptions {Cookie = Cookie});
                AddSettings(services, new NetworkOptions
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
            }, mapHubs: endpoints =>
            {
                endpoints.MapHub<OptionsHub>("/hub/ui");
                if (withProgressor)
                {
                    endpoints.MapHub<ProgressorHub>("/hub/progressor");
                }
            });

        // As BakabaseStartup.Configure does.
        host.Services.GetRequiredService<WebGuiHubConfigurationAdapter>().Initialize();
        return host;
    }

    /// <summary>An options object as the Service holds it: a manager over its own file.</summary>
    private void AddSettings<T>(IServiceCollection services, T value) where T : class, new()
    {
        var file = new SettingsFile<T>(value);
        var path = Path.Combine(_settingsDirectory, $"{typeof(T).Name}.json");
        services.AddSingleton(file);
        services.AddSingleton(_ => new AspNetCoreOptionsManager<T>(path, typeof(T).Name, file,
            NullLogger<AspNetCoreOptionsManager<T>>.Instance));
        services.AddSingleton<IBOptionsManager<T>>(sp => sp.GetRequiredService<AspNetCoreOptionsManager<T>>());
        services.AddSingleton<IBOptionsManagerInternal>(sp => sp.GetRequiredService<AspNetCoreOptionsManager<T>>());
    }

    /// <summary>A change saved through the options manager, which the adapter sends on.</summary>
    private static Task SaveAsync<T>(ServiceGateHost host, T options) where T : class, new() =>
        host.Services.GetRequiredService<AspNetCoreOptionsManager<T>>().SaveAsync(options);

    private static async Task<PairingCredentials> PairAsync(ServiceGateHost host)
    {
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var code = await devices.IssuePairingCodeAsync();
        return (await devices.PairWithCodeAsync(code.Code, "Laptop", RemoteDevicePlatform.Windows)).Credentials!;
    }

    private static void AssertNotSent(IEnumerable<string> records, string because, params string[] secrets)
    {
        foreach (var record in records)
        foreach (var secret in secrets)
        {
            Assert.IsFalse(record.Contains(secret, StringComparison.Ordinal), $"{because}: sent {secret}");
        }
    }

    private static void AssertNoSecret(IEnumerable<string> records, string because) =>
        AssertNotSent(records, because, Secrets);

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
        await SaveAsync(host, new TmdbOptions {ApiKey = ApiKey + "-changed"});
        await SaveAsync(host, new UIOptions {IsMenuCollapsed = false});

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

        await SaveAsync(host, new TmdbOptions {ApiKey = ApiKey + "-changed"});

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
        Assert.AreEqual(HttpStatusCode.OK, switched.StatusCode);
        Assert.AreEqual(RemoteAccessMode.Enabled,
            host.Services.GetRequiredService<IRemoteAccessService>().GetEffectiveMode());

        await browser.AssertClosedAsync();

        // The paired device keeps its connection and what it is sent.
        await SaveAsync(host, new TmdbOptions {ApiKey = ApiKey + "-changed"});
        Assert.AreEqual(ApiKey + "-changed",
            (await paired.ReceiveOptionsUntilAsync("tmdbOptions")).Last().Options.GetProperty("apiKey").GetString());

        // Back again, the browser is judged by the mode as it is now.
        await using var again = await HubClient.ConnectAsync(host, LanAddress);
        CollectionAssert.AreEquivalent(BrowsingOptions, (await again.GetInitialDataAsync()).Keys.ToArray());
        AssertNoSecret(again.Records, "reconnected on Enabled");
    }

    /// <summary>
    /// The mode is not only changed on the remote-access settings page: turning library sharing
    /// on from the devices page, with its "also set remote access to Enabled and require
    /// pairing" option, makes an Unrestricted server Enabled for paired devices only; and the
    /// settings file can change on disk.
    /// </summary>
    [TestMethod]
    [DataRow("sharing switch")]
    [DataRow("settings file")]
    public async Task Every_writer_that_narrows_the_mode_hangs_up_on_the_browsers_Unrestricted_trusted(string writer)
    {
        const string changedApiKey = "tmdb-api-key-canary-after-switch";

        await using var host = await StartAsync(RemoteAccessMode.Unrestricted);
        var laptop = await PairAsync(host);
        await using var browser = await HubClient.ConnectAsync(host, LanAddress);
        await using var paired = await HubClient.ConnectAsync(host, LanAddress, laptop);

        CollectionAssert.AreEquivalent(AllOptions, (await browser.GetInitialDataAsync()).Keys.ToArray());
        CollectionAssert.AreEquivalent(AllOptions, (await paired.GetInitialDataAsync()).Keys.ToArray());

        if (writer == "sharing switch")
        {
            var shared = await host.SendAsync(HttpMethod.Put, "/federation/local/peers/sharing",
                json: """{"enabled":true,"enablePairedRemoteAccess":true}""");
            Assert.AreEqual(HttpStatusCode.OK, shared.StatusCode, await shared.Content.ReadAsStringAsync());
        }
        else
        {
            host.Services.GetRequiredService<SettingsFile<RemoteAccessOptions>>().Reload(new RemoteAccessOptions
                {Mode = RemoteAccessMode.Enabled, RequirePairing = true, ServerId = "this-server"});
        }

        var remote = host.Services.GetRequiredService<IRemoteAccessService>();
        Assert.AreEqual(RemoteAccessMode.Enabled, remote.GetEffectiveMode());
        Assert.IsTrue(remote.GetRequirePairing());

        // A new request from the browser is refused...
        var request = host.Request(HttpMethod.Get, "/remote-access/context");
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, LanAddress);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await host.SendAsync(request)).StatusCode);

        // ...and so is the connection it opened before: closed, and sent none of what changed.
        await SaveAsync(host, new TmdbOptions {ApiKey = changedApiKey});
        await browser.AssertClosedAsync();
        AssertNotSent(browser.Records, "after the switch", changedApiKey);

        // The paired device keeps its connection and what it is sent.
        Assert.AreEqual(changedApiKey,
            (await paired.ReceiveOptionsUntilAsync("tmdbOptions")).Last().Options.GetProperty("apiKey").GetString());
    }

    /// <summary>
    /// A connection's handshake is let in by the gate, but the hub opens it only once the
    /// client has sent SignalR's own handshake. A change made in between hangs up on the
    /// connections already open, which this one is not yet.
    /// </summary>
    [TestMethod]
    [DataRow(RemoteAccessMode.Disabled, false)]
    [DataRow(RemoteAccessMode.Enabled, true)]
    public async Task A_connection_the_hub_opens_after_a_change_is_judged_by_it(RemoteAccessMode mode,
        bool requirePairing)
    {
        await using var host = await StartAsync(RemoteAccessMode.Enabled);
        var laptop = await PairAsync(host);

        Task Change() => SaveAsync(host,
            new RemoteAccessOptions {Mode = mode, RequirePairing = requirePairing, ServerId = "this-server"});

        await using var browser = await HubClient.ConnectAsync(host, LanAddress, beforeHandshake: Change);
        await browser.AssertClosedAsync();

        if (mode != RemoteAccessMode.Disabled)
        {
            // A paired device in the same position is still let in.
            await SaveAsync(host, new RemoteAccessOptions {Mode = RemoteAccessMode.Enabled, ServerId = "this-server"});
            await using var paired = await HubClient.ConnectAsync(host, LanAddress, laptop, beforeHandshake: Change);
            CollectionAssert.AreEquivalent(AllOptions, (await paired.GetInitialDataAsync()).Keys.ToArray());
        }
    }

    /// <summary>
    /// A device revoked in the same gap: the revocation hangs up on the device's connections
    /// already open, which this one is not yet. Whoever holds a stolen key could otherwise keep a
    /// socket waiting there and, once cut off, still hold a connection that is sent every options
    /// object and every change.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_connection_the_hub_opens_after_its_device_is_revoked_is_refused(bool requirePairing)
    {
        await using var host = await StartAsync(RemoteAccessMode.Enabled, requirePairing: requirePairing);
        var laptop = await PairAsync(host);

        async Task Revoke()
        {
            var revoked = await host.SendAsync(HttpMethod.Delete, $"/remote-access/devices/{laptop.DeviceId}");
            Assert.AreEqual(HttpStatusCode.OK, revoked.StatusCode, await revoked.Content.ReadAsStringAsync());
        }

        await using var paired = await HubClient.ConnectAsync(host, LanAddress, laptop, beforeHandshake: Revoke);
        await paired.AssertClosedAsync();

        AssertNoSecret(paired.Records, "after the revocation");
        Assert.AreEqual(0, host.Services.GetRequiredService<RemoteConnectionRegistry>().Count);
    }

    [TestMethod]
    public async Task The_progressor_hub_joins_no_options_group()
    {
        // This machine's window would join the group sent every options object; on a hub that
        // sends none, it joins nothing.
        await using var host = await StartAsync(RemoteAccessMode.Enabled, withProgressor: true);
        await using var progressor = await HubClient.ConnectAsync(host, null, path: "/hub/progressor");
        // Answered only once the hub has opened the connection, filters and all.
        await progressor.InvokeAsync(nameof(ProgressorHub.Ping));

        var clients = host.Services.GetRequiredService<IHubContext<ProgressorHub>>().Clients;
        await clients.Group(WebGuiOptionsAudience.AllOptionsGroup).SendAsync("Probe", "options group");
        await clients.Group(WebGuiOptionsAudience.BrowsingOptionsGroup).SendAsync("Probe", "options group");
        await clients.All.SendAsync("Probe", "everyone");

        Assert.AreEqual("everyone", await progressor.ReceiveAsync("Probe"));
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
    /// Options reach the UI hub only through <see cref="WebGuiOptionsAudience"/>. A send anywhere
    /// else — every options object to the caller at connect, a change to every connection —
    /// would hand an unpaired browser every secret again (#1455), and the tests above would only
    /// see it if it were in the code they drive: <c>WebGuiHub</c> itself is not.
    /// </summary>
    [TestMethod]
    public void Nothing_else_sends_options_to_the_UI_hub()
    {
        var src = SourceRoot();
        Assert.IsTrue(File.Exists(Path.Combine(src, "legacy", "Bakabase.InsideWorld.Business", "Components", "Gui",
            "WebGuiHub.cs")), $"Not the source tree: {src}");

        var mention = new Regex(@"\b" + nameof(IWebGuiClient.OptionsChanged) + @"\b");
        var offenders = new List<string>();
        foreach (var folder in new[] {"abstractions", "apps", "legacy", "libs", "modules"})
        foreach (var file in Directory.EnumerateFiles(Path.Combine(src, folder), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file);
            if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj") ||
                Path.GetFileName(file) == $"{nameof(WebGuiOptionsAudience)}.cs")
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                // The one mention allowed elsewhere: the client's declaration of the method.
                var isDeclaration = Path.GetFileName(file) == "WebGuiHub.cs" &&
                                    lines[i].Trim() == "Task OptionsChanged(string optionsName, object options);";
                if (mention.IsMatch(lines[i]) && !isDeclaration)
                {
                    offenders.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.AreEqual(0, offenders.Count,
            $"Send options through {nameof(WebGuiOptionsAudience)}, which decides who may read them:\n" +
            string.Join("\n", offenders));
    }

    private static string SourceRoot([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", ".."));

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
        /// <param name="beforeHandshake">
        /// Runs once the gate has let the WebSocket in and before SignalR's handshake, which is
        /// when the hub opens the connection.
        /// </param>
        public static async Task<HubClient> ConnectAsync(ServiceGateHost host, string? remoteIp,
            PairingCredentials? device = null, Func<Task>? beforeHandshake = null, string path = "/hub/ui")
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
                var canonical = RemoteRequestSignature.BuildCanonicalString(device.DeviceId, "GET", path,
                    string.Empty, timestamp, nonce, string.Empty);
                headers["Authorization"] = RemoteRequestSignature.BuildHeader(device.DeviceId, timestamp, nonce,
                    RemoteRequestSignature.Sign(RemoteRequestSignature.FromBase64Url(device.Key), canonical));
            }

            var socket = await host.OpenSocketAsync(path, null, headers: headers);
            Assert.AreEqual(WebSocketState.Open, socket.State, $"HTTP {socket.HttpStatusCode}");

            if (beforeHandshake != null)
            {
                await beforeHandshake();
            }

            var client = new HubClient(socket);
            await client.SendAsync("""{"protocol":"json","version":1}""");
            Assert.AreEqual("{}", await client.NextAsync());
            return client;
        }

        /// <summary>Asks for the initial data, and answers the options it was sent, by name.</summary>
        public async Task<Dictionary<string, JsonElement>> GetInitialDataAsync()
        {
            var options = new Dictionary<string, JsonElement>();
            await InvokeAsync("GetInitialData", root =>
            {
                if (TryReadOptions(root, out var name, out var value))
                {
                    options[name] = value;
                }
            });
            return options;
        }

        /// <summary>Calls <paramref name="target"/> and waits for it to complete.</summary>
        /// <param name="pushed">Sees every other record received meanwhile.</param>
        public async Task InvokeAsync(string target, Action<JsonElement>? pushed = null)
        {
            var id = (++_invocations).ToString();
            await SendAsync($$"""{"type":1,"invocationId":"{{id}}","target":"{{target}}","arguments":[]}""");

            while (true)
            {
                using var record = JsonDocument.Parse(await NextAsync());
                var root = record.RootElement;

                if (root.GetProperty("type").GetInt32() == 3 && root.GetProperty("invocationId").GetString() == id)
                {
                    Assert.IsFalse(root.TryGetProperty("error", out var error), error.ToString());
                    return;
                }

                pushed?.Invoke(root);
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

        /// <summary>The first argument of the next call of <paramref name="target"/> pushed.</summary>
        public async Task<string?> ReceiveAsync(string target)
        {
            while (true)
            {
                using var record = JsonDocument.Parse(await NextAsync());
                var root = record.RootElement;
                if (root.GetProperty("type").GetInt32() == 1 && root.GetProperty("target").GetString() == target)
                {
                    return root.GetProperty("arguments")[0].GetString();
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
            catch (OperationCanceledException)
            {
                Assert.Fail($"Still open after {Timeout.TotalSeconds} s.");
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
