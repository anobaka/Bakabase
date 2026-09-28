using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Remoting.Components.Connection;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Holds the pairing routes and the middleware's anonymous allowlist against each other,
/// and asks the real gates who may manage this server's devices.
/// </summary>
/// <remarks>
/// <para>
/// The two have to agree, and neither direction of disagreement is visible by reading
/// one file: a management route that slips under the allowlist lets an unpaired caller
/// approve itself, and a pairing route that falls outside it locks out the very device
/// that has no credentials yet. This is not hypothetical — writing the controller
/// against a <c>/remote-access/pair</c> prefix (no trailing slash) produced the first of
/// those, because it also matched <c>/remote-access/pairing/</c>.
/// </para>
/// <para>
/// Nor is the allowlist the only way in. Marking the management routes
/// <c>[RemoteAccessible]</c> so a phone could look after devices let any unpaired caller on
/// the LAN of an Enabled server file a request, approve it and collect a key with full
/// control (#1454). A paired device never needed the mark, so the tests at the end call
/// every management route as each kind of caller, through the real gates.
/// </para>
/// </remarks>
[TestClass]
public class PairingEndpointExposureTests
{
    /// <summary>Everything a device with no key may call, by prefix.</summary>
    private const string AnonymousPrefix = "/remote-access/pair/";

    /// <summary>Everything only the host or an already-paired device may call.</summary>
    private static readonly string[] ManagementPrefixes =
    [
        "/remote-access/pairing/",
        "/remote-access/devices"
    ];

    /// <summary>A caller on the LAN, as the test host stands one in.</summary>
    private const string LanAddress = "192.168.1.50";

    private sealed record Route(string Method, string Path, bool RemoteAccessible)
    {
        public override string ToString() => $"{Method} {Path}";
    }

    private static IReadOnlyList<Route> Routes()
    {
        var prefix = typeof(RemoteAccessController).GetCustomAttribute<RouteAttribute>()!.Template
            .TrimStart('~').Trim('/');

        return typeof(RemoteAccessController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(action => action.GetCustomAttributes<HttpMethodAttribute>()
                .SelectMany(http => http.HttpMethods.Select(method =>
                {
                    var template = http.Template?.Trim('/') ?? string.Empty;
                    var path = template.Length == 0 ? $"/{prefix}" : $"/{prefix}/{template}";
                    return new Route(method, path,
                        action.GetCustomAttribute<RemoteAccessibleAttribute>()?.Allowed ?? false);
                })))
            .ToArray();
    }

    private static Route[] ManagementRoutes() =>
        Routes()
            .Where(r => ManagementPrefixes.Any(p => r.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    private static bool IsAllowlisted(string path) =>
        RemoteAccessMiddleware.AnonymousPathPrefixes.Any(p =>
            path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    [TestMethod]
    public void Every_pairing_route_is_reachable_without_credentials()
    {
        // Both gates have to let these through: the middleware's RequirePairing check
        // and the per-action filter. Missing either one means a fresh device cannot
        // pair, which is the one thing this group of routes exists for.
        var routes = Routes().Where(r => r.Path.StartsWith(AnonymousPrefix, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsTrue(routes.Length >= 3, $"expected the pair/ routes to exist, found {routes.Length}");

        var unmarked = routes.Where(r => !r.RemoteAccessible).Select(r => r.ToString()).ToArray();
        Assert.AreEqual(0, unmarked.Length, $"not [RemoteAccessible]: {string.Join(", ", unmarked)}");

        var unlisted = routes.Where(r => !IsAllowlisted(r.Path)).Select(r => r.ToString()).ToArray();
        Assert.AreEqual(0, unlisted.Length, $"outside the anonymous allowlist: {string.Join(", ", unlisted)}");
    }

    [TestMethod]
    public void No_management_route_is_reachable_without_credentials()
    {
        var routes = ManagementRoutes();

        Assert.IsTrue(routes.Length >= 5, $"expected the management routes to exist, found {routes.Length}");

        // The property that matters, and the one [RemoteAccessible] does not provide:
        // being in this list means a caller with no key at all gets through. Approving
        // your own pairing request is exactly what that would allow.
        var listed = routes.Where(r => IsAllowlisted(r.Path)).Select(r => r.ToString()).ToArray();
        Assert.AreEqual(0, listed.Length, $"must not be in the anonymous allowlist: {string.Join(", ", listed)}");
    }

    [TestMethod]
    public void No_management_route_is_marked_for_other_devices()
    {
        // A paired device may look after devices — see them, name them, cut one off, and
        // let a new one in: a headless server has nobody standing at it to click approve.
        // It gets there without the mark: the authorization filter lets paired devices,
        // and every caller of an Unrestricted server, through before it looks for one. So
        // on a management route the mark adds exactly one kind of caller — one that has
        // not paired — and an unpaired caller that may approve requests approves its own.
        var marked = ManagementRoutes().Where(r => r.RemoteAccessible).Select(r => r.ToString()).ToArray();

        Assert.AreEqual(0, marked.Length, $"marked [RemoteAccessible]: {string.Join(", ", marked)}");
    }

    [TestMethod]
    public void Nothing_that_configures_the_server_is_open_to_another_device()
    {
        // These decide whether remote access exists at all, what it may transcode, and
        // whether pairing is required — and the settings page also hands out every
        // address this server can be reached on. They belong to whoever is sitting at the
        // machine. Issuing a pairing code sits here too: it hands out access with no
        // approval step, which is a different thing from approving a request that names
        // the device asking.
        string[] hostOnly =
        [
            "GET /remote-access/settings",
            "PUT /remote-access/mode",
            "PUT /remote-access/live-transcode",
            "PUT /remote-access/require-pairing",
            "POST /remote-access/pairing/code"
        ];

        var routes = Routes().ToDictionary(r => r.ToString(), r => r.RemoteAccessible);

        foreach (var route in hostOnly)
        {
            Assert.IsTrue(routes.ContainsKey(route), $"{route} no longer exists under that name");
            Assert.IsFalse(routes[route], $"{route} must stay host-only");
        }
    }

    [TestMethod]
    public void Nothing_carries_a_secret_in_its_url()
    {
        // A pairing code or a request id in a path or query string ends up in access
        // logs, proxy caches and browser history. They travel in bodies instead, which
        // is why all three pair/ routes are POSTs.
        var offenders = Routes()
            .Where(r => r.Path.StartsWith(AnonymousPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(r => r.Path.Contains('{') || !string.Equals(r.Method, "POST", StringComparison.Ordinal))
            .Select(r => r.ToString())
            .ToArray();

        Assert.AreEqual(0, offenders.Length, $"secrets belong in the body: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void The_allowlist_has_no_prefix_that_swallows_a_management_route()
    {
        // Guards the allowlist itself, independent of what the controller currently
        // declares: a prefix like "/remote-access/pair" (no slash) would pass the tests
        // above today and start swallowing management routes the moment one is added.
        var swallowed = RemoteAccessMiddleware.AnonymousPathPrefixes
            .SelectMany(prefix => ManagementPrefixes
                .Where(m => m.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(m => $"{prefix} covers {m}"))
            .ToArray();

        Assert.AreEqual(0, swallowed.Length, string.Join(", ", swallowed));
    }

    // ---- through the real gates ----

    /// <summary>
    /// A request to <paramref name="route"/> with its <c>{id}</c> filled in: from the LAN,
    /// or from this machine when <paramref name="remoteIp"/> is null.
    /// </summary>
    private static HttpRequestMessage Call(ServiceGateHost host, Route route, string? remoteIp,
        string id = "someone")
    {
        var request = host.Request(new HttpMethod(route.Method), route.Path.Replace("{id}", id),
            json: route.Method == "PUT" ? """{"name":"Renamed"}""" : null);
        if (remoteIp != null)
        {
            request.Headers.Add(ServiceGateHost.RemoteIpHeader, remoteIp);
        }

        return request;
    }

    private static HttpRequestMessage FromTheLan(ServiceGateHost host, HttpMethod method, string path,
        string? json = null)
    {
        var request = host.Request(method, path, json: json);
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, LanAddress);
        return request;
    }

    private static async Task<PairingCredentials> PairAsync(ServiceGateHost host, string name)
    {
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var code = await devices.IssuePairingCodeAsync();
        return (await devices.PairWithCodeAsync(code.Code, name, RemoteDevicePlatform.Android)).Credentials!;
    }

    /// <summary>Signed with the device's key, the way the phone and the desktop app's relay sign.</summary>
    private static async Task<HttpResponseMessage> SendSignedAsync(ServiceGateHost host, HttpRequestMessage request,
        PairingCredentials device)
    {
        await UpstreamRequestSigner.SignAsync(request, device.DeviceId,
            RemoteRequestSignature.FromBase64Url(device.Key), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return await host.SendAsync(request);
    }

    private static void AssertRefused(HttpResponseMessage response, string because)
    {
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode, because);
        Assert.IsTrue(response.Headers.TryGetValues("X-Bakabase-Remote-Access", out var reason), because);
        Assert.AreEqual(nameof(RemoteAccessDenialReason.HostOnly), reason.Single(), because);
    }

    private static void AssertAdmitted(HttpResponseMessage response, string because)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, because);
        Assert.IsFalse(response.Headers.Contains("X-Bakabase-Remote-Access"), because);
    }

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, body.RootElement.GetProperty("code").GetInt32());
        return body.RootElement.GetProperty("data").Clone();
    }

    [TestMethod]
    public async Task An_unpaired_caller_on_an_Enabled_server_is_refused_every_management_route()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(RemoteAccessController)]);
        host.Remote.Mode = RemoteAccessMode.Enabled;
        host.Remote.RequirePairing = false;

        foreach (var route in ManagementRoutes())
        {
            AssertRefused(await host.SendAsync(Call(host, route, LanAddress)), $"{route}, unpaired");
        }
    }

    [TestMethod]
    public async Task A_paired_device_the_host_and_callers_of_an_Unrestricted_server_manage_devices()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(RemoteAccessController)]);
        var phone = await PairAsync(host, "Phone");

        foreach (var route in ManagementRoutes())
        {
            host.Remote.Mode = RemoteAccessMode.Enabled;
            AssertAdmitted(await SendSignedAsync(host, Call(host, route, LanAddress), phone), $"{route}, paired");
            AssertAdmitted(await host.SendAsync(Call(host, route, null)), $"{route}, from this machine");

            host.Remote.Mode = RemoteAccessMode.Unrestricted;
            AssertAdmitted(await host.SendAsync(Call(host, route, LanAddress)), $"{route}, unpaired on Unrestricted");
        }

        // Admitted, and it does what it says: the phone lets a device in, which then has a key.
        host.Remote.Mode = RemoteAccessMode.Enabled;
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var request = await devices.RequestPairingAsync("Laptop", RemoteDevicePlatform.Windows, "192.168.1.9");
        var approve = ManagementRoutes().Single(r => r.Path.EndsWith("/approve", StringComparison.Ordinal));

        var approved = await DataOf(await SendSignedAsync(host, Call(host, approve, LanAddress, request.Id), phone));

        var claim = await devices.ClaimApprovedAsync(request.Id);
        Assert.IsTrue(claim.Succeeded);
        Assert.AreEqual(approved.GetProperty("deviceId").GetString(), claim.Credentials!.DeviceId);
        Assert.AreEqual(phone.DeviceId, devices.Find(claim.Credentials.DeviceId)!.ApprovedByDeviceId);
    }

    [TestMethod]
    public async Task An_unpaired_caller_cannot_let_itself_in()
    {
        // #1454 as it was done: ask to pair, approve the request, collect the key.
        await using var host = await ServiceGateHost.StartAsync([typeof(RemoteAccessController)]);
        host.Remote.Mode = RemoteAccessMode.Enabled;
        host.Remote.RequirePairing = false;
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var phone = await PairAsync(host, "Phone");

        // Asking is what pair/ is for.
        var asked = await DataOf(await host.SendAsync(FromTheLan(host, HttpMethod.Post, "/remote-access/pair/request",
            """{"deviceName":"Intruder","platform":1}""")));
        var requestId = asked.GetProperty("requestId").GetString()!;

        // Answering it is not.
        AssertRefused(await host.SendAsync(FromTheLan(host, HttpMethod.Post,
            $"/remote-access/pairing/requests/{requestId}/approve")), "approving its own request");

        // So there is no key to collect, and the request still waits for someone who may answer it.
        var claimed = await DataOf(await host.SendAsync(FromTheLan(host, HttpMethod.Post,
            "/remote-access/pair/claim", $$"""{"requestId":"{{requestId}}"}""")));
        Assert.AreEqual(JsonValueKind.Null, claimed.GetProperty("credentials").ValueKind);
        Assert.AreEqual((int) PairingFailure.NotYetApproved, claimed.GetProperty("failure").GetInt32());
        Assert.IsTrue(devices.GetPendingRequests().Any(r => r.Id == requestId));

        // Nor may it see who is paired or waiting, cut a device off, rename it, or turn a request away.
        AssertRefused(await host.SendAsync(FromTheLan(host, HttpMethod.Get, "/remote-access/devices")),
            "listing devices");
        AssertRefused(await host.SendAsync(FromTheLan(host, HttpMethod.Get, "/remote-access/pairing/requests")),
            "listing requests");
        AssertRefused(await host.SendAsync(FromTheLan(host, HttpMethod.Delete,
            $"/remote-access/devices/{phone.DeviceId}")), "revoking a device");
        AssertRefused(await host.SendAsync(FromTheLan(host, HttpMethod.Put,
            $"/remote-access/devices/{phone.DeviceId}/name", """{"name":"Mine now"}""")), "renaming a device");
        AssertRefused(await host.SendAsync(FromTheLan(host, HttpMethod.Post,
            $"/remote-access/pairing/requests/{requestId}/reject")), "rejecting a request");

        Assert.AreEqual("Phone", devices.Find(phone.DeviceId)?.Name);
        Assert.AreEqual(1, devices.GetDevices().Count);
    }
}
