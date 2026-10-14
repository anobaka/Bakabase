using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Services;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Console;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class DevicePlaybackOverridesRouteTests
{
    private const string Route = "/player/device-playback/overrides";
    private string _root = null!;
    private string PreferencesPath => Path.Combine(_root, "device-playback.json");
    private DevicePlaybackOverridesStore Store() => new(Options.Create(new PlayerModuleOptions
    { DevicePlaybackPreferencesFile = () => PreferencesPath }));
    private static string Choices(string target) => JsonSerializer.Serialize(new
    { target, resourceId = 42, choices = new { video = new { mode = "native", playerId = "Vlc" } } });

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "bakabase-override-route-tests", Guid.NewGuid().ToString("N"));
    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class NoPlayers : IPlayerExecutableLocator
    {
        public IReadOnlyList<string> Locate(KnownPlayerDefinition definition) => [];
    }

    private Task<ServiceGateHost> Service(ServerKind kind = ServerKind.Desktop) =>
        ServiceGateHost.StartAsync([typeof(DevicePlaybackOverridesController)], services =>
        {
            services.AddSingleton(Store());
            services.AddSingleton<IPlayerExecutableLocator, NoPlayers>();
            services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => kind));
        });

    [TestMethod]
    public async Task Desktop_rules_roundtrip_without_exposing_executable_paths()
    {
        await using var host = await Service();
        using var saved = await host.SendAsync(HttpMethod.Put, Route, "same-origin", host.Origin, Choices("server:remote"));
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        using var loaded = await host.SendAsync(HttpMethod.Get, Route + "?target=server%3Aremote", "same-origin", host.Origin);
        var text = await loaded.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, loaded.StatusCode, text);
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");
        Assert.AreEqual(1, data.GetProperty("rules").GetArrayLength());
        Assert.IsFalse(text.Contains("executablePath", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(data.GetProperty("openers").EnumerateArray().All(p => !p.GetProperty("installed").GetBoolean()));
    }

    [DataTestMethod]
    [DataRow(ServerKind.Headless, false)]
    [DataRow(ServerKind.Headless, true)]
    [DataRow(ServerKind.Desktop, true)]
    public async Task Headless_and_remote_requests_cannot_read_or_change_host_opening_rules(ServerKind kind, bool remote)
    {
        await using var host = await Service(kind);
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var request = host.Request(method, Route + "?target=server%3Aremote", json: method == HttpMethod.Put ? Choices("server:remote") : null);
            if (remote) request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.1.50");
            using var response = await host.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        Assert.IsFalse(Directory.Exists(_root));
    }

    [TestMethod]
    public async Task Relays_scope_rules_to_the_managed_target_and_never_forward_them()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var first = await FakeServer.StartAsync("override-first", "First", 49210);
        await using var second = await FakeServer.StartAsync("override-second", "Second", 49220);
        await console.AddManagedAsync(first);
        await console.AddManagedAsync(second);
        var firstPort = ConsoleHarness.PortOf((await console.Manager.OpenAsync(first.ServerId, null))!.Url);
        var secondPort = ConsoleHarness.PortOf((await console.Manager.OpenAsync(second.ServerId, null))!.Url);
        foreach (var server in new[] { first, second })
            console.Manager.RelayServices(server.ServerId)!.GetRequiredService<IOptions<PlayerModuleOptions>>()
                .Value.DevicePlaybackPreferencesFile = () => PreferencesPath;

        using var saved = await Send(firstPort, HttpMethod.Put, json: Choices("server:override-first"));
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode, await saved.Content.ReadAsStringAsync());
        using var loaded = await Send(firstPort, HttpMethod.Get, "server:override-first");
        Assert.AreEqual(HttpStatusCode.OK, loaded.StatusCode, await loaded.Content.ReadAsStringAsync());
        using var wrongRead = await Send(secondPort, HttpMethod.Get, "server:override-first");
        using var wrongWrite = await Send(secondPort, HttpMethod.Put, json: Choices("server:override-first"));
        using var nodeWrite = await Send(firstPort, HttpMethod.Put, json: Choices("node:peer:epoch"));
        foreach (var refused in new[] { wrongRead, wrongWrite, nodeWrite })
            Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode, await refused.Content.ReadAsStringAsync());
        using var other = await Send(secondPort, HttpMethod.Get, "server:override-second");
        var empty = JsonDocument.Parse(await other.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(0, empty.GetProperty("data").GetProperty("rules").GetArrayLength());
        Assert.AreEqual("Vlc", (await Store().ResolveAsync("server:override-first", 42, "video"))!.PlayerId);
        Assert.IsFalse(first.Requests.Concat(second.Requests).Any(r => r.Path == Route));
    }

    [TestMethod]
    public async Task Missing_fields_executable_payloads_and_foreign_origins_are_rejected_before_writing()
    {
        await using var host = await Service();
        await using var console = await ConsoleHarness.StartAsync();
        await using var server = await FakeServer.StartAsync("override-validation", "Validation", 49230);
        await console.AddManagedAsync(server);
        var port = ConsoleHarness.PortOf((await console.Manager.OpenAsync(server.ServerId, null))!.Url);
        console.Manager.RelayServices(server.ServerId)!.GetRequiredService<IOptions<PlayerModuleOptions>>()
            .Value.DevicePlaybackPreferencesFile = () => PreferencesPath;
        string[] invalid =
        [
            "null", "{}", "{\"target\":\"server:override-validation\"}",
            "{\"target\":\"server:override-validation\",\"choices\":null}",
            "{\"target\":\"server:override-validation\",\"choices\":{\"video\":{}}}",
            "{\"target\":\"server:override-validation\",\"choices\":{\"video\":{\"mode\":\"native\",\"playerId\":\"/tmp/run\"}}}",
            "{\"target\":\"server:override-validation\",\"choices\":{},\"executablePath\":\"/tmp/run\"}",
            "{\"target\":\"server:override-validation\",\"choices\":{\"video\":{\"mode\":\"native\",\"command\":\"run\"}}}"
        ];
        foreach (var json in invalid)
        {
            using var desktop = await host.SendAsync(HttpMethod.Put, Route, "same-origin", host.Origin, json);
            using var relay = await Send(port, HttpMethod.Put, json: json);
            Assert.AreEqual(HttpStatusCode.BadRequest, desktop.StatusCode, json + ": " + await desktop.Content.ReadAsStringAsync());
            Assert.AreEqual(HttpStatusCode.BadRequest, relay.StatusCode, json + ": " + await relay.Content.ReadAsStringAsync());
        }
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var desktop = await host.SendAsync(method, Route + "?target=server%3Aoverride-validation", "cross-site", "https://foreign.example", method == HttpMethod.Put ? Choices("server:override-validation") : null);
            using var relay = await Send(port, method, "server:override-validation", method == HttpMethod.Put ? Choices("server:override-validation") : null, "cross-site", "https://foreign.example");
            Assert.AreEqual(HttpStatusCode.Forbidden, desktop.StatusCode);
            Assert.AreEqual(HttpStatusCode.BadRequest, relay.StatusCode);
        }
        Assert.IsFalse(Directory.Exists(_root));
        Assert.IsFalse(server.Requests.Any(r => r.Path == Route));
    }

    private static async Task<HttpResponseMessage> Send(int port, HttpMethod method, string? target = null,
        string? json = null, string? site = "same-origin", string? origin = null)
    {
        using var handler = new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false };
        using var client = new HttpClient(handler);
        var query = target == null ? "" : "?target=" + Uri.EscapeDataString(target);
        using var request = new HttpRequestMessage(method, $"http://127.0.0.1:{port}{Route}{query}");
        request.Headers.Host = $"127.0.0.1:{port}";
        if (site != null) request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", site);
        request.Headers.TryAddWithoutValidation("Origin", origin ?? $"http://127.0.0.1:{port}");
        if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await client.SendAsync(request);
        await response.Content.LoadIntoBufferAsync();
        return response;
    }
}
