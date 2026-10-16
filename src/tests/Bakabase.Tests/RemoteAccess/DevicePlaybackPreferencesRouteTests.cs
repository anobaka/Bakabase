using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Models.Input;
using Bakabase.Modules.Player.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Console;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Playback preferences belong to the computer running the window, including when that
/// window displays a managed server. Real listeners exercise the Service's request gates
/// and the desktop relay's production composition without touching native players or files.
/// </summary>
[TestClass]
public class DevicePlaybackPreferencesRouteTests
{
    private const string Route = "/player/device-playback/preferences";
    private const string LanAddress = "192.168.1.50";
    private const string SnapshotRoute = "/player/batch-play/resource-snapshot";
    private const string Defaults = """
        {"version":1,"image":{"mode":"automatic"},"text":{"mode":"automatic"},
        "audio":{"mode":"automatic"},"video":{"mode":"automatic"},"other":{"mode":"automatic"}}
        """;
    private const string Choices = """
        {"version":1,"image":{"mode":"browser"},"text":{"mode":"browser"},
        "audio":{"mode":"native","nativeSchemeId":"device-vlc"},
        "video":{"mode":"ask"},"other":{"mode":"automatic"}}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() {PropertyNameCaseInsensitive = true};

    private sealed class RecordingStore : IDevicePlaybackPreferencesStore
    {
        private string _saved = Defaults;
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public string Saved => _saved;

        public Task<DevicePlaybackPreferences> ReadAsync(CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(JsonSerializer.Deserialize<DevicePlaybackPreferences>(_saved, JsonOptions)!);
        }

        public Task WriteAsync(DevicePlaybackPreferences preferences, CancellationToken ct = default)
        {
            Writes++;
            _saved = JsonSerializer.Serialize(preferences, JsonOptions);
            return Task.CompletedTask;
        }
    }

    /// <summary>The server's existing resource/player configuration, independent of the device preference store.</summary>
    private sealed class PlaybackSources : IBatchPlayService, IBatchPlayResourceSource, IBatchPlayPlaylistSource
    {
        public int SnapshotReads { get; private set; }
        public int OtherCalls { get; private set; }

        public Task<BatchPlayResourceSnapshot> GetSnapshotAsync(int[] resourceIds, CancellationToken ct)
        {
            SnapshotReads++;
            return Task.FromResult(new BatchPlayResourceSnapshot(
                [new BatchPlayResourceFiles(42, ["/server/library/movie.mkv"])],
                [new MediaLibraryPlayer {ExecutablePath = "/server/players/profile-player", Command = "{0}"}]));
        }

        private Task<T> Unexpected<T>()
        {
            OtherCalls++;
            throw new InvalidOperationException("Preference routes must not launch a player or change server resources.");
        }

        public Task<List<BatchPlayCandidate>> GetCandidatesAsync(int[] resourceIds, CancellationToken ct) =>
            Unexpected<List<BatchPlayCandidate>>();
        public Task<BatchPlayResult> PlayAsync(BatchPlayInputModel input, CancellationToken ct) => Unexpected<BatchPlayResult>();
        public Task<List<BatchPlayCandidate>> GetPlaylistCandidatesAsync(int playlistId, CancellationToken ct) =>
            Unexpected<List<BatchPlayCandidate>>();
        public Task<BatchPlayResult> PlayPlaylistAsync(int playlistId, string playerKey, CancellationToken ct) =>
            Unexpected<BatchPlayResult>();
        public Task MarkPlayedAsync(IReadOnlyDictionary<int, string> playedByResourceId, CancellationToken ct) =>
            Unexpected<object>();
        public Task<BatchPlayPlaylistSnapshot?> GetSnapshotAsync(int playlistId, CancellationToken ct) =>
            Unexpected<BatchPlayPlaylistSnapshot?>();
    }

    private static Task<ServiceGateHost> StartService(RecordingStore store, PlaybackSources sources,
        ServerKind kind = ServerKind.Desktop) =>
        ServiceGateHost.StartAsync([typeof(PlayerController)], services =>
        {
            services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => kind));
            services.AddSingleton<IDevicePlaybackPreferencesStore>(store);
            services.AddSingleton<IBatchPlayService>(sources);
            services.AddSingleton<IBatchPlayResourceSource>(sources);
            services.AddSingleton<IBatchPlayPlaylistSource>(sources);
        });

    private static async Task<JsonElement> SuccessData(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, json);
        var body = JsonDocument.Parse(json).RootElement;
        Assert.AreEqual(0, body.GetProperty("code").GetInt32(), json);
        return body.GetProperty("data").Clone();
    }

    private static void AssertChoices(JsonElement data)
    {
        Assert.AreEqual(1, data.GetProperty("version").GetInt32());
        Assert.AreEqual("browser", data.GetProperty("image").GetProperty("mode").GetString());
        Assert.AreEqual("browser", data.GetProperty("text").GetProperty("mode").GetString());
        Assert.AreEqual("native", data.GetProperty("audio").GetProperty("mode").GetString());
        Assert.AreEqual("device-vlc", data.GetProperty("audio").GetProperty("nativeSchemeId").GetString());
        Assert.AreEqual("ask", data.GetProperty("video").GetProperty("mode").GetString());
        Assert.AreEqual("automatic", data.GetProperty("other").GetProperty("mode").GetString());
    }

    [TestMethod]
    public async Task Own_desktop_window_reads_and_writes_preferences_without_changing_server_profiles()
    {
        var store = new RecordingStore();
        var sources = new PlaybackSources();
        await using var host = await StartService(store, sources);

        using var before = await host.SendAsync(HttpMethod.Post, SnapshotRoute, "same-origin", host.Origin,
            """{"resourceIds":[42]}""");
        var profileBefore = await SuccessData(before);
        using var saved = await host.SendAsync(HttpMethod.Put, Route, "same-origin", host.Origin, Choices);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        using var loaded = await host.SendAsync(HttpMethod.Get, Route, "same-origin", host.Origin);
        AssertChoices(await SuccessData(loaded));
        using var after = await host.SendAsync(HttpMethod.Post, SnapshotRoute, "same-origin", host.Origin,
            """{"resourceIds":[42]}""");
        var profileAfter = await SuccessData(after);

        Assert.AreEqual(profileBefore.GetRawText(), profileAfter.GetRawText());
        Assert.AreEqual("/server/players/profile-player",
            profileAfter.GetProperty("configuredPlayers")[0].GetProperty("executablePath").GetString());
        Assert.AreEqual(1, store.Writes);
        Assert.AreEqual(1, store.Reads);
        Assert.AreEqual(2, sources.SnapshotReads);
        Assert.AreEqual(0, sources.OtherCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Headless_server_never_exposes_device_preferences_even_when_requests_are_trusted(bool remote)
    {
        var store = new RecordingStore();
        var sources = new PlaybackSources();
        await using var host = await StartService(store, sources, ServerKind.Headless);
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        foreach (var method in new[] {HttpMethod.Get, HttpMethod.Put})
        {
            using var request = host.Request(method, Route, json: method == HttpMethod.Put ? Choices : null);
            if (remote) request.Headers.Add(ServiceGateHost.RemoteIpHeader, LanAddress);
            using var response = await host.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine),
                response.Headers.GetValues("X-Bakabase-Remote-Access").Single());
        }

        // Ordinary data work remains reachable on the same headless host.
        using var snapshotRequest = host.Request(HttpMethod.Post, SnapshotRoute, json: """{"resourceIds":[42]}""");
        if (remote) snapshotRequest.Headers.Add(ServiceGateHost.RemoteIpHeader, LanAddress);
        using var snapshotResponse = await host.SendAsync(snapshotRequest);
        await SuccessData(snapshotResponse);
        Assert.AreEqual(1, sources.SnapshotReads);
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Writes);
    }

    [TestMethod]
    [DataRow(RemoteAccessMode.Enabled, false)]
    [DataRow(RemoteAccessMode.Unrestricted, false)]
    [DataRow(RemoteAccessMode.Enabled, true)]
    public async Task A_remote_caller_cannot_read_or_replace_the_hosts_device_preferences(RemoteAccessMode mode,
        bool paired)
    {
        var store = new RecordingStore();
        await using var host = await StartService(store, new PlaybackSources());
        host.Remote.Mode = mode;
        PairingCredentials? credentials = null;
        if (paired)
        {
            var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
            var code = await devices.IssuePairingCodeAsync();
            credentials = (await devices.PairWithCodeAsync(code.Code, "Laptop", RemoteDevicePlatform.Windows)).Credentials!;
        }

        foreach (var method in new[] {HttpMethod.Get, HttpMethod.Put})
        {
            var payload = method == HttpMethod.Put ? Choices : null;
            using var request = host.Request(method, Route, json: payload);
            request.Headers.Add(ServiceGateHost.RemoteIpHeader, LanAddress);
            if (credentials != null)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var nonce = RemoteRequestSignature.NewNonce();
                var digest = payload == null ? string.Empty : RemoteRequestSignature.HashBody(Encoding.UTF8.GetBytes(payload));
                var canonical = RemoteRequestSignature.BuildCanonicalString(credentials.DeviceId, method.Method,
                    Route, string.Empty, timestamp, nonce, digest);
                request.Headers.TryAddWithoutValidation("Authorization",
                    RemoteRequestSignature.BuildHeader(credentials.DeviceId, timestamp, nonce,
                        RemoteRequestSignature.Sign(RemoteRequestSignature.FromBase64Url(credentials.Key), canonical)));
            }

            using var response = await host.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(nameof(RemoteAccessDenialReason.RunsOnUserMachine),
                response.Headers.GetValues("X-Bakabase-Remote-Access").Single());
        }

        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Writes);
    }

    [TestMethod]
    [DataRow("same-site", ServiceGateHost.RelayOrigin)]
    [DataRow("cross-site", "https://foreign.example")]
    [DataRow(null, "https://foreign.example")]
    public async Task A_foreign_loopback_page_cannot_read_or_change_desktop_preferences(string? site, string origin)
    {
        var store = new RecordingStore();
        await using var host = await StartService(store, new PlaybackSources());
        foreach (var method in new[] {HttpMethod.Get, HttpMethod.Put})
        {
            using var response = await host.SendAsync(method, Route, site, origin,
                method == HttpMethod.Put ? Choices : null);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
            Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly),
                response.Headers.GetValues("X-Bakabase-Remote-Access").Single());
        }

        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(0, store.Writes);
    }

    [TestMethod]
    public async Task Managed_server_relays_use_this_devices_one_store_and_never_forward_preferences()
    {
        var store = new RecordingStore();
        await using var console = await ConsoleHarness.StartAsync(services: services =>
            services.AddSingleton<IDevicePlaybackPreferencesStore>(store));
        await using var first = await FakeServer.StartAsync("playback-first", "First", 49150);
        await using var second = await FakeServer.StartAsync("playback-second", "Second", 49160);
        await console.AddManagedAsync(first);
        await console.AddManagedAsync(second);
        var firstPort = ConsoleHarness.PortOf((await console.Manager.OpenAsync(first.ServerId, null))!.Url);
        var secondPort = ConsoleHarness.PortOf((await console.Manager.OpenAsync(second.ServerId, null))!.Url);

        Assert.AreSame(store, console.Manager.RelayServices(first.ServerId)!
            .GetRequiredService<IDevicePlaybackPreferencesStore>());
        Assert.AreSame(store, console.Manager.RelayServices(second.ServerId)!
            .GetRequiredService<IDevicePlaybackPreferencesStore>());

        using var saved = await SendRelay(firstPort, HttpMethod.Put, Choices);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        using var loaded = await SendRelay(secondPort, HttpMethod.Get);
        AssertChoices(await SuccessData(loaded));

        // The app's own Service reads the same settings after switching back to this device.
        await using var host = await StartService(store, new PlaybackSources());
        using var ownLoaded = await host.SendAsync(HttpMethod.Get, Route, "same-origin", host.Origin);
        AssertChoices(await SuccessData(ownLoaded));
        Assert.AreEqual(1, store.Writes);
        Assert.IsFalse(first.Requests.Any(r => r.Path == Route), "first server received this device's settings");
        Assert.IsFalse(second.Requests.Any(r => r.Path == Route), "second server received this device's settings");
    }

    [TestMethod]
    [DataRow("same-site", "http://127.0.0.1:34567")]
    [DataRow("cross-site", "https://foreign.example")]
    [DataRow(null, "https://foreign.example")]
    public async Task Foreign_origin_mutation_is_refused_before_the_relay_touches_preferences(string? site,
        string origin)
    {
        var store = new RecordingStore();
        await using var console = await ConsoleHarness.StartAsync(services: services =>
            services.AddSingleton<IDevicePlaybackPreferencesStore>(store));
        await using var server = await FakeServer.StartAsync("playback-protected", "Protected", 49170);
        await console.AddManagedAsync(server);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(server.ServerId, null))!.Url);

        using var response = await SendRelay(port, HttpMethod.Put, Choices, site, origin);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, await response.Content.ReadAsStringAsync());
        Assert.AreEqual(nameof(ClientForwardingFailure.ForeignCaller),
            response.Headers.GetValues("X-Bakabase-Client").Single());
        Assert.AreEqual(0, store.Writes);
        Assert.AreEqual(0, store.Reads);
        Assert.AreEqual(Defaults, store.Saved);
        Assert.IsFalse(server.Requests.Any(r => r.Path == Route));
    }

    [TestMethod]
    public async Task Preferences_remain_device_local_when_the_managed_server_is_detached()
    {
        var store = new RecordingStore();
        await using var console = await ConsoleHarness.StartAsync(services: services =>
            services.AddSingleton<IDevicePlaybackPreferencesStore>(store));
        await using var server = await FakeServer.StartAsync("playback-detached", "Detached", 49180);
        await console.AddManagedAsync(server);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(server.ServerId, null))!.Url);
        await console.Store.MutateAsync(data => data.Servers.RemoveAll(s => s.ServerId == server.ServerId));

        using var saved = await SendRelay(port, HttpMethod.Put, Choices);
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        using var loaded = await SendRelay(port, HttpMethod.Get);
        AssertChoices(await SuccessData(loaded));
        Assert.IsFalse(server.Requests.Any(r => r.Path == Route));
    }

    [TestMethod]
    public async Task Desktop_and_relay_reject_incomplete_or_executable_bearing_preferences_before_writing()
    {
        var store = new RecordingStore();
        await using var host = await StartService(store, new PlaybackSources());
        await using var console = await ConsoleHarness.StartAsync(services: services =>
            services.AddSingleton<IDevicePlaybackPreferencesStore>(store));
        await using var server = await FakeServer.StartAsync("playback-validation", "Validation", 49190);
        await console.AddManagedAsync(server);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(server.ServerId, null))!.Url);
        var finalBrace = Defaults.LastIndexOf('}');
        string[] invalid =
        [
            "null", "{}",
            Defaults.Replace("\"image\":{\"mode\":\"automatic\"},", ""),
            Defaults.Replace("\"image\":{\"mode\":\"automatic\"}", "\"image\":{}"),
            Defaults.Replace("\"version\":1", "\"version\":2"),
            Defaults.Replace("\"mode\":\"automatic\"", "\"mode\":\"remote\""),
            Defaults[..finalBrace] + ",\"executable\":\"/bin/unsafe-player\"}",
            Defaults.Replace("\"image\":{\"mode\":\"automatic\"}",
                "\"image\":{\"mode\":\"native\",\"nativeSchemeId\":\"file:///bin/unsafe-player\"}"),
            Defaults.Replace("\"image\":{\"mode\":\"automatic\"}",
                "\"image\":{\"mode\":\"native\",\"command\":\"arbitrary-command\"}")
        ];

        foreach (var json in invalid)
        {
            using var desktop = await host.SendAsync(HttpMethod.Put, Route, "same-origin", host.Origin, json);
            using var relay = await SendRelay(port, HttpMethod.Put, json);
            foreach (var response in new[] {desktop, relay})
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode, $"{json}: {body}");
                Assert.AreEqual(400, JsonDocument.Parse(body).RootElement.GetProperty("code").GetInt32());
            }
        }

        Assert.AreEqual(0, store.Writes);
        Assert.AreEqual(Defaults, store.Saved);
        Assert.IsFalse(server.Requests.Any(r => r.Path == Route));
    }

    [TestMethod]
    public async Task Oversized_preferences_are_refused_without_writing_or_forwarding()
    {
        var store = new RecordingStore();
        await using var host = await StartService(store, new PlaybackSources());
        await using var console = await ConsoleHarness.StartAsync(services: services =>
            services.AddSingleton<IDevicePlaybackPreferencesStore>(store));
        await using var server = await FakeServer.StartAsync("playback-large", "Large", 49200);
        await console.AddManagedAsync(server);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(server.ServerId, null))!.Url);
        var json = "{\"padding\":\"" + new string('x', 17 * 1024) + "\"}";

        using var desktop = await host.SendAsync(HttpMethod.Put, Route, "same-origin", host.Origin, json);
        using var relay = await SendRelay(port, HttpMethod.Put, json);
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, desktop.StatusCode,
            await desktop.Content.ReadAsStringAsync());
        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, relay.StatusCode,
            await relay.Content.ReadAsStringAsync());
        Assert.AreEqual(0, store.Writes);
        Assert.AreEqual(Defaults, store.Saved);
        Assert.IsFalse(server.Requests.Any(r => r.Path == Route));
    }

    private static async Task<HttpResponseMessage> SendRelay(int port, HttpMethod method, string? json = null,
        string? site = "same-origin", string? origin = null)
    {
        using var handler = new SocketsHttpHandler {UseProxy = false, UseCookies = false, AllowAutoRedirect = false};
        using var client = new HttpClient(handler);
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{port}{Route}");
        request.Headers.Host = $"127.0.0.1:{port}";
        if (site != null) request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        request.Headers.TryAddWithoutValidation("Origin", origin ?? $"http://127.0.0.1:{port}");
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
