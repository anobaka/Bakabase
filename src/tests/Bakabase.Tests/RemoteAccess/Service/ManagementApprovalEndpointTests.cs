using System.Net;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Controllers;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Service;

/// <summary>
/// Approving a request to manage this server answers which device it lets in — the id that
/// device is listed under once it has collected its key — so the page that approved it can
/// move to it. Never the key.
/// </summary>
[TestClass]
public class ManagementApprovalEndpointTests
{
    private string _root = null!;
    private DateTime _now;
    private RemoteDeviceService _devices = null!;
    private RemoteAccessController _controller = null!;

    private sealed class TempDirectory(string path) : IRemoteAccessDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-management-approval", Guid.NewGuid().ToString("N"));
        _now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        _devices = new RemoteDeviceService(new RemoteDeviceStore(new TempDirectory(_root)), () => _now);
        _controller = new RemoteAccessController(new FakeRemoteAccessService(), _devices,
            new RemoteConnectionRegistry(), new RecordingNotificationService(), new PairingRequestRateLimiter(() => _now))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext {Connection = {RemoteIpAddress = IPAddress.Loopback}}
            }
        };
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (Exception e) when (e is IOException or DirectoryNotFoundException)
        {
        }
    }

    [TestMethod]
    public async Task Approving_answers_the_id_the_device_is_listed_under_once_it_collects_its_key()
    {
        var request = await _devices.RequestPairingAsync("Laptop", RemoteDevicePlatform.Windows, "192.168.1.9");

        var approved = await _controller.ApprovePairingRequest(request.Id);

        Assert.AreEqual(0, approved.Code);
        var deviceId = approved.Data?.DeviceId;
        Assert.IsFalse(string.IsNullOrEmpty(deviceId));
        // Not a device yet: it becomes one when it collects its key.
        Assert.IsNull(_devices.Find(deviceId));

        var claim = await _devices.ClaimApprovedAsync(request.Id);

        Assert.IsTrue(claim.Succeeded);
        Assert.AreEqual(deviceId, claim.Credentials!.DeviceId);
        Assert.AreEqual("Laptop", _devices.Find(deviceId)!.Name);
        // The key went to the device alone.
        Assert.AreNotEqual(deviceId, claim.Credentials.Key);
        StringAssert.DoesNotMatch(System.Text.Json.JsonSerializer.Serialize(approved),
            new System.Text.RegularExpressions.Regex(System.Text.RegularExpressions.Regex.Escape(claim.Credentials.Key)));
    }

    [TestMethod]
    public async Task Approving_what_is_not_there_answers_not_found_and_no_device()
    {
        var request = await _devices.RequestPairingAsync("Laptop", RemoteDevicePlatform.Windows, null);

        Assert.IsNotNull((await _controller.ApprovePairingRequest(request.Id)).Data);

        foreach (var id in new[] {request.Id, "nope"})
        {
            var again = await _controller.ApprovePairingRequest(id);

            Assert.AreEqual((int) ResponseCode.NotFound, again.Code, id);
            Assert.IsNull(again.Data, id);
        }

        _now = _now.Add(PendingPairingRequest.DefaultLifetime).AddMinutes(1);
        var late = await _devices.RequestPairingAsync("Phone", RemoteDevicePlatform.Android, null);
        _now = _now.Add(PendingPairingRequest.DefaultLifetime).AddMinutes(1);

        Assert.AreEqual((int) ResponseCode.NotFound, (await _controller.ApprovePairingRequest(late.Id)).Code);
    }
}
