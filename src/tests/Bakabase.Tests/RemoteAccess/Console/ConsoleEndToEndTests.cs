using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Remoting.Components.Forwarding;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Switching the window to a managed server, from the click in this device's own UI to the
/// request arriving at the other server: the Service's real request gates and controller in
/// front of the real manager, its relay, and a stand-in for the other server.
/// </summary>
[TestClass]
public class ConsoleEndToEndTests
{
    private ConsoleHarness _console = null!;
    private ServiceGateHost _service = null!;
    private FakeServer _desk = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync();
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);

        // The app's own server, with the manager the desktop host composes beside it.
        var manager = _console.Manager;
        _service = await ServiceGateHost.StartAsync([typeof(FederationServerController)],
            services => services.AddSingleton<IManagedServerService>(manager));

        // The window opened at the server's own origin.
        _console.LocalOrigin.Set(_service.Origin + "/");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _service.DisposeAsync();
        await _console.DisposeAsync();
        await _desk.DisposeAsync();
    }

    /// <summary>What this device's own window sends: same origin, fetch metadata and all.</summary>
    private async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> Local(HttpMethod method, string path,
        string? json = null)
    {
        var response = await _service.SendAsync(method, "/federation/local/servers" + path, "same-origin",
            _service.Origin, json);
        var raw = await response.Content.ReadAsStringAsync();

        return (response.StatusCode, JsonDocument.Parse(raw).RootElement.Clone(), raw);
    }

    [TestMethod]
    public async Task A_click_in_this_devices_window_reaches_the_other_server_signed_as_this_device()
    {
        var (deviceId, key) = await _console.AddManagedAsync(_desk);

        // The page asks where to go…
        var (status, body, raw) = await Local(HttpMethod.Post, $"/{_desk.ServerId}/open", """{"path":"/"}""");
        Assert.AreEqual(HttpStatusCode.OK, status, raw);

        var url = body.GetProperty("url").GetString()!;
        var port = _console.Manager.RunningRelays[_desk.ServerId];
        StringAssert.StartsWith(url, $"http://127.0.0.1:{port}/?{RelayNavigationTokens.QueryName}=");
        StringAssert.DoesNotMatch(raw, new Regex(Regex.Escape(key)));

        // …and assigns location, with the route in the hash as the SPA does. To the browser,
        // localhost:<service> to 127.0.0.1:<relay> is another site, and it sends the cookies
        // it holds for 127.0.0.1 — the port does not scope them.
        var navigation = url + "#/resource";
        var landing = await ConsoleHarness.NavigateAsync(navigation, "cross-site", cookie: "Bakabase=this-devices-own");
        Assert.AreEqual(HttpStatusCode.OK, landing.StatusCode, await landing.Content.ReadAsStringAsync());
        Assert.IsFalse(_desk.Requests.Any(r => r.Path == "/"), "the ticketed navigation itself was forwarded");

        // The landing page sends the window on from its own origin, fragment and all.
        var next = await ConsoleHarness.ContinuationOfAsync(landing, "#/resource");
        Assert.AreEqual($"http://127.0.0.1:{port}/#/resource", next);

        var page = await ConsoleHarness.NavigateAsync(next, "same-origin", cookie: "Bakabase=this-devices-own");
        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);

        // The page's own requests follow, same-origin.
        var api = await ConsoleHarness.SendToRelayAsync(port, "/resource/search?page=1");
        Assert.AreEqual(HttpStatusCode.OK, api.StatusCode);

        foreach (var path in new[] {"/", "/resource/search"})
        {
            var arrived = _desk.Requests.Single(r => r.Path == path);

            Assert.AreEqual(deviceId, arrived.DeviceId, path);
            Assert.IsTrue(arrived.SignatureValid, path);
            Assert.IsNull(arrived.Cookie, $"{path}: a cookie reached the other server");
            Assert.IsFalse(arrived.Query.Contains(RelayNavigationTokens.QueryName, StringComparison.OrdinalIgnoreCase),
                $"{path}: the ticket reached the other server");
        }

        Assert.AreEqual("page=1", _desk.Requests.Single(r => r.Path == "/resource/search").Query);
    }

    [TestMethod]
    public async Task Another_page_on_this_machine_cannot_open_a_server()
    {
        await _console.AddManagedAsync(_desk);

        // A managed server's own UI, running on its relay's origin, is another site to this
        // device's Service: it may not start a relay for itself, let alone for another.
        var response = await _service.SendAsync(HttpMethod.Post, $"/federation/local/servers/{_desk.ServerId}/open",
            "same-site", "http://127.0.0.1:47300", "{}");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, _console.Manager.RunningRelays.Count);
    }

    [TestMethod]
    public async Task The_listing_through_the_service_never_carries_a_key()
    {
        var (_, key) = await _console.AddManagedAsync(_desk);

        foreach (var path in new[] {"", "?probe=true"})
        {
            var (status, body, raw) = await Local(HttpMethod.Get, path);

            Assert.AreEqual(HttpStatusCode.OK, status, raw);
            Assert.AreEqual(_desk.ServerId, body.GetProperty("servers")[0].GetProperty("serverId").GetString());
            StringAssert.DoesNotMatch(raw, new Regex(Regex.Escape(key)), path);
            StringAssert.DoesNotMatch(raw, new Regex("deviceKey", RegexOptions.IgnoreCase), path);
        }
    }

    [TestMethod]
    public async Task Path_mappings_null_or_absent_clear_the_table()
    {
        await _console.AddManagedAsync(_desk);
        var path = $"/{_desk.ServerId}/path-mappings";

        foreach (var clearing in new[] {"""{"mappings":null}""", "{}"})
        {
            var (status, body, raw) = await Local(HttpMethod.Put, path,
                """{"mappings":[{"serverPath":"/volume1","localPath":"/Volumes/volume1"}]}""");
            Assert.AreEqual(HttpStatusCode.OK, status, raw);
            Assert.AreEqual(1, _console.Store.Find(_desk.ServerId)!.PathMappings.Count);

            (status, body, raw) = await Local(HttpMethod.Put, path, clearing);

            Assert.AreEqual(HttpStatusCode.OK, status, $"{clearing}: {raw}");
            Assert.IsTrue(body.GetProperty("changed").GetBoolean(), clearing);
            Assert.AreEqual(0, _console.Store.Find(_desk.ServerId)!.PathMappings.Count, clearing);
        }
    }

    [TestMethod]
    public async Task A_body_that_does_not_bind_is_refused_in_the_federation_shape()
    {
        await _console.AddManagedAsync(_desk);

        foreach (var (method, path, json, expected, code) in new (HttpMethod, string, string?, HttpStatusCode, string)[]
                 {
                     (HttpMethod.Put, $"/{_desk.ServerId}/path-mappings", """{"mappings":"not a list"}""",
                         HttpStatusCode.BadRequest, "InvalidRequest"),
                     (HttpMethod.Put, $"/{_desk.ServerId}/path-mappings", "", HttpStatusCode.BadRequest,
                         "InvalidRequest"),
                     (HttpMethod.Put, $"/{_desk.ServerId}/path-mappings", null, HttpStatusCode.UnsupportedMediaType,
                         "UnsupportedMediaType"),
                     (HttpMethod.Post, "/probe", "{}", HttpStatusCode.BadRequest, "InvalidRequest"),
                     (HttpMethod.Post, "/pair", """{"code":"123456"}""", HttpStatusCode.BadRequest, "InvalidRequest")
                 })
        {
            var (status, body, raw) = await Local(method, path, json);

            Assert.AreEqual(expected, status, $"{method} {path} {json}: {raw}");
            Assert.AreEqual(code, body.GetProperty("code").GetString(), raw);
            Assert.IsFalse(body.GetProperty("retryable").GetBoolean(), raw);
            Assert.IsFalse(body.TryGetProperty("errors", out _), $"MVC's problem answered: {raw}");
        }

        // Nothing reached the manager: the table is as it was, and no pairing was attempted.
        Assert.AreEqual(0, _console.Store.Find(_desk.ServerId)!.PathMappings.Count);
        Assert.IsFalse(_desk.Requests.Any(r => r.Path.StartsWith("/remote-access/pair", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task No_free_port_for_a_relay_is_a_retryable_refusal()
    {
        await _console.DisposeAsync();
        await _service.DisposeAsync();

        // A console with room for no relay at all: its one port is held by something else.
        using var squatter = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        squatter.Start();
        var taken = ((IPEndPoint) squatter.LocalEndpoint).Port;

        _console = await ConsoleHarness.StartAsync(options: o =>
        {
            o.FirstRelayPort = taken;
            o.RelayPortRange = 1;
        });
        var manager = _console.Manager;
        _service = await ServiceGateHost.StartAsync([typeof(FederationServerController)],
            services => services.AddSingleton<IManagedServerService>(manager));
        await _console.AddManagedAsync(_desk);

        var (status, body, raw) = await Local(HttpMethod.Post, $"/{_desk.ServerId}/open", "{}");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, status, raw);
        Assert.AreEqual("RelayUnavailable", body.GetProperty("code").GetString());
        Assert.IsTrue(body.GetProperty("retryable").GetBoolean());
    }
}
