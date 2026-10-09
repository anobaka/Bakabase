using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation.Security;

[TestClass]
public sealed class HeadlessFederationAdministrationTests
{
    private static Task<ServiceGateHost> Host(ServerKind kind = ServerKind.Headless) =>
        ServiceGateHost.StartAsync([typeof(HeadlessFederationProbeController), typeof(FederationServerController),
                typeof(RemoteAccessController)], services =>
            services.AddSingleton<IServerSelfDescription>(new ServerSelfDescription(() => kind)));

    private static HttpRequestMessage LanRequest(ServiceGateHost host, string path = "/federation/local/peers",
        string? origin = null, string? site = "same-origin")
    {
        var request = host.Request(HttpMethod.Get, path, site, origin ?? host.Origin);
        request.Headers.Add(ServiceGateHost.RemoteIpHeader, "192.168.20.8");
        return request;
    }

    [DataTestMethod]
    [DataRow(RemoteAccessMode.Unrestricted, false, HttpStatusCode.OK)]
    [DataRow(RemoteAccessMode.Enabled, false, HttpStatusCode.Forbidden)]
    [DataRow(RemoteAccessMode.Enabled, true, HttpStatusCode.Unauthorized)]
    [DataRow(RemoteAccessMode.Disabled, false, HttpStatusCode.Forbidden)]
    public async Task Headless_browser_administration_obeys_the_real_remote_gate(
        RemoteAccessMode mode, bool pairing, HttpStatusCode expected)
    {
        await using var host = await Host();
        host.Remote.Mode = mode;
        host.Remote.RequirePairing = pairing;
        using var request = LanRequest(host);
        using var response = await host.SendAsync(request);
        Assert.AreEqual(expected, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task Paired_headless_administrator_passes_but_invalid_and_node_credentials_do_not()
    {
        await using var host = await Host();
        host.Remote.RequirePairing = true;
        var devices = host.Services.GetRequiredService<IRemoteDeviceService>();
        var code = await devices.IssuePairingCodeAsync();
        var paired = await devices.PairWithCodeAsync(code.Code, "Desktop", RemoteDevicePlatform.MacOS);
        var credentials = paired.Credentials!;
        foreach (var valid in new[] { true, false })
        {
            using var request = LanRequest(host);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var nonce = Guid.NewGuid().ToString("N");
            var canonical = RemoteRequestSignature.BuildCanonicalString(credentials.DeviceId, "GET",
                "/federation/local/peers", "", timestamp, nonce, "");
            request.Headers.TryAddWithoutValidation("Authorization", RemoteRequestSignature.BuildHeader(
                credentials.DeviceId, timestamp, nonce, valid
                    ? RemoteRequestSignature.Sign(RemoteRequestSignature.FromBase64Url(credentials.Key), canonical)
                    : "invalid"));
            using var response = await host.SendAsync(request);
            Assert.AreEqual(valid ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        }
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var node = LanRequest(host);
        node.Headers.TryAddWithoutValidation("Authorization", "Bakabase-Node invalid");
        using var refused = await host.SendAsync(node);
        Assert.AreEqual(HttpStatusCode.Forbidden, refused.StatusCode);
    }

    [DataTestMethod]
    [DataRow("https://another.example", "same-origin")]
    [DataRow("null", "same-origin")]
    [DataRow(null, "cross-site")]
    [DataRow(null, "same-site")]
    public async Task Another_website_cannot_borrow_an_unrestricted_browser(string? origin, string site)
    {
        await using var host = await Host();
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = LanRequest(host, origin: origin, site: site);
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Desktop_federation_stays_local_and_headless_server_management_stays_unavailable()
    {
        await using var desktop = await Host(ServerKind.Desktop);
        desktop.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var request = LanRequest(desktop);
        using var denied = await desktop.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, denied.StatusCode);

        await using var server = await Host();
        server.Remote.Mode = RemoteAccessMode.Unrestricted;
        using var listing = LanRequest(server, "/federation/local/servers");
        using var response = await server.SendAsync(listing);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsFalse(body.RootElement.GetProperty("available").GetBoolean());
        using var discover = LanRequest(server, "/federation/local/servers/discover");
        using var unavailable = await server.SendAsync(discover);
        Assert.AreEqual(HttpStatusCode.NotFound, unavailable.StatusCode);
    }

    [TestMethod]
    public async Task Context_exposes_federation_to_headless_administration_without_desktop_actions()
    {
        await using var host = await Host();
        host.Remote.Mode = RemoteAccessMode.Unrestricted;
        host.Remote.Descriptor = host.Remote.Descriptor with { Kind = ServerKind.Headless };
        using var request = LanRequest(host, "/remote-access/context");
        using var response = await host.SendAsync(request);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.IsTrue(data.GetProperty("federationAvailable").GetBoolean());
        Assert.IsFalse(data.GetProperty("cookieCaptureAvailable").GetBoolean());
    }

    [TestMethod]
    public async Task Headless_local_media_never_launches_a_player_or_file_manager()
    {
        var controller = new FederationMediaController(null!, null!, null!, null!,
            new ServerSelfDescription(() => ServerKind.Headless));
        var player = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            controller.Prepare(new PlaybackSessionRequest(null!, "player"), default));
        Assert.AreEqual("PlayerUnavailable", player.Code);
        var directory = await Assert.ThrowsExactlyAsync<FederationQueryException>(() =>
            controller.OpenDirectory(new OpenResourceDirectoryRequest(null!), null!, default));
        Assert.AreEqual("OpenDirectoryUnavailable", directory.Code);
    }
}

[ApiController]
[Route("federation/local/peers")]
[FederationEndpoint(FederationEndpointKind.Local)]
public sealed class HeadlessFederationProbeController : ControllerBase
{
    [HttpGet]
    public object Get() => new { allowed = true, paired = HttpContext.GetRemoteAccessContext()?.IsPaired };
}
