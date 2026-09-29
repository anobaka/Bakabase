using System.Globalization;
using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// A device asking to browse this library has to reach whoever can allow it, in their
/// language, with a link to the place where it is allowed — the requests on the devices
/// page's Library sharing tab, not the page's first tab.
/// </summary>
/// <remarks>
/// Runs the controller against the real sharing state and the real localizer over the shipped
/// resources, so a key missing from either language fails here.
/// </remarks>
[TestClass]
public sealed class FederationPairingNotificationTests
{
    private string _root = null!;
    private CultureInfo _culture = null!;
    private GrantLeaseRegistry _leases = null!;
    private RecordingNotificationService _notifications = null!;
    private FederationPeerController _controller = null!;

    private sealed class DataDirectory(string path) : IFederationDataDirectory, INodeIdSource
    {
        public string Path => path;
        public string Ensure() => System.IO.Directory.CreateDirectory(path).FullName;
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("this-node");
    }

    [TestInitialize]
    public async Task Setup()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bakabase-federation-notifications",
            Guid.NewGuid().ToString("N"));
        _culture = CultureInfo.CurrentUICulture;
        _leases = new GrantLeaseRegistry();
        _notifications = new RecordingNotificationService();

        var directory = new DataDirectory(_root);
        var store = new FederationStateStore(directory, directory);
        var identity = new NodeIdentityProvider(store);
        var peers = new FederationPeerService(store, identity, _leases, TimeProvider.System);
        await peers.SetSharingAsync(true);

        _controller = new FederationPeerController(peers, null!, identity, null!,
            new NodePairingRateLimiter(TimeProvider.System), null!, null!, null!, null!, null!, TimeProvider.System,
            null!, store);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.9");
        _controller.ControllerContext = new ControllerContext {HttpContext = http};
    }

    [TestCleanup]
    public void Cleanup()
    {
        CultureInfo.CurrentUICulture = _culture;
        _leases.Dispose();
        try
        {
            System.IO.Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    private static IBakabaseLocalizer Localizer() =>
        new BakabaseLocalizer(new StringLocalizer<SharedResource>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions {ResourcesPath = "Resources"}), NullLoggerFactory.Instance)));

    private Task Request(string name) =>
        _controller.PairRequest(
            new NodePairRequest("reader-a", name, NodeRequestSignature.RandomToken(18),
                NodeRequestSignature.RandomToken()), _notifications, Localizer(), default);

    [TestMethod]
    public async Task A_library_request_is_announced_in_english_with_a_link_to_the_requests()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        await Request("Laptop");

        var notification = _notifications.Created.Single();
        Assert.AreEqual("Federation", notification.Source);
        Assert.AreEqual(AppNotificationSeverity.Warning, notification.Severity);
        Assert.AreEqual("Laptop asks to browse this device's library", notification.Title);
        Assert.AreEqual("From 192.168.1.9. Allow or reject it under Devices and sharing → Library sharing.",
            notification.Body);

        // The requests themselves: the page alone opens on its first tab, where nothing can be allowed.
        using var payload = JsonDocument.Parse(notification.PayloadJson!);
        Assert.AreEqual("/federation/devices?section=sharing-requests",
            payload.RootElement.GetProperty("route").GetString());
        Assert.AreEqual(FederationPeerController.SharingRequestRoute,
            payload.RootElement.GetProperty("route").GetString());
    }

    [TestMethod]
    public async Task A_library_request_is_announced_in_chinese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("zh-Hans");

        await Request("笔记本");

        var notification = _notifications.Created.Single();
        Assert.AreEqual("笔记本 请求浏览本机的资源库", notification.Title);
        Assert.AreEqual("来自 192.168.1.9。请在“设备与分享 → 资源库分享”中允许或拒绝。", notification.Body);
    }
}
