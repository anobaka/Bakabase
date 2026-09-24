using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Remoting.Components.Console;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// What a managed server's own UI is told about the window it is shown in, through that
/// server's relay's <c>/client</c> API.
/// </summary>
/// <remarks>
/// That UI is the other machine's code running in this device's window. What it may do
/// here is exactly what the thin client let it do about its own server — read the status,
/// edit its path mappings, report the tray — plus switching the window; never pairing,
/// forgetting, or reaching another server's settings.
/// </remarks>
[TestClass]
public class ConsoleEndpointsTests
{
    private ConsoleHarness _console = null!;
    private FakeServer _desk = null!;
    private FakeServer _nas = null!;
    private string _deskKey = null!;
    private string _nasKey = null!;
    private int _port;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        _nas = await FakeServer.StartAsync("server-nas", "NAS", 46900);

        (_, _deskKey) = await _console.AddManagedAsync(_desk);
        (_, _nasKey) = await _console.AddManagedAsync(_nas);

        _port = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(_desk.ServerId, null))!.Url);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
        await _nas.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> Call(string path,
        HttpMethod? method = null, string? body = null)
    {
        var response = await ConsoleHarness.SendToRelayAsync(_port, path, method, body);
        var raw = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    [TestMethod]
    public async Task Status_describes_the_console_and_only_this_relays_server()
    {
        var (status, body, raw) = await Call("/client/status");

        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.AreEqual(0, body.GetProperty("code").GetInt32());

        var data = body.GetProperty("data");
        Assert.AreEqual(ConsoleEndpoints.HostName, data.GetProperty("host").GetString());
        Assert.AreEqual(Environment.MachineName, data.GetProperty("localName").GetString());
        Assert.AreEqual(_desk.ServerId, data.GetProperty("activeServerId").GetString());
        Assert.IsTrue(data.GetProperty("serverReachable").GetBoolean());
        Assert.IsTrue(data.GetProperty("implementedUserMachineRoutes").GetArrayLength() > 0);

        var servers = data.GetProperty("servers").EnumerateArray().ToList();
        Assert.AreEqual(1, servers.Count);
        Assert.AreEqual(_desk.ServerId, servers[0].GetProperty("serverId").GetString());
        Assert.IsTrue(servers[0].GetProperty("isActive").GetBoolean());
        Assert.AreEqual(0, servers[0].GetProperty("pathMappings").GetArrayLength());

        // Neither this server's key nor any other's; and no trace the other server exists.
        StringAssert.DoesNotMatch(raw, Literal(_deskKey));
        StringAssert.DoesNotMatch(raw, Literal(_nasKey));
        StringAssert.DoesNotMatch(raw, Literal(_nas.ServerId));
    }

    [TestMethod]
    public async Task The_switcher_lists_this_device_first_then_every_managed_server()
    {
        var (_, body, raw) = await Call("/client/switcher");
        var data = body.GetProperty("data");

        Assert.AreEqual(_desk.ServerId, data.GetProperty("currentId").GetString());

        var targets = data.GetProperty("targets").EnumerateArray().ToList();
        Assert.AreEqual(3, targets.Count);

        Assert.AreEqual("local", targets[0].GetProperty("id").GetString());
        Assert.IsTrue(targets[0].GetProperty("isLocal").GetBoolean());
        Assert.IsFalse(targets[0].GetProperty("isCurrent").GetBoolean());

        var desk = targets.Single(t => t.GetProperty("id").GetString() == _desk.ServerId);
        Assert.IsTrue(desk.GetProperty("isCurrent").GetBoolean());
        Assert.AreEqual("Desk", desk.GetProperty("name").GetString());

        var nas = targets.Single(t => t.GetProperty("id").GetString() == _nas.ServerId);
        Assert.IsFalse(nas.GetProperty("isCurrent").GetBoolean());

        StringAssert.DoesNotMatch(raw, Literal(_deskKey));
        StringAssert.DoesNotMatch(raw, Literal(_nasKey));
    }

    private static int? StateOf(JsonElement body, string id)
    {
        var target = body.GetProperty("data").GetProperty("targets").EnumerateArray()
            .Single(t => t.GetProperty("id").GetString() == id);

        return target.TryGetProperty("state", out var state) ? state.GetInt32() : null;
    }

    private async Task<JsonElement> Switcher() => (await Call("/client/switcher")).Body;

    [TestMethod]
    public async Task The_switcher_says_how_each_managed_server_was_last_seen_and_asks_nobody_to_say_it()
    {
        // Nothing has asked yet. This device is not a managed server and carries no state.
        var body = await Switcher();

        Assert.IsNull(StateOf(body, "local"));
        Assert.AreEqual((int) ManagedServerState.Unknown, StateOf(body, _desk.ServerId));
        Assert.AreEqual((int) ManagedServerState.Unknown, StateOf(body, _nas.ServerId));

        // The NAS has since forgotten this device, and the devices page's probe found out.
        _nas.KnownDevices.Clear();
        await _console.Manager.GetAsync(probe: true);

        var deskRequests = _desk.Requests.Count;
        var nasRequests = _nas.Requests.Count;

        body = await Switcher();

        Assert.AreEqual((int) ManagedServerState.Revoked, StateOf(body, _nas.ServerId));
        Assert.AreEqual((int) ManagedServerState.Online, StateOf(body, _desk.ServerId));
        Assert.IsNull(StateOf(body, "local"));

        // Read from what the console already knew: the listing itself asked neither server.
        Assert.AreEqual(deskRequests, _desk.Requests.Count, "listing the switcher asked the desk");
        Assert.AreEqual(nasRequests, _nas.Requests.Count, "listing the switcher asked the NAS");
    }

    [TestMethod]
    public async Task This_relays_own_server_reads_online_once_it_has_answered_through_it()
    {
        // The last probe found the desk refusing this device — since put right on the desk.
        var device = _desk.KnownDevices.Single();
        _desk.KnownDevices.Clear();
        await _console.Manager.GetAsync(probe: true);
        _desk.KnownDevices[device.Key] = device.Value;

        // Until something has gone through this relay, that is all there is to go on.
        Assert.AreEqual((int) ManagedServerState.Revoked, StateOf(await Switcher(), _desk.ServerId));

        // Its page loading here is the server answering: reachable, and taking this device.
        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(_port, "/")).StatusCode);

        var body = await Switcher();
        Assert.AreEqual((int) ManagedServerState.Online, StateOf(body, _desk.ServerId));

        // Another server's entry is only ever what the console last knew of it.
        Assert.AreEqual((int) ManagedServerState.Online, StateOf(body, _nas.ServerId));
        _nas.KnownDevices.Clear();
        await _console.Manager.GetAsync(probe: true);
        Assert.AreEqual((int) ManagedServerState.Revoked, StateOf(await Switcher(), _nas.ServerId));
    }

    [TestMethod]
    public async Task This_relays_own_server_reads_as_its_latest_answer_through_it()
    {
        _desk.RefusesUnknownDevices = true;

        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(_port, "/resource/search")).StatusCode);
        Assert.AreEqual((int) ManagedServerState.Online, StateOf(await Switcher(), _desk.ServerId));

        // Revoked on the desk while its page is open: the page's next request is refused
        // as a device the desk no longer knows.
        _desk.KnownDevices.Clear();
        var refused = await ConsoleHarness.SendToRelayAsync(_port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.AreEqual((int) ManagedServerState.Revoked, StateOf(await Switcher(), _desk.ServerId));

        // Then gone altogether: nothing answers at its address any more.
        using var vacated = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        vacated.Start();
        var closed = ((IPEndPoint) vacated.LocalEndpoint).Port;
        vacated.Stop();

        await _console.Store.MutateAsync(data =>
            data.Servers.Single(s => s.ServerId == _desk.ServerId).BaseAddress = $"http://127.0.0.1:{closed}");

        var unreachable = await ConsoleHarness.SendToRelayAsync(_port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, unreachable.StatusCode);
        Assert.AreEqual((int) ManagedServerState.Offline, StateOf(await Switcher(), _desk.ServerId));

        // None of which the relay's own answers count as: its /client API is not the server.
        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(_port, "/client/status")).StatusCode);
        Assert.AreEqual((int) ManagedServerState.Offline, StateOf(await Switcher(), _desk.ServerId));
    }

    [TestMethod]
    // Anything the gate let through is the server taking this device, whatever the route said.
    [DataRow(200, null, ManagedServerState.Online)]
    [DataRow(404, null, ManagedServerState.Online)]
    [DataRow(500, null, ManagedServerState.Online)]
    // Refusals about a path, not about this device.
    [DataRow(403, nameof(RemoteAccessDenialReason.HostOnly), ManagedServerState.Online)]
    [DataRow(403, nameof(RemoteAccessDenialReason.PathNotServable), ManagedServerState.Online)]
    [DataRow(403, nameof(RemoteAccessDenialReason.RunsOnUserMachine), ManagedServerState.Online)]
    // This device's standing — read as the console's own probe reads it.
    [DataRow(401, nameof(RemoteAccessDenialReason.DeviceRevoked), ManagedServerState.Revoked)]
    [DataRow(401, nameof(RemoteAccessDenialReason.Unauthenticated), ManagedServerState.Revoked)]
    [DataRow(403, nameof(RemoteAccessDenialReason.Disabled), ManagedServerState.Offline)]
    [DataRow(401, nameof(RemoteAccessDenialReason.SignatureExpired), ManagedServerState.Offline)]
    public void An_answer_through_the_relay_reads_as_the_consoles_own_probe_would(int status, string? denial,
        ManagedServerState expected)
    {
        Assert.AreEqual(expected, UpstreamStanding.Classify(status, denial));
    }

    [TestMethod]
    public async Task Switching_to_another_server_starts_its_relay_and_returns_a_ticketed_url()
    {
        var (status, body, _) = await Call($"/client/switcher/{_nas.ServerId}/open", HttpMethod.Post,
            "{\"path\":\"/#/resource\"}");

        Assert.AreEqual(HttpStatusCode.OK, status);

        var url = new Uri(body.GetProperty("data").GetProperty("url").GetString()!);
        Assert.AreEqual(_console.Manager.RunningRelays[_nas.ServerId], url.Port);
        StringAssert.StartsWith(url.Query, $"?{RelayNavigationTokens.QueryName}=");
        Assert.AreEqual("#/resource", url.Fragment);
    }

    [TestMethod]
    public async Task Switching_to_this_device_goes_to_the_origin_its_window_opened_at()
    {
        // Before the window has reported anything: the server's own first address.
        var (_, body, _) = await Call("/client/switcher/local/open", HttpMethod.Post);
        Assert.AreEqual($"http://localhost:{_console.ServicePort}/",
            body.GetProperty("data").GetProperty("url").GetString());

        // Once it has — a debug build's window is the frontend dev server, not the server —
        // exactly that origin, so the browser's storage is the same one.
        _console.LocalOrigin.Set("http://localhost:3000/#/resource");

        (_, body, _) = await Call("/client/switcher/local/open", HttpMethod.Post, "{\"path\":\"/#/settings\"}");
        Assert.AreEqual("http://localhost:3000/#/settings", body.GetProperty("data").GetProperty("url").GetString());
    }

    [TestMethod]
    public async Task Switching_to_a_server_whose_relay_cannot_start_is_a_503()
    {
        // The NAS's remembered port and every other in range are held by something else.
        using var squatter = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var taken = ((IPEndPoint) squatter.LocalEndpoint).Port;

        _console.Get<RemoteConsoleOptions>().FirstRelayPort = taken;
        _console.Get<RemoteConsoleOptions>().RelayPortRange = 1;
        await _console.Store.MutateAsync(data => data.Servers.Single(s => s.ServerId == _nas.ServerId).RelayPort = taken);

        var (status, body, _) = await Call($"/client/switcher/{_nas.ServerId}/open", HttpMethod.Post);

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, status);
        Assert.AreEqual("RelayUnavailable", body.GetProperty("message").GetString());
    }

    [TestMethod]
    public async Task Switching_to_an_unknown_server_is_404()
    {
        var (status, body, _) = await Call("/client/switcher/nobody/open", HttpMethod.Post);

        Assert.AreEqual(HttpStatusCode.NotFound, status);
        Assert.AreEqual(404, body.GetProperty("code").GetInt32());
    }

    [TestMethod]
    public async Task Thin_client_management_is_refused_as_managed_by_the_host()
    {
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Post, "/client/connect"),
            (HttpMethod.Post, "/client/pair/code"),
            (HttpMethod.Post, "/client/pair/request"),
            (HttpMethod.Post, "/client/pair/claim"),
            (HttpMethod.Post, $"/client/servers/{_nas.ServerId}/activate"),
            (HttpMethod.Delete, $"/client/servers/{_desk.ServerId}"),
            (HttpMethod.Get, "/client/discover")
        ];

        foreach (var (method, path) in routes)
        {
            var (status, body, _) = await Call(path, method,
                method == HttpMethod.Post ? "{\"address\":\"127.0.0.1:1\",\"code\":\"123456\"}" : null);

            Assert.AreEqual(HttpStatusCode.Conflict, status, $"{method} {path}");
            Assert.AreEqual(409, body.GetProperty("code").GetInt32(), $"{method} {path}");
            Assert.AreEqual(ConsoleEndpoints.ManagedByHost, body.GetProperty("message").GetString(), $"{method} {path}");
        }

        // And nothing happened: the server a page tried to delete is still managed.
        Assert.IsNotNull(_console.Store.Find(_desk.ServerId));
    }

    [TestMethod]
    public async Task Thin_client_updater_and_migration_export_are_not_here_and_not_forwarded()
    {
        (HttpMethod Method, string Path)[] routes =
        [
            (HttpMethod.Get, "/client/migration-hints"),
            (HttpMethod.Post, "/client/migration-hints/export"),
            (HttpMethod.Get, "/client/updater/new-version"),
            (HttpMethod.Get, "/client/updater/state"),
            (HttpMethod.Post, "/client/updater/update"),
            (HttpMethod.Post, "/client/updater/restart"),
            (HttpMethod.Get, "/client/anything-else")
        ];

        foreach (var (method, path) in routes)
        {
            var (status, _, _) = await Call(path, method);

            Assert.AreEqual(HttpStatusCode.NotFound, status, $"{method} {path}");
        }

        Assert.IsFalse(_desk.Requests.Any(r => r.Path.StartsWith("/client", StringComparison.Ordinal)),
            "a /client request reached the server");
    }

    [TestMethod]
    public async Task The_tray_report_is_accepted_and_ignored()
    {
        var (status, body, _) = await Call("/client/tray", HttpMethod.Post, "{\"running\":true}");

        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.IsFalse(body.GetProperty("data").GetProperty("applied").GetBoolean());
    }

    [TestMethod]
    public async Task Path_mappings_can_be_set_for_this_relays_server_only()
    {
        var (status, body, _) = await Call($"/client/servers/{_desk.ServerId}/path-mappings", HttpMethod.Put,
            "{\"mappings\":[{\"serverPath\":\"/data/media\",\"localPath\":\"/Volumes/media\"}]}");

        Assert.AreEqual(HttpStatusCode.OK, status);
        Assert.IsTrue(body.GetProperty("data").GetProperty("changed").GetBoolean());

        var desk = _console.Store.Find(_desk.ServerId)!;
        Assert.AreEqual("/data/media", desk.PathMappings.Single().ServerPath);
        Assert.AreEqual("/Volumes/media", desk.PathMappings.Single().LocalPath);

        // Another server's, from this server's page: not this relay's business.
        (status, _, _) = await Call($"/client/servers/{_nas.ServerId}/path-mappings", HttpMethod.Put,
            "{\"mappings\":[{\"serverPath\":\"/x\",\"localPath\":\"/y\"}]}");

        Assert.AreEqual(HttpStatusCode.NotFound, status);
        Assert.AreEqual(0, _console.Store.Find(_nas.ServerId)!.PathMappings.Count);

        // What the relay itself maps with is the new table, without a restart.
        var (_, statusBody, _) = await Call("/client/status");
        Assert.AreEqual("/data/media", statusBody.GetProperty("data").GetProperty("servers")[0]
            .GetProperty("pathMappings")[0].GetProperty("serverPath").GetString());
    }

    [TestMethod]
    public async Task The_unavailable_page_links_back_to_this_device()
    {
        _console.LocalOrigin.Set("http://localhost:34567/#/resource");

        var response = await ConsoleHarness.SendToRelayAsync(_port, RelayPaths.ConnectPath);
        var html = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.StartsWith(response.Content.Headers.ContentType!.MediaType, "text/html");
        StringAssert.Contains(html, "href=\"http://localhost:34567/\"");
        Assert.IsTrue(response.Headers.TryGetValues("Content-Security-Policy", out var policy));
        StringAssert.Contains(policy.Single(), "default-src 'none'");
    }

    [TestMethod]
    public async Task A_relay_whose_server_is_gone_sends_the_window_to_the_unavailable_page()
    {
        // The entry disappears under a running relay (forgotten from another window, say):
        // the relay must neither sign with a key it no longer has nor forward unsigned.
        await _console.Store.MutateAsync(data => data.Servers.RemoveAll(s => s.ServerId == _desk.ServerId));
        var before = _desk.Requests.Count;

        using var handler = new SocketsHttpHandler {AllowAutoRedirect = false, UseProxy = false};
        using var client = new HttpClient(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_port}/");
        request.Headers.Host = $"127.0.0.1:{_port}";
        request.Headers.Accept.ParseAdd("text/html");

        var response = await client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual(RelayPaths.ConnectPath, response.Headers.Location!.OriginalString);

        var fetch = await ConsoleHarness.SendToRelayAsync(_port, "/resource/search");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, fetch.StatusCode);
        Assert.AreEqual(before, _desk.Requests.Count, "the relay forwarded without a server");
    }

    private static System.Text.RegularExpressions.Regex Literal(string value) =>
        new(System.Text.RegularExpressions.Regex.Escape(value));
}
