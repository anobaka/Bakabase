using System.Net;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.OAuth;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class OAuthCallbackBoundaryTests
{
    private const string Callback = "/oauth/callback/microsoftGraph";

    [DataTestMethod]
    [DataRow("GET", Callback, true)]
    [DataRow("POST", Callback, false)]
    [DataRow("HEAD", Callback, false)]
    [DataRow("GET", Callback + "/", false)]
    [DataRow("GET", Callback + "/settings", false)]
    [DataRow("GET", "/oauth/callback/unknown", false)]
    [DataRow("GET", "/oauth/callback/MicrosoftGraph", false)]
    [DataRow("GET", "/oauth/providers/microsoftGraph/configuration", false)]
    public void OnlyRegisteredExactCallbackMethodsAndPathsAreAnonymous(string method, string path, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method; context.Request.Path = path;
        Assert.AreEqual(expected, OAuthProviderRegistry.IsCallbackRequest(context.Request));
    }

    [TestMethod]
    public void HttpsUiBehindTlsTerminationRequiresBrowserProofAndPreservedAuthority()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("bakabase.example.com", 8443);
        context.Request.Headers.Origin = "https://bakabase.example.com:8443";
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        Assert.IsTrue(OwnerUiOriginGuard.IsAllowed(context));
        context.Request.Headers["Sec-Fetch-Site"] = "same-site";
        Assert.IsFalse(OwnerUiOriginGuard.IsAllowed(context));
        context.Request.Headers.Remove("Sec-Fetch-Site");
        Assert.IsFalse(OwnerUiOriginGuard.IsAllowed(context));
        context.Request.Headers["Sec-Fetch-Site"] = "same-origin";
        context.Request.Headers.Origin = "https://other.example.com:8443";
        context.Request.Headers["X-Forwarded-Host"] = "other.example.com:8443";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["Forwarded"] = "host=other.example.com:8443;proto=https";
        Assert.IsFalse(OwnerUiOriginGuard.IsAllowed(context), "Forwarded headers cannot establish UI ownership.");
        context.Request.Headers.Origin = "https://bakabase.example.com";
        Assert.IsFalse(OwnerUiOriginGuard.IsAllowed(context), "A different public port is a different origin.");
        context.Request.Headers.Origin = "https://bakabase.example.com:8443";
        context.Request.Headers.Append("Origin", "https://other.example.com");
        Assert.IsFalse(OwnerUiOriginGuard.IsAllowed(context));
    }

    [TestMethod]
    public async Task MicrosoftTopLevelCallbackCanReachTheBrokerWithoutPairingButAdjacentAdministrationCannot()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(OAuthCallbackGateProbeController)]);
        host.Remote.RequirePairing = true;
        using var request = Lan(host, HttpMethod.Get, Callback + "?state=synthetic-state&code=synthetic-code");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("callback reached", await response.Content.ReadAsStringAsync());

        foreach (var path in new[] {Callback + "/", Callback + "/more", "/oauth/callback/unknown", "/oauth/providers/microsoftGraph/configuration", "/rclone/accounts"})
        {
            using var denied = await host.SendAsync(Lan(host, HttpMethod.Get, path));
            Assert.AreEqual(HttpStatusCode.Unauthorized, denied.StatusCode, path);
        }
        using var post = await host.SendAsync(Lan(host, HttpMethod.Post, Callback));
        Assert.AreEqual(HttpStatusCode.Unauthorized, post.StatusCode);
    }

    [TestMethod]
    public async Task CallbackDoesNotOverrideDisabledRemoteAccessOrFailedDeviceSignatures()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(OAuthCallbackGateProbeController)]);
        host.Remote.RequirePairing = true;
        host.Remote.Mode = RemoteAccessMode.Disabled;
        using var disabled = await host.SendAsync(Lan(host, HttpMethod.Get, Callback));
        Assert.AreEqual(HttpStatusCode.Forbidden, disabled.StatusCode);
        host.Remote.Mode = RemoteAccessMode.Enabled;
        // A syntactically valid signature from a device no longer known to the
        // server remains an authentication failure. Malformed credentials are
        // deliberately anonymous in the existing remote-access protocol.
        using var signed = Lan(host, HttpMethod.Get, Callback);
        signed.Headers.TryAddWithoutValidation("Authorization",
            Bakabase.Modules.RemoteAccess.Components.Pairing.RemoteRequestSignature.BuildHeader(
                "unknown-device", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "synthetic-nonce", "synthetic-signature"));
        using var invalid = await host.SendAsync(signed);
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [TestMethod]
    public async Task CallbackStillCannotBeLoadedInAForeignFrameOnLoopback()
    {
        await using var host = await ServiceGateHost.StartAsync([typeof(OAuthCallbackGateProbeController)]);
        using var request = host.Request(HttpMethod.Get, Callback, "cross-site", "https://untrusted.example");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "iframe");
        using var response = await host.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpRequestMessage Lan(ServiceGateHost host, HttpMethod method, string path)
    {
        var request = host.Request(method, path);
        request.Headers.TryAddWithoutValidation(ServiceGateHost.RemoteIpHeader, "192.168.1.50");
        return request;
    }
}

// Tests only the production request gates, independently of the OAuth protocol
// fixtures that verify one-use state and the provider's token exchange.
[Route("~/oauth/callback/{providerId}")]
public sealed class OAuthCallbackGateProbeController : Controller
{
    [HttpGet]
    [RemoteAccessible]
    public IActionResult Callback() => Content("callback reached");
}
