using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class RemoteDeviceServiceTests
{
    private string _root = null!;
    private DateTime _now;
    private RemoteDeviceStore _store = null!;
    private RemoteDeviceService _service = null!;

    private sealed class TempDirectory(string path) : IRemoteAccessDataDirectory
    {
        public string Path => path;
        public string Ensure() => Directory.CreateDirectory(path).FullName;
    }

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "bakabase-pairing-tests", Guid.NewGuid().ToString("N"));
        _now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _store = new RemoteDeviceStore(new TempDirectory(Path.Combine(_root, "remote-access")));
        _service = new RemoteDeviceService(_store, () => _now);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    // ---- pairing code ----

    [TestMethod]
    public async Task A_code_is_six_digits_and_pairs_once()
    {
        var issue = await _service.IssuePairingCodeAsync();

        Assert.AreEqual(6, issue.Code.Length);
        Assert.IsTrue(issue.Code.All(char.IsAsciiDigit), issue.Code);

        var first = await _service.PairWithCodeAsync(issue.Code, "Phone", RemoteDevicePlatform.Android);
        Assert.IsTrue(first.Succeeded);

        // Consumed: the same code must not let a second device in.
        var second = await _service.PairWithCodeAsync(issue.Code, "Laptop", RemoteDevicePlatform.Windows);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(PairingFailure.CodeRejected, second.Failure);
        Assert.AreEqual(1, _service.GetDevices().Count);
    }

    [TestMethod]
    public async Task The_plaintext_code_is_never_persisted()
    {
        var issue = await _service.IssuePairingCodeAsync();

        var onDisk = await File.ReadAllTextAsync(
            Path.Combine(_root, "remote-access", RemoteDeviceStore.FileName));

        Assert.IsFalse(onDisk.Contains(issue.Code),
            "the pairing code itself is in devices.json; only its digest should be");
    }

    [TestMethod]
    public async Task An_expired_code_is_rejected()
    {
        var issue = await _service.IssuePairingCodeAsync(TimeSpan.FromMinutes(10));

        _now = _now.AddMinutes(11);

        var result = await _service.PairWithCodeAsync(issue.Code, "Phone", RemoteDevicePlatform.Android);
        Assert.AreEqual(PairingFailure.CodeRejected, result.Failure);
        Assert.IsNull(_service.GetPairingCodeStatus());
    }

    [TestMethod]
    public async Task Five_wrong_guesses_burn_the_code()
    {
        var issue = await _service.IssuePairingCodeAsync();
        var wrong = issue.Code == "000000" ? "111111" : "000000";

        for (var i = 0; i < PairingCodeState.MaxFailedAttempts; i++)
        {
            Assert.IsFalse((await _service.PairWithCodeAsync(wrong, "Attacker", RemoteDevicePlatform.Unknown))
                .Succeeded);
        }

        // Even the right code is dead now — otherwise the limit would only slow a
        // guesser down rather than stop them.
        Assert.IsFalse((await _service.PairWithCodeAsync(issue.Code, "Phone", RemoteDevicePlatform.Android))
            .Succeeded);
        Assert.IsNull(_service.GetPairingCodeStatus());
    }

    [TestMethod]
    public async Task Issuing_again_invalidates_the_previous_code()
    {
        var first = await _service.IssuePairingCodeAsync();
        var second = await _service.IssuePairingCodeAsync();

        Assert.IsFalse((await _service.PairWithCodeAsync(first.Code, "A", RemoteDevicePlatform.Unknown)).Succeeded);
        Assert.IsTrue((await _service.PairWithCodeAsync(second.Code, "B", RemoteDevicePlatform.Unknown)).Succeeded);
    }

    [TestMethod]
    public async Task Status_reports_remaining_attempts_without_the_code()
    {
        var issue = await _service.IssuePairingCodeAsync();
        await _service.PairWithCodeAsync(issue.Code == "000000" ? "111111" : "000000", "x",
            RemoteDevicePlatform.Unknown);

        var status = _service.GetPairingCodeStatus();
        Assert.IsNotNull(status);
        Assert.AreEqual(PairingCodeState.MaxFailedAttempts - 1, status!.RemainingAttempts);
    }

    [TestMethod]
    public async Task No_code_means_no_pairing()
    {
        Assert.IsNull(_service.GetPairingCodeStatus());
        Assert.IsFalse((await _service.PairWithCodeAsync("123456", "x", RemoteDevicePlatform.Unknown)).Succeeded);
        Assert.IsFalse((await _service.PairWithCodeAsync(null, "x", RemoteDevicePlatform.Unknown)).Succeeded);
        Assert.IsFalse((await _service.PairWithCodeAsync("  ", "x", RemoteDevicePlatform.Unknown)).Succeeded);
    }

    // ---- approval flow ----

    [TestMethod]
    public async Task An_approved_request_hands_credentials_over_exactly_once()
    {
        var approver = (await PairFirstDevice()).DeviceId;

        var request = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, "192.168.1.9");

        // Nothing to collect before somebody approves.
        Assert.AreEqual(PairingFailure.NotYetApproved,
            (await _service.ClaimApprovedAsync(request.Id)).Failure);

        var approved = await _service.ApproveRequestAsync(request.Id, approver);
        Assert.IsNotNull(approved);
        // Not listed until it collects its key.
        Assert.IsNull(_service.Find(approved));

        var claim = await _service.ClaimApprovedAsync(request.Id);
        Assert.IsTrue(claim.Succeeded);
        Assert.AreEqual(2, _service.GetDevices().Count);
        // The id the approval named is the one the device is listed under.
        Assert.AreEqual(approved, claim.Credentials!.DeviceId);
        Assert.AreEqual("Tablet", _service.Find(approved)!.Name);
        Assert.AreEqual(approver, _service.Find(claim.Credentials!.DeviceId)!.ApprovedByDeviceId);

        // Replaying the claim must not mint a second device.
        Assert.AreEqual(PairingFailure.RequestRejected,
            (await _service.ClaimApprovedAsync(request.Id)).Failure);
        Assert.AreEqual(2, _service.GetDevices().Count);
    }

    [TestMethod]
    public async Task An_unapproved_request_creates_no_device()
    {
        await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);

        Assert.AreEqual(0, _service.GetDevices().Count);
        Assert.AreEqual(1, _service.GetPendingRequests().Count);
    }

    [TestMethod]
    public async Task An_approval_nobody_collects_leaves_no_device_behind()
    {
        // Otherwise the device list would show an entry that can never authenticate,
        // because its key was never delivered.
        var approver = (await PairFirstDevice()).DeviceId;
        var request = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);
        await _service.ApproveRequestAsync(request.Id, approver);

        Assert.AreEqual(1, _service.GetDevices().Count);

        _now = _now.AddMinutes(11);

        Assert.AreEqual(PairingFailure.RequestRejected, (await _service.ClaimApprovedAsync(request.Id)).Failure);
        Assert.AreEqual(1, _service.GetDevices().Count);
    }

    [TestMethod]
    public async Task An_expired_request_cannot_be_approved()
    {
        var approver = (await PairFirstDevice()).DeviceId;
        var request = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);

        _now = _now.AddMinutes(11);

        Assert.IsNull(await _service.ApproveRequestAsync(request.Id, approver));
    }

    [TestMethod]
    public async Task Approving_twice_does_not_reissue()
    {
        var approver = (await PairFirstDevice()).DeviceId;
        var request = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);

        Assert.IsNotNull(await _service.ApproveRequestAsync(request.Id, approver));
        Assert.IsNull(await _service.ApproveRequestAsync(request.Id, approver));
    }

    [TestMethod]
    public async Task A_rejected_request_cannot_be_claimed()
    {
        var approver = (await PairFirstDevice()).DeviceId;
        var request = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);
        await _service.ApproveRequestAsync(request.Id, approver);

        Assert.IsTrue(await _service.RejectRequestAsync(request.Id));

        Assert.AreEqual(PairingFailure.RequestRejected, (await _service.ClaimApprovedAsync(request.Id)).Failure);
    }

    [TestMethod]
    public async Task An_unknown_request_id_is_rejected()
    {
        Assert.AreEqual(PairingFailure.RequestRejected, (await _service.ClaimApprovedAsync("nope")).Failure);
        Assert.IsNull(await _service.ApproveRequestAsync("nope", "whoever"));
    }

    [TestMethod]
    public async Task Approved_requests_drop_out_of_the_pending_list()
    {
        // The list drives an approval prompt; showing something already approved would
        // ask the user to approve it twice.
        var approver = (await PairFirstDevice()).DeviceId;
        var request = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);

        Assert.AreEqual(1, _service.GetPendingRequests().Count);
        await _service.ApproveRequestAsync(request.Id, approver);
        Assert.AreEqual(0, _service.GetPendingRequests().Count);
    }

    [TestMethod]
    public async Task The_pending_queue_is_bounded()
    {
        // Anyone who can reach the server can file a request without credentials, so an
        // unbounded queue is a file that grows on its own.
        for (var i = 0; i < PendingPairingRequest.MaxPending + 10; i++)
        {
            await _service.RequestPairingAsync($"Device {i}", RemoteDevicePlatform.Android, null);
        }

        Assert.AreEqual(PendingPairingRequest.MaxPending, _service.GetPendingRequests().Count);

        // Trimming takes the oldest, so the request a real user just filed survives a
        // flood rather than being the one dropped.
        Assert.IsTrue(_service.GetPendingRequests()
            .Any(r => r.DeviceName == $"Device {PendingPairingRequest.MaxPending + 9}"));
    }

    [TestMethod]
    public async Task A_flood_cannot_strand_a_device_waiting_for_its_key()
    {
        // An approved request holds the only copy of a key its device has not collected
        // yet. Dropping it under pressure would leave that device unable to finish.
        var approver = (await PairFirstDevice()).DeviceId;
        var mine = await _service.RequestPairingAsync("Tablet", RemoteDevicePlatform.Android, null);
        Assert.IsNotNull(await _service.ApproveRequestAsync(mine.Id, approver));

        for (var i = 0; i < PendingPairingRequest.MaxPending * 2; i++)
        {
            await _service.RequestPairingAsync($"Noise {i}", RemoteDevicePlatform.Android, null);
        }

        Assert.IsTrue((await _service.ClaimApprovedAsync(mine.Id)).Succeeded);
    }

    // ---- device list ----

    [TestMethod]
    public async Task Revoking_removes_the_device()
    {
        var credentials = await PairFirstDevice();

        Assert.IsTrue(_service.HasAnyDevice);
        Assert.IsTrue(await _service.RevokeAsync(credentials.DeviceId));

        Assert.IsFalse(_service.HasAnyDevice);
        Assert.IsNull(_service.Find(credentials.DeviceId));
        Assert.IsFalse(await _service.RevokeAsync(credentials.DeviceId));
    }

    [TestMethod]
    public async Task Renaming_bounds_and_trims_the_name()
    {
        var credentials = await PairFirstDevice();

        Assert.IsTrue(await _service.RenameAsync(credentials.DeviceId, new string('x', 200)));
        Assert.AreEqual(64, _service.Find(credentials.DeviceId)!.Name.Length);

        await _service.RenameAsync(credentials.DeviceId, "   ");
        Assert.AreEqual("Unnamed device", _service.Find(credentials.DeviceId)!.Name);
    }

    [TestMethod]
    public async Task Touch_writes_at_most_once_per_interval()
    {
        var credentials = await PairFirstDevice();
        var path = Path.Combine(_root, "remote-access", RemoteDeviceStore.FileName);

        await _service.TouchAsync(credentials.DeviceId);
        Assert.AreEqual(_now, _service.Find(credentials.DeviceId)!.LastSeenAt);

        var writtenAt = File.GetLastWriteTimeUtc(path);

        _now = _now.AddMinutes(1);
        await _service.TouchAsync(credentials.DeviceId);

        // Still the first timestamp: the second touch fell inside the interval and must
        // not have gone to disk, because this runs on every signed request.
        Assert.AreEqual(writtenAt, File.GetLastWriteTimeUtc(path));

        _now = _now.AddMinutes(15);
        await _service.TouchAsync(credentials.DeviceId);
        Assert.AreEqual(_now, _service.Find(credentials.DeviceId)!.LastSeenAt);
    }

    [TestMethod]
    public async Task Touching_an_unknown_device_does_nothing()
    {
        await _service.TouchAsync("nobody");
        Assert.IsFalse(_store.Exists);
    }

    [TestMethod]
    public async Task Keys_and_ids_differ_between_devices()
    {
        var a = await PairFirstDevice();
        var issue = await _service.IssuePairingCodeAsync();
        var b = (await _service.PairWithCodeAsync(issue.Code, "B", RemoteDevicePlatform.Linux)).Credentials!;

        Assert.AreNotEqual(a.DeviceId, b.DeviceId);
        Assert.AreNotEqual(a.Key, b.Key);
        Assert.AreEqual(32, RemoteRequestSignature.FromBase64Url(b.Key).Length);
    }

    [TestMethod]
    public async Task Nothing_touches_the_disk_until_something_is_paired()
    {
        // The all-in-one default. Reading state must not create the directory.
        Assert.IsFalse(_service.HasAnyDevice);
        Assert.AreEqual(0, _service.GetDevices().Count);
        Assert.AreEqual(0, _service.GetPendingRequests().Count);
        Assert.IsNull(_service.GetPairingCodeStatus());
        await _service.TouchAsync("nobody");

        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "remote-access")));
    }

    private async Task<PairingCredentials> PairFirstDevice()
    {
        var issue = await _service.IssuePairingCodeAsync();
        var result = await _service.PairWithCodeAsync(issue.Code, "First", RemoteDevicePlatform.Windows);
        Assert.IsTrue(result.Succeeded);
        return result.Credentials!;
    }
}
