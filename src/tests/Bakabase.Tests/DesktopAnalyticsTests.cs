using System;
using System.IO;
using System.Net;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.App;
using Bakabase.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class DesktopAnalyticsTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bakabase-analytics-" + Guid.NewGuid());

    [TestCleanup]
    public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [TestMethod]
    public void ConsentIsLiveAndIdentitySurvivesRestartAndReenable()
    {
        var enabled = false;
        var provider = new DesktopAnalyticsContextProvider(_directory, () => enabled, "1.2.3");
        Assert.IsNull(provider.Read().ClientId);
        Assert.IsFalse(Directory.Exists(_directory));
        enabled = true;
        var first = provider.Read();
        Assert.IsTrue(Guid.TryParse(first.ClientId, out var id));
        Assert.AreNotEqual(Guid.Empty, id);
        Assert.AreEqual("desktop_webview", first.ClientSurface);
        enabled = false;
        Assert.IsNull(provider.Read().ClientId);
        Assert.IsFalse(provider.Read().EnableAnonymousDataTracking);
        enabled = true;
        Assert.AreEqual(first.ClientId, provider.Read().ClientId);
        Assert.AreEqual(first.ClientId, new DesktopAnalyticsContextProvider(_directory, () => true, "2.0.0").Read().ClientId);
        if (!OperatingSystem.IsWindows())
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(_directory, "analytics-client-id")));
    }

    [TestMethod]
    [DataRow("corrupt")]
    [DataRow("00000000-0000-0000-0000-000000000000")]
    public void InvalidPersistedIdentityDisablesCollectionWithoutReplacingIt(string contents)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "analytics-client-id");
        File.WriteAllText(path, contents);
        var context = new DesktopAnalyticsContextProvider(_directory, () => true, "1").Read();
        Assert.IsFalse(context.EnableAnonymousDataTracking);
        Assert.IsNull(context.ClientId);
        Assert.AreEqual(contents, File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow(false, "BakabaseDesktop/1.0", "localhost:34567", "", "", false)]
    [DataRow(true, "Mozilla/5.0", "localhost:34567", "", "", false)]
    [DataRow(true, "BakabaseDesktop/1.0", "evil.example:34567", "", "", false)]
    [DataRow(true, "BakabaseDesktop/1.0", "127.0.0.1:34567", "https://evil.example", "same-origin", false)]
    [DataRow(true, "BakabaseDesktop/1.0", "localhost:34567", "http://localhost:3000", "same-origin", false)]
    [DataRow(true, "BakabaseDesktop/1.0", "localhost:34567", "", "cross-site", false)]
    [DataRow(true, "Mozilla/5.0 BakabaseDesktop/1.0", "localhost:34567", "http://localhost:34567", "same-origin", true)]
    [DataRow(true, "BakabaseDesktop/1.0", "[::1]:34567", "", "none", true)]
    public void ContextRequiresNativeLoopbackAndSameOrigin(bool loopback, string agent, string host,
        string origin, string site, bool allowed) =>
        Assert.AreEqual(allowed, DesktopAnalyticsRequest.IsAllowed(loopback, agent, "http", host, origin, site));

    [TestMethod]
    public void HeadlessAndOrdinaryBrowserCannotReadDesktopIdentity()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 34567);
        context.Request.Headers.UserAgent = "BakabaseDesktop/1.0";
        var controller = new DesktopAnalyticsController { ControllerContext = new ControllerContext { HttpContext = context } };
        Assert.IsInstanceOfType<NotFoundResult>(controller.Get().Result);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        using var desktop = new ServiceCollection().AddSingleton<IDesktopAnalyticsContextProvider>(
            new DesktopAnalyticsContextProvider(_directory, () => true, "1")).BuildServiceProvider();
        context.RequestServices = desktop;
        context.Request.Headers.UserAgent = "Mozilla/5.0";
        Assert.IsInstanceOfType<NotFoundResult>(controller.Get().Result);
        Assert.IsFalse(Directory.Exists(_directory));
        context.Request.Headers.UserAgent = "BakabaseDesktop/1.0";
        Assert.IsNotNull(controller.Get().Value?.Data?.ClientId);
    }
}
