using System.Net;
using Bakabase.Service.Components.ServerData;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Primitives;

namespace Bakabase.Tests;

[TestClass]
public class ServerSetupLocalAccessTests
{
    [TestMethod]
    [DataRow("127.0.0.1", "localhost:34567", null, null)]
    [DataRow("127.0.0.1", "localhost:34567", "http://localhost:34567", "same-origin")]
    [DataRow("::1", "[::1]:34567", "http://[::1]:34567", "same-origin")]
    [DataRow("::ffff:127.0.0.1", "127.0.0.1:34567", null, "none")]
    public void DirectLocalBrowsersAndNativeClientsCanClaim(string remote, string host, string? origin, string? site)
    {
        var context = Context(remote, host, origin, site);
        Assert.IsTrue(ServerSetupLocalAccess.CanClaim(context, false));
    }

    [TestMethod]
    public void DockerAlwaysRequiresItsLoggedCapability()
    {
        var context = Context();
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(context, true));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("192.168.1.10")]
    [DataRow("2001:db8::1")]
    public void MissingOrRemotePeersCannotClaimEvenWithSpoofedForwardingHeaders(string? remote)
    {
        var context = Context(remote);
        context.Request.Headers["Forwarded"] = "for=127.0.0.1;host=localhost:34567;proto=http";
        context.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        context.Request.Headers["X-Forwarded-Host"] = "localhost:34567";
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(context, false));
    }

    [TestMethod]
    [DataRow("attacker.example:34567", "http://attacker.example:34567")]
    [DataRow("192.168.1.10:34567", "http://192.168.1.10:34567")]
    [DataRow("localhost.attacker.example:34567", "http://localhost.attacker.example:34567")]
    [DataRow("localhost:34568", "http://localhost:34568")]
    [DataRow("localhost", "http://localhost")]
    public void HostMustBeLoopbackAndNameTheActualListeningPort(string host, string origin)
    {
        var context = Context(host: host, origin: origin);
        context.Request.Headers["X-Forwarded-Host"] = "localhost:34567";
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(context, false));
    }

    [TestMethod]
    [DataRow("http://127.0.0.1:34567")]
    [DataRow("http://localhost:34568")]
    [DataRow("https://localhost:34567")]
    [DataRow("http://attacker.example:34567")]
    [DataRow("http://localhost:34567/path")]
    [DataRow("http://localhost:34567/?query=1")]
    [DataRow("http://localhost:34567/#fragment")]
    [DataRow("http://user@localhost:34567")]
    [DataRow("null")]
    [DataRow("")]
    public void OriginWhenPresentMustBeExactlyThisServer(string origin)
    {
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(Context(origin: origin), false));
    }

    [TestMethod]
    [DataRow("same-site")]
    [DataRow("cross-site")]
    [DataRow("unknown")]
    [DataRow("")]
    public void FetchMetadataMustNotDescribeAnotherSite(string site)
    {
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(Context(site: site), false));
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(Context(origin: null, site: site), false));
    }

    [TestMethod]
    public void MultipleOriginsAndMissingBoundPortsAreRejected()
    {
        var context = Context();
        context.Request.Headers.Origin = new StringValues(["http://localhost:34567", "http://localhost:34567"]);
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(context, false));
        context = Context();
        context.Connection.LocalPort = 0;
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(context, false));
    }

    [TestMethod]
    public void DefaultHttpsPortIsMatchedWithoutTrustingForwardedScheme()
    {
        var context = Context(host: "localhost", origin: "https://localhost");
        context.Connection.LocalPort = 443;
        context.Request.Scheme = "https";
        Assert.IsTrue(ServerSetupLocalAccess.CanClaim(context, false));
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        Assert.IsFalse(ServerSetupLocalAccess.CanClaim(context, false));
    }

    private static DefaultHttpContext Context(string? remote = "127.0.0.1", string host = "localhost:34567",
        string? origin = "http://localhost:34567", string? site = "same-origin")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote == null ? null : IPAddress.Parse(remote);
        context.Connection.LocalPort = 34567;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(host);
        if (origin != null) context.Request.Headers.Origin = origin;
        if (site != null) context.Request.Headers["Sec-Fetch-Site"] = site;
        return context;
    }
}
