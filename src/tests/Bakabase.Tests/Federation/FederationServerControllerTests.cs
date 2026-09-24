using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// <c>/federation/local/servers</c> end to end: through the Service's real gates, MVC and
/// the federation filters, with the desktop app's manager stood in for — or absent, as on a
/// headless server.
/// </summary>
[TestClass]
public class FederationServerControllerTests
{
    private ServiceGateHost _host = null!;
    private FakeManagedServerService? _managed;

    private async Task StartAsync(bool withManager)
    {
        _managed = withManager ? new FakeManagedServerService() : null;
        _host = await ServiceGateHost.StartAsync([typeof(FederationServerController)], services =>
        {
            if (_managed != null)
            {
                services.AddSingleton<IManagedServerService>(_managed);
            }
        });
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_host != null)
        {
            await _host.DisposeAsync();
        }
    }

    /// <summary>What this device's own window sends: same origin, fetch metadata and all.</summary>
    private Task<HttpResponseMessage> Local(HttpMethod method, string path, string? json = null) =>
        _host.SendAsync(method, "/federation/local/servers" + path, "same-origin", _host.Origin, json);

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, response.StatusCode, text);
        StringAssert.StartsWith(response.Content.Headers.ContentType?.MediaType, "application/json");
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Code(JsonElement error) => error.GetProperty("code").GetString()!;

    [TestMethod]
    public async Task The_listing_is_what_the_manager_reports_in_the_federation_wire_format()
    {
        await StartAsync(withManager: true);

        var response = await Local(HttpMethod.Get, "?probe=true");
        var body = await Json(response, HttpStatusCode.OK);

        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());
        Assert.IsTrue(body.GetProperty("available").GetBoolean());

        // camelCase, enums as numbers — what the SDK's constants describe.
        var server = body.GetProperty("servers").EnumerateArray().Single();
        Assert.AreEqual(FakeManagedServerService.KnownServerId, server.GetProperty("serverId").GetString());
        Assert.AreEqual((int) ManagedServerState.Online, server.GetProperty("state").GetInt32());
        Assert.AreEqual((int) RemoteAccessMode.Unrestricted, server.GetProperty("mode").GetInt32());
        Assert.IsTrue(server.GetProperty("importedFromLegacyClient").GetBoolean());
        Assert.AreEqual("/volume1/media",
            server.GetProperty("pathMappings")[0].GetProperty("serverPath").GetString());
        Assert.IsFalse(server.TryGetProperty("key", out _), "a key never leaves the manager");

        // Whether a request is still being waited on travels on its own, next to what its
        // last attempt said: a transient failure is not an ending.
        var requests = body.GetProperty("requests").EnumerateArray().ToList();
        Assert.AreEqual(2, requests.Count);
        Assert.AreEqual("request-1", requests[0].GetProperty("requestId").GetString());
        Assert.AreEqual((int) ManagedServerOutcome.Unreachable, requests[0].GetProperty("outcome").GetInt32());
        Assert.IsTrue(requests[0].GetProperty("active").GetBoolean());
        Assert.AreEqual((int) ManagedServerOutcome.RequestRejected, requests[1].GetProperty("outcome").GetInt32());
        Assert.IsFalse(requests[1].GetProperty("active").GetBoolean());

        await Local(HttpMethod.Get, "");
        CollectionAssert.AreEqual(new[] {"Get probe=True", "Get probe=False"}, _managed!.Calls.ToArray());
    }

    [TestMethod]
    public async Task Every_route_reaches_the_manager_with_what_the_caller_sent()
    {
        await StartAsync(withManager: true);
        var id = FakeManagedServerService.KnownServerId;

        var probe = await Json(await Local(HttpMethod.Post, "/probe", """{"address":"192.168.1.9:34567"}"""),
            HttpStatusCode.OK);
        Assert.AreEqual("server-2", probe.GetProperty("serverId").GetString());
        Assert.IsTrue(probe.GetProperty("pairingSupported").GetBoolean());

        var withCode = await Json(await Local(HttpMethod.Post, "/pair", """{"address":"192.168.1.9","code":"123456"}"""),
            HttpStatusCode.OK);
        Assert.AreEqual((int) ManagedServerOutcome.Ok, withCode.GetProperty("outcome").GetInt32());

        var filed = await Json(await Local(HttpMethod.Post, "/pair", """{"address":"192.168.1.9"}"""),
            HttpStatusCode.OK);
        Assert.AreEqual((int) ManagedServerOutcome.AwaitingApproval, filed.GetProperty("outcome").GetInt32());
        Assert.AreEqual("request-2", filed.GetProperty("requestId").GetString());

        Assert.IsTrue((await Json(await Local(HttpMethod.Delete, "/requests/request-1"), HttpStatusCode.OK))
            .GetProperty("changed").GetBoolean());
        Assert.IsFalse((await Json(await Local(HttpMethod.Delete, "/requests/request-9"), HttpStatusCode.OK))
            .GetProperty("changed").GetBoolean());

        var mapped = await Json(await Local(HttpMethod.Put, $"/{id}/path-mappings",
                """{"mappings":[{"serverPath":"/volume1/media","localPath":"Z:\\media"},{"serverPath":"/volume1/comics","localPath":"Y:\\"}]}"""),
            HttpStatusCode.OK);
        Assert.IsTrue(mapped.GetProperty("changed").GetBoolean());
        Assert.AreEqual(2, _managed!.LastMappings!.Count);
        Assert.AreEqual(@"Z:\media", _managed.LastMappings[0].LocalPath);

        var emptied = await Json(await Local(HttpMethod.Put, $"/{id}/path-mappings", """{"mappings":[]}"""),
            HttpStatusCode.OK);
        Assert.IsTrue(emptied.GetProperty("changed").GetBoolean());
        Assert.AreEqual(0, _managed.LastMappings.Count, "an empty list clears the mappings");

        var opened = await Json(await Local(HttpMethod.Post, $"/{id}/open", """{"path":"/resource"}"""),
            HttpStatusCode.OK);
        Assert.AreEqual("http://127.0.0.1:34650/resource?__bakabase_switch=token", opened.GetProperty("url").GetString());

        var root = await Json(await Local(HttpMethod.Post, $"/{id}/open", "{}"), HttpStatusCode.OK);
        StringAssert.StartsWith(root.GetProperty("url").GetString(), "http://127.0.0.1:34650/?");

        var imported = await Json(await Local(HttpMethod.Post, "/import-legacy-client"), HttpStatusCode.OK);
        Assert.IsTrue(imported.GetProperty("found").GetBoolean());
        Assert.AreEqual(2, imported.GetProperty("imported").GetInt32());
        Assert.AreEqual(1, imported.GetProperty("skipped").GetInt32());

        Assert.IsTrue((await Json(await Local(HttpMethod.Delete, $"/{id}"), HttpStatusCode.OK))
            .GetProperty("changed").GetBoolean());

        CollectionAssert.AreEqual(new[]
        {
            "Probe 192.168.1.9:34567", "Pair 192.168.1.9 code=123456", "Pair 192.168.1.9 code=<none>",
            "Cancel request-1", "Cancel request-9", $"Map {id} 2", $"Map {id} 0", $"Open {id} path=/resource",
            $"Open {id} path=<root>", "Import", $"Forget {id}"
        }, _managed.Calls.ToArray());
    }

    [TestMethod]
    public async Task Discovery_is_what_the_manager_found_in_the_federation_wire_format()
    {
        await StartAsync(withManager: true);

        var response = await Local(HttpMethod.Get, "/discover");
        var body = await Json(response, HttpStatusCode.OK);

        Assert.AreEqual("no-store", response.Headers.CacheControl?.ToString());

        var servers = body.GetProperty("servers").EnumerateArray().ToList();
        Assert.AreEqual(2, servers.Count);
        Assert.AreEqual(FakeManagedServerService.KnownServerId, servers[0].GetProperty("serverId").GetString());
        Assert.AreEqual("Living room NAS", servers[0].GetProperty("name").GetString());
        Assert.AreEqual("http://192.168.1.5:34567", servers[0].GetProperty("address").GetString());
        Assert.AreEqual("2.4.0", servers[0].GetProperty("appVersion").GetString());
        Assert.IsTrue(servers[0].GetProperty("alreadyManaged").GetBoolean());
        Assert.AreEqual("server-2", servers[1].GetProperty("serverId").GetString());
        Assert.IsFalse(servers[1].GetProperty("alreadyManaged").GetBoolean());

        CollectionAssert.AreEqual(new[] {"Discover"}, _managed!.Calls.ToArray());
    }

    [TestMethod]
    public async Task Discovery_is_only_for_this_devices_own_window()
    {
        await StartAsync(withManager: true);

        // Another machine on the LAN, and another page on this one — a managed server's own
        // UI in its relay — may not make this device search its network.
        var lan = _host.Request(HttpMethod.Get, "/federation/local/servers/discover");
        lan.Headers.TryAddWithoutValidation(ServiceGateHost.RemoteIpHeader, "192.168.1.20");
        Assert.AreEqual("LocalInterfaceOnly", Code(await Json(await _host.SendAsync(lan), HttpStatusCode.Forbidden)));

        var relay = await _host.SendAsync(HttpMethod.Get, "/federation/local/servers/discover", "same-site",
            ServiceGateHost.RelayOrigin);
        Assert.AreEqual(HttpStatusCode.Forbidden, relay.StatusCode);

        // Nor through any other method.
        foreach (var method in new[] {HttpMethod.Post, HttpMethod.Put})
        {
            Assert.AreEqual("NodeRouteForbidden",
                Code(await Json(await Local(method, "/discover", "{}"), HttpStatusCode.Forbidden)), method.Method);
        }

        Assert.IsTrue(_managed!.Calls.IsEmpty, string.Join(", ", _managed.Calls));
    }

    [TestMethod]
    public async Task Opening_a_server_this_device_does_not_manage_is_not_found()
    {
        await StartAsync(withManager: true);

        var error = await Json(await Local(HttpMethod.Post, "/someone-else/open", "{}"), HttpStatusCode.NotFound);

        Assert.AreEqual("ServerNotManaged", Code(error));
        Assert.IsFalse(error.GetProperty("retryable").GetBoolean());
    }

    [TestMethod]
    public async Task Without_a_manager_the_listing_says_unavailable_and_everything_else_refuses()
    {
        await StartAsync(withManager: false);

        var listing = await Json(await Local(HttpMethod.Get, "?probe=true"), HttpStatusCode.OK);
        Assert.IsFalse(listing.GetProperty("available").GetBoolean());
        Assert.AreEqual(0, listing.GetProperty("servers").GetArrayLength());
        Assert.AreEqual(0, listing.GetProperty("requests").GetArrayLength());

        foreach (var (method, path, json) in new (HttpMethod, string, string?)[]
                 {
                     // A headless server has nothing to manage from, so it does not search the
                     // network for anything to manage either.
                     (HttpMethod.Get, "/discover", null),
                     (HttpMethod.Post, "/probe", """{"address":"192.168.1.9"}"""),
                     (HttpMethod.Post, "/pair", """{"address":"192.168.1.9"}"""),
                     (HttpMethod.Delete, "/requests/request-1", null),
                     (HttpMethod.Delete, "/server-1", null),
                     (HttpMethod.Put, "/server-1/path-mappings", """{"mappings":[]}"""),
                     (HttpMethod.Post, "/server-1/open", "{}"),
                     (HttpMethod.Post, "/import-legacy-client", null)
                 })
        {
            var error = await Json(await Local(method, path, json), HttpStatusCode.NotFound);
            Assert.AreEqual("ManagementUnavailable", Code(error), $"{method} {path}");
        }
    }

    [TestMethod]
    public async Task The_interface_is_only_for_the_computer_it_runs_on()
    {
        await StartAsync(withManager: true);

        foreach (var mode in new[] {RemoteAccessMode.Enabled, RemoteAccessMode.Unrestricted})
        {
            _host.Remote.Mode = mode;

            // Another machine, even one the legacy gate would let anywhere.
            var lan = _host.Request(HttpMethod.Get, "/federation/local/servers");
            lan.Headers.TryAddWithoutValidation(ServiceGateHost.RemoteIpHeader, "192.168.1.20");
            Assert.AreEqual("LocalInterfaceOnly", Code(await Json(await _host.SendAsync(lan), HttpStatusCode.Forbidden)),
                mode.ToString());
        }

        // A name that only resolves here: DNS rebinding.
        var rebound = _host.Request(HttpMethod.Post, "/federation/local/servers/import-legacy-client");
        rebound.Headers.Host = $"attacker.example:{_host.Port}";
        Assert.AreEqual("LocalInterfaceOnly",
            Code(await Json(await _host.SendAsync(rebound), HttpStatusCode.Forbidden)));

        // Another page on this machine: a relay, or yarn dev's port with the wrong number.
        foreach (var origin in new[] {ServiceGateHost.RelayOrigin, "http://localhost:3001", "https://attacker.example"})
        {
            var response = await _host.SendAsync(HttpMethod.Post, "/federation/local/servers/import-legacy-client",
                "same-site", origin);
            Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, origin);
        }

        Assert.IsTrue(_managed!.Calls.IsEmpty, string.Join(", ", _managed.Calls));
    }

    [TestMethod]
    public async Task Node_credentials_and_off_policy_routes_never_reach_the_controller()
    {
        await StartAsync(withManager: true);

        var signed = _host.Request(HttpMethod.Get, "/federation/local/servers");
        signed.Headers.TryAddWithoutValidation("Authorization", "Bakabase-Node reader.grant.nonce.1.a.b");
        Assert.AreEqual("NodeRouteForbidden", Code(await Json(await _host.SendAsync(signed), HttpStatusCode.Forbidden)));

        foreach (var (method, path) in new[]
                 {
                     (HttpMethod.Post, ""), (HttpMethod.Get, "/probe"), (HttpMethod.Get, "/server-1/open"),
                     (HttpMethod.Patch, "/server-1"), (HttpMethod.Post, "/server-1/path-mappings"),
                     (HttpMethod.Post, "/a.b/open"), (HttpMethod.Delete, "/requests/request-1/extra")
                 })
        {
            var error = await Json(await Local(method, path), HttpStatusCode.Forbidden);
            Assert.AreEqual("NodeRouteForbidden", Code(error), $"{method} {path}");
        }

        Assert.IsTrue(_managed!.Calls.IsEmpty, string.Join(", ", _managed.Calls));
    }
}
