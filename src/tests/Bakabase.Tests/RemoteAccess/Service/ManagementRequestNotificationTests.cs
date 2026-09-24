using System.Globalization;
using System.Net;
using System.Text.Json;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// A device asking to manage this server has to reach whoever can approve it, once, in
/// their language, with a way to the page where it is approved.
/// </summary>
/// <remarks>
/// Runs the controller against the real device service and the real localizer over the
/// shipped resources, so a key missing from either language fails here rather than
/// showing its name in the notification centre.
/// </remarks>
[TestClass]
public class ManagementRequestNotificationTests
{
    private string _root = null!;
    private DateTime _now;
    private RemoteDeviceService _devices = null!;
    private RecordingNotificationService _notifications = null!;
    private PairingRequestRateLimiter _limiter = null!;
    private CultureInfo _culture = null!;

    private sealed class TempDirectory(string path) : IRemoteAccessDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-management-requests", Guid.NewGuid().ToString("N"));
        _now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        _devices = new RemoteDeviceService(new RemoteDeviceStore(new TempDirectory(_root)), () => _now);
        _notifications = new RecordingNotificationService();
        _limiter = new PairingRequestRateLimiter(() => _now);
        _culture = CultureInfo.CurrentUICulture;
    }

    [TestCleanup]
    public void Cleanup()
    {
        CultureInfo.CurrentUICulture = _culture;
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    private static IBakabaseLocalizer Localizer() =>
        new BakabaseLocalizer(new StringLocalizer<SharedResource>(new ResourceManagerStringLocalizerFactory(
            Options.Create(new LocalizationOptions {ResourcesPath = "Resources"}), NullLoggerFactory.Instance)));

    private async Task<RemoteAccessPairingRequestAcceptedViewModelShape> Request(string deviceName,
        string address = "192.168.1.9", RemoteDevicePlatform platform = RemoteDevicePlatform.Windows)
    {
        var controller = new RemoteAccessController(new FakeRemoteAccessService(), _devices,
            new RemoteConnectionRegistry(), _notifications, _limiter);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        controller.ControllerContext = new ControllerContext {HttpContext = http};

        var response = await controller.RequestPairing(
            new RemoteAccessPairRequestInputModel {DeviceName = deviceName, Platform = platform}, Localizer());

        return new RemoteAccessPairingRequestAcceptedViewModelShape(response.Data!.RequestId, response.Data.Failure);
    }

    private sealed record RemoteAccessPairingRequestAcceptedViewModelShape(string? RequestId, PairingFailure Failure);

    [TestMethod]
    public async Task A_management_request_is_announced_in_english_with_a_link_to_the_devices_page()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");

        await Request("Laptop");

        var notification = _notifications.Created.Single();
        Assert.AreEqual("RemoteAccess", notification.Source);
        Assert.AreEqual(AppNotificationSeverity.Warning, notification.Severity);
        Assert.AreEqual("Laptop wants to manage this device", notification.Title);
        StringAssert.StartsWith(notification.Body, "Windows, from 192.168.1.9.");
        StringAssert.Contains(notification.Body, "full control");
        // Named by a page every reader can open — this machine, a paired device, the desktop
        // app showing this server, a browser on an Unrestricted one — not by the devices
        // page's menu entry, which only this machine's own window shows. The link still
        // lands on the devices page.
        StringAssert.EndsWith(notification.Body, "Approve or reject it in Configuration → Remote access.");

        using var payload = JsonDocument.Parse(notification.PayloadJson!);
        Assert.AreEqual("/federation/devices?section=management", payload.RootElement.GetProperty("route").GetString());
    }

    [TestMethod]
    public async Task A_management_request_is_announced_in_chinese()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("zh-Hans");

        await Request("笔记本", platform: RemoteDevicePlatform.MacOS);

        var notification = _notifications.Created.Single();
        Assert.AreEqual("笔记本 请求管理此设备", notification.Title);
        StringAssert.StartsWith(notification.Body, "MacOS，来自 192.168.1.9。");
        StringAssert.EndsWith(notification.Body, "请在“配置 → 远程访问”中批准或拒绝。");
    }

    [TestMethod]
    public async Task A_device_filing_again_while_its_request_waits_is_not_announced_twice()
    {
        var first = await Request("Laptop");

        // Well past the global throttle, well inside the request's lifetime: only the
        // repeat itself keeps this one quiet.
        _now = _now.Add(PairingRequestRateLimiter.NotificationInterval).AddMinutes(1);
        var second = await Request("Laptop");

        Assert.AreNotEqual(first.RequestId, second.RequestId, "the request itself is still filed");
        Assert.AreEqual(1, _notifications.Created.Count);

        // A different device is news.
        _now = _now.Add(PairingRequestRateLimiter.NotificationInterval).AddMinutes(1);
        await Request("Phone", "192.168.1.10", RemoteDevicePlatform.Android);
        Assert.AreEqual(2, _notifications.Created.Count);
    }

    [TestMethod]
    public async Task A_device_the_throttle_held_back_is_announced_when_it_files_again()
    {
        // Laptop is announced; Desk files 30 s later and is held back by the global
        // throttle. When Desk files again, its first request is still waiting — but nobody
        // was ever told about it, so this one is news. On a headless server this
        // notification is the only prompt the person who approves ever gets.
        await Request("Laptop", "192.168.1.9");
        _now = _now.AddSeconds(30);
        await Request("Desk", "192.168.1.20");
        Assert.AreEqual(1, _notifications.Created.Count, "held back by the throttle");

        _now = _now.AddMinutes(3);
        await Request("Desk", "192.168.1.20");

        Assert.AreEqual(3, _devices.GetPendingRequests().Count, "all three still wait");
        Assert.AreEqual(2, _notifications.Created.Count);
        StringAssert.StartsWith(_notifications.Created.Last().Title, "Desk ");
    }

    [TestMethod]
    public async Task A_device_whose_notification_could_not_be_raised_is_announced_when_it_files_again()
    {
        _notifications.Fail = true;
        await Request("Laptop");
        Assert.AreEqual(0, _notifications.Created.Count);

        // The store recovers, and the device retries while its first request still waits.
        _notifications.Fail = false;
        _now = _now.AddMinutes(3);
        await Request("Laptop");

        Assert.AreEqual(2, _devices.GetPendingRequests().Count);
        StringAssert.StartsWith(_notifications.Created.Single().Title, "Laptop ");

        // Announced now, so the next retry stays quiet as any repeat does.
        _now = _now.Add(PairingRequestRateLimiter.NotificationInterval).AddMinutes(1);
        await Request("Laptop");
        Assert.AreEqual(1, _notifications.Created.Count);
    }

    [TestMethod]
    public async Task A_device_whose_earlier_request_lapsed_is_announced_again()
    {
        await Request("Laptop");

        _now = _now.Add(PendingPairingRequest.DefaultLifetime).AddMinutes(1);
        await Request("Laptop");

        Assert.AreEqual(2, _notifications.Created.Count);
    }

    [TestMethod]
    public async Task Requests_inside_the_throttle_window_raise_one_notification()
    {
        await Request("Laptop", "192.168.1.9");
        _now = _now.AddSeconds(30);
        await Request("Phone", "192.168.1.10", RemoteDevicePlatform.Android);

        Assert.AreEqual(1, _notifications.Created.Count);
        Assert.AreEqual(2, _devices.GetPendingRequests().Count, "both are still waiting on the devices page");
    }

    [TestMethod]
    public async Task A_notification_that_cannot_be_raised_does_not_fail_the_request()
    {
        _notifications.Fail = true;

        var accepted = await Request("Laptop");

        Assert.IsNotNull(accepted.RequestId);
        Assert.AreEqual(PairingFailure.None, accepted.Failure);
        Assert.AreEqual(accepted.RequestId, _devices.GetPendingRequests().Single().Id);
    }

    [TestMethod]
    public async Task A_request_over_its_address_budget_is_neither_filed_nor_announced()
    {
        for (var i = 0; i < PairingRequestRateLimiter.MaxPerAddress; i++)
        {
            await Request($"Laptop {i}");
            _now = _now.Add(PairingRequestRateLimiter.NotificationInterval).AddSeconds(1);
        }

        var announced = _notifications.Created.Count;
        var refused = await Request("One too many");

        Assert.AreEqual(PairingFailure.TooManyAttempts, refused.Failure);
        Assert.AreEqual(announced, _notifications.Created.Count);
    }
}
