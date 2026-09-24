using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Getting management rights on another server, knowing how the managed ones are, and
/// never mistaking this device for one of them.
/// </summary>
[TestClass]
public class RemoteConsolePairingTests
{
    private ConsoleHarness _console = null!;
    private readonly List<FakeServer> _servers = [];

    [TestInitialize]
    public async Task Setup() => _console = await ConsoleHarness.StartAsync();

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();

        foreach (var server in _servers)
        {
            await server.DisposeAsync();
        }
    }

    private async Task<FakeServer> Server(string id, string name = "Desk")
    {
        var server = await FakeServer.StartAsync(id, name, 46900);
        _servers.Add(server);
        return server;
    }

    [TestMethod]
    public async Task Pairing_with_a_code_makes_the_server_managed()
    {
        var desk = await Server("server-desk");
        desk.PairingCode = "123456";

        var probe = await _console.Manager.ProbeAsync(desk.BaseAddress);
        Assert.AreEqual(ManagedServerOutcome.Ok, probe.Outcome);
        Assert.AreEqual("server-desk", probe.ServerId);
        Assert.IsFalse(probe.AlreadyManaged);

        var wrong = await _console.Manager.PairAsync(desk.BaseAddress, "000000");
        Assert.AreEqual(ManagedServerOutcome.CodeRejected, wrong.Outcome);
        Assert.IsNull(_console.Store.Find("server-desk"));

        var paired = await _console.Manager.PairAsync(desk.BaseAddress, "123456");
        Assert.AreEqual(ManagedServerOutcome.Ok, paired.Outcome);
        Assert.AreEqual("server-desk", paired.ServerId);

        var entry = _console.Store.Find("server-desk")!;
        Assert.AreEqual(desk.BaseAddress, entry.BaseAddress);
        Assert.IsTrue(desk.KnownDevices.ContainsKey(entry.DeviceId));
        Assert.IsTrue((await _console.Manager.ProbeAsync(desk.BaseAddress)).AlreadyManaged);

        var listed = (await _console.Manager.GetAsync(true)).Servers.Single();
        Assert.AreEqual(ManagedServerState.Online, listed.State);
    }

    [TestMethod]
    public async Task A_filed_request_is_collected_in_the_background_once_approved()
    {
        var desk = await Server("server-desk");

        var filed = await _console.Manager.PairAsync(desk.BaseAddress, null);

        Assert.AreEqual(ManagedServerOutcome.AwaitingApproval, filed.Outcome);
        Assert.AreEqual("request-1", filed.RequestId);
        var waiting = (await _console.Manager.GetAsync(false)).Requests.Single();
        Assert.AreEqual(ManagedServerOutcome.AwaitingApproval, waiting.Outcome);
        Assert.IsTrue(waiting.Active);
        // The install it was filed with, as the address answered: what it joins the list under.
        Assert.AreEqual("server-desk", waiting.ServerId);

        desk.Approved = true;

        await WaitUntilAsync(() => Task.FromResult(_console.Store.Find("server-desk") != null), "the approval");

        var entry = _console.Store.Find("server-desk");
        Assert.IsNotNull(entry, string.Join("\n", _console.Logs));
        Assert.IsTrue(desk.KnownDevices.ContainsKey(entry.DeviceId));

        // Approved is over: the server is in the list, and nothing is left being waited on.
        await WaitUntilAsync(async () => (await _console.Manager.GetAsync(false)).Requests.Count == 0,
            "the approved request to leave the listing");
        Assert.AreEqual("server-desk", (await _console.Manager.GetAsync(false)).Servers.Single().ServerId);
    }

    [TestMethod]
    public async Task A_cancelled_request_stops_being_collected()
    {
        var desk = await Server("server-desk");
        var filed = await _console.Manager.PairAsync(desk.BaseAddress, null);

        Assert.IsTrue((await _console.Manager.GetAsync(false)).Requests.Single().Active);

        Assert.IsTrue(await _console.Manager.CancelRequestAsync(filed.RequestId!));
        Assert.IsFalse(await _console.Manager.CancelRequestAsync(filed.RequestId!));

        desk.Approved = true;
        await Task.Delay(300);

        Assert.IsNull(_console.Store.Find("server-desk"));
        Assert.AreEqual(0, (await _console.Manager.GetAsync(false)).Requests.Count);
    }

    // ---- whether a filed request is still being waited on ----

    private Task WaitUntilAsync(Func<Task<bool>> condition, string what) => WaitUntilAsync(condition, () => what);

    private async Task WaitUntilAsync(Func<Task<bool>> condition, Func<string> what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                string logs;
                lock (_console.Logs)
                {
                    logs = string.Join("\n", _console.Logs);
                }

                Assert.Fail($"Timed out waiting for {what()}.\n{logs}");
            }

            await Task.Delay(20);
        }
    }

    /// <summary>The request as the listing shows it, once it satisfies <paramref name="until"/>.</summary>
    private async Task<ManagedServerPendingRequestView> RequestOnceAsync(string requestId,
        Func<ManagedServerPendingRequestView, bool> until, string what)
    {
        ManagedServerPendingRequestView? seen = null;

        await WaitUntilAsync(async () =>
        {
            seen = (await _console.Manager.GetAsync(false)).Requests.SingleOrDefault(r => r.RequestId == requestId);
            return seen != null && until(seen);
        }, () => $"{what} (last seen: {seen?.ToString() ?? "not listed"})");

        return seen!;
    }

    private int Claims(FakeServer server) =>
        server.Requests.Count(r => r.Path == "/remote-access/pair/claim");

    [TestMethod]
    public async Task A_claim_that_does_not_get_through_leaves_the_request_active()
    {
        var desk = await Server("server-desk");
        var id = (await _console.Manager.PairAsync(desk.BaseAddress, null)).RequestId!;

        // A proxy in front of the server answers 502 for a moment. That is what this attempt
        // ran into, and it is reported as such — but nothing ended.
        desk.ClaimStatus = System.Net.HttpStatusCode.BadGateway;
        var unreachable = await RequestOnceAsync(id, r => r.Outcome == ManagedServerOutcome.Unreachable,
            "a claim that did not get through");
        Assert.IsTrue(unreachable.Active, "one dropped claim was reported as the end of the wait");

        // The server throttles this device for a while: the same.
        desk.ClaimStatus = System.Net.HttpStatusCode.TooManyRequests;
        var throttled = await RequestOnceAsync(id, r => r.Outcome == ManagedServerOutcome.TooManyAttempts,
            "a throttled claim");
        Assert.IsTrue(throttled.Active);

        // And the wait really was still on: approved now, it is collected.
        desk.ClaimStatus = null;
        desk.Approved = true;

        await WaitUntilAsync(() => Task.FromResult(_console.Store.Find("server-desk") != null), "the approval");
        await WaitUntilAsync(async () => !(await _console.Manager.GetAsync(false)).Requests.Any(r => r.Active),
            "no request left active");
    }

    [TestMethod]
    public async Task A_rejected_request_is_over_and_says_so()
    {
        var desk = await Server("server-desk");
        var id = (await _console.Manager.PairAsync(desk.BaseAddress, null)).RequestId!;

        desk.Rejected = true;

        var ended = await RequestOnceAsync(id, r => !r.Active, "the rejection");
        Assert.AreEqual(ManagedServerOutcome.RequestRejected, ended.Outcome);

        // Kept in the listing so the page can say what happened, but no longer asked about.
        var claims = Claims(desk);
        await Task.Delay(300);
        Assert.AreEqual(claims, Claims(desk), "an ended request is still being claimed");
        Assert.IsFalse((await _console.Manager.GetAsync(false)).Requests.Single().Active);
        Assert.IsNull(_console.Store.Find("server-desk"));
    }

    [TestMethod]
    public async Task An_expired_request_is_over_without_the_server_saying_so()
    {
        var desk = await Server("server-desk");
        desk.RequestLifetime = TimeSpan.FromMilliseconds(400);

        var id = (await _console.Manager.PairAsync(desk.BaseAddress, null)).RequestId!;
        Assert.IsTrue((await _console.Manager.GetAsync(false)).Requests.Single().Active);

        // Nobody answers it at the server, which keeps saying "not yet"; the expiry alone ends it.
        var ended = await RequestOnceAsync(id, r => !r.Active, "the request to lapse");
        Assert.AreEqual(ManagedServerOutcome.RequestRejected, ended.Outcome);

        var claims = Claims(desk);
        await Task.Delay(300);
        Assert.AreEqual(claims, Claims(desk), "a lapsed request is still being claimed");
    }

    [TestMethod]
    public async Task Requests_are_not_active_once_the_app_stops_waiting_on_them()
    {
        var desk = await Server("server-desk");
        await _console.Manager.PairAsync(desk.BaseAddress, null);

        // The app closing cancels every wait. Nothing is collecting the request any more, so
        // it must not read as one still being waited on.
        await _console.Manager.StopAsync(default);

        var request = (await _console.Manager.GetAsync(false)).Requests.Single();
        Assert.IsFalse(request.Active);
    }

    [TestMethod]
    public async Task This_devices_own_server_is_refused_by_identity()
    {
        // Reached through an address that is not one of this app's ports — a LAN address of
        // this machine — so only the identity gives it away.
        var self = await Server(ConsoleHarness.OwnServerId, "Me");
        self.PairingCode = "123456";

        Assert.AreEqual(ManagedServerOutcome.ThisDevice, (await _console.Manager.ProbeAsync(self.BaseAddress)).Outcome);
        Assert.AreEqual(ManagedServerOutcome.ThisDevice,
            (await _console.Manager.PairAsync(self.BaseAddress, "123456")).Outcome);

        Assert.AreEqual(0, _console.Store.Read().Servers.Count);
        Assert.IsFalse(self.Requests.Any(r => r.Path.StartsWith("/remote-access/pair", StringComparison.Ordinal)),
            "pairing was attempted with this device itself");
    }

    [TestMethod]
    public async Task This_apps_own_ports_are_refused_before_anything_is_sent()
    {
        var desk = await Server("server-desk");
        await _console.AddManagedAsync(desk);
        var relayPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);

        // A relay port reserved by a server whose relay is not running this session.
        var idle = await Server("server-idle", "Idle");
        await _console.AddManagedAsync(idle);
        await _console.Store.MutateAsync(data => data.Servers.Single(s => s.ServerId == idle.ServerId).RelayPort =
            relayPort + 1);

        // The app's own server port, listened on here so that a connection would show.
        using var service = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, _console.ServicePort);
        service.Start();

        var reachedDesk = desk.Requests.Count;

        string[] addresses =
        [
            $"127.0.0.1:{relayPort}",
            $"http://localhost:{relayPort}/",
            $"http://[::1]:{relayPort}",
            $"127.0.0.1:{relayPort + 1}",
            $"localhost:{_console.ServicePort}",
            $"http://127.0.0.1:{_console.ServicePort}"
        ];

        var watch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var address in addresses)
        {
            Assert.AreEqual(ManagedServerOutcome.ThisDevice, (await _console.Manager.ProbeAsync(address)).Outcome,
                address);
            Assert.AreEqual(ManagedServerOutcome.ThisDevice, (await _console.Manager.PairAsync(address, "123456")).Outcome,
                address);
        }

        // Answered from the address alone: no connection was even attempted, so there is no
        // timeout to wait out either.
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
        Assert.IsFalse(service.Pending(), "a connection reached this app's own server port");
        Assert.AreEqual(reachedDesk, desk.Requests.Count, "a request went through the relay to the desk");

        // A relay forwards server-info upstream, so without the port check the handshake
        // would have succeeded — as the desk — and paired this app with itself through it.
        Assert.AreEqual(2, _console.Store.Read().Servers.Count);
    }

    [TestMethod]
    public async Task Probing_tells_online_revoked_and_offline_apart_and_reports_unrestricted_servers()
    {
        var online = await Server("server-online", "Online");
        online.Mode = RemoteAccessMode.Unrestricted;
        var revoked = await Server("server-revoked", "Revoked");
        var offline = await Server("server-offline", "Offline");
        var reset = await Server("server-reset", "Reset");

        await _console.AddManagedAsync(online);
        var (revokedDevice, _) = await _console.AddManagedAsync(revoked);
        await _console.AddManagedAsync(offline);
        await _console.AddManagedAsync(reset);

        revoked.KnownDevices.TryRemove(revokedDevice, out _);
        await offline.DisposeAsync();
        _servers.Remove(offline);

        // The address now answers as another install: its data was reset.
        await _console.Store.MutateAsync(data =>
            data.Servers.Single(s => s.ServerId == "server-reset").ServerId = "server-reset-before");

        var before = (await _console.Manager.GetAsync(false)).Servers;
        Assert.IsTrue(before.All(s => s.State == ManagedServerState.Unknown), "listing without probing asked anyway");

        var servers = (await _console.Manager.GetAsync(true)).Servers.ToDictionary(s => s.ServerId);

        Assert.AreEqual(ManagedServerState.Online, servers["server-online"].State);
        Assert.AreEqual(RemoteAccessMode.Unrestricted, servers["server-online"].Mode);
        Assert.AreEqual("9.9.9", servers["server-online"].AppVersion);
        Assert.AreEqual(ManagedServerState.Revoked, servers["server-revoked"].State);
        Assert.AreEqual(ManagedServerState.Offline, servers["server-offline"].State);

        // Another install answers there — its data was reset, or the address went to another
        // server. Not "revoked": that would send the user to pair again at the address, with
        // whoever now answers there. Who answered is said, and nothing it said is taken as
        // the server's own.
        var answered = servers["server-reset-before"];
        Assert.AreEqual(ManagedServerState.WrongServer, answered.State);
        Assert.AreEqual(new ManagedServerAnswerView("server-reset", "Reset", false), answered.AnsweredBy);
        Assert.IsNull(answered.Mode);
        Assert.IsNull(answered.AppVersion);
        Assert.IsNull(servers["server-online"].AnsweredBy);

        // Warned about, never changed: nothing but reads reached the unrestricted server.
        Assert.IsTrue(online.Requests.All(r => r.Method == "GET"));
    }

    [TestMethod]
    public async Task Path_mappings_are_replaced_as_a_whole()
    {
        var desk = await Server("server-desk");
        await _console.AddManagedAsync(desk);

        Assert.IsTrue(await _console.Manager.SetPathMappingsAsync(desk.ServerId,
            [new ManagedServerPathMapping("/data", "/mnt/data"), new ManagedServerPathMapping(" ", "/ignored")]));
        Assert.IsFalse(await _console.Manager.SetPathMappingsAsync("nobody", []));

        var view = (await _console.Manager.GetAsync(false)).Servers.Single();
        Assert.AreEqual(new ManagedServerPathMapping("/data", "/mnt/data"), view.PathMappings.Single());
    }

    [TestMethod]
    public async Task The_switcher_lists_this_device_first()
    {
        var desk = await Server("server-desk", "Desk");
        await _console.AddManagedAsync(desk);

        var targets = _console.Manager.ListTargets();

        Assert.AreEqual("local", targets[0].Id);
        Assert.IsTrue(targets[0].IsLocal);
        Assert.AreEqual(Environment.MachineName, targets[0].Name);
        Assert.AreEqual(("server-desk", "Desk", false), (targets[1].Id, targets[1].Name, targets[1].IsLocal));

        Assert.AreEqual($"http://localhost:{_console.ServicePort}/", await _console.Manager.ResolveUrlAsync("local"));
        StringAssert.StartsWith(await _console.Manager.ResolveUrlAsync("server-desk"), "http://127.0.0.1:");
        Assert.IsNull(await _console.Manager.ResolveUrlAsync("nobody"));
    }

    [TestMethod]
    public async Task No_view_or_log_ever_carries_a_key()
    {
        var desk = await Server("server-desk");
        desk.PairingCode = "123456";
        var other = await Server("server-other", "Other");

        var paired = await _console.Manager.PairAsync(desk.BaseAddress, "123456");
        var (_, otherKey) = await _console.AddManagedAsync(other);
        var filed = await _console.Manager.PairAsync((await Server("server-waiting", "Waiting")).BaseAddress, null);
        var deskKey = _console.Store.Find("server-desk")!.DeviceKey;

        var views = new object?[]
        {
            paired,
            filed,
            await _console.Manager.ProbeAsync(desk.BaseAddress),
            await _console.Manager.GetAsync(false),
            await _console.Manager.GetAsync(true),
            await _console.Manager.OpenAsync("server-desk", "/"),
            await _console.Manager.ImportFromLegacyClientAsync(),
            _console.Manager.ListTargets(),
            await _console.Manager.ResolveUrlAsync("server-other")
        };

        foreach (var view in views)
        {
            var json = JsonSerializer.Serialize(view);
            var newtonsoft = Newtonsoft.Json.JsonConvert.SerializeObject(view);

            foreach (var key in new[] {deskKey, otherKey})
            {
                StringAssert.DoesNotMatch(json, new Regex(Regex.Escape(key)), view?.GetType().Name);
                StringAssert.DoesNotMatch(newtonsoft, new Regex(Regex.Escape(key)), view?.GetType().Name);
            }
        }

        string logs;
        lock (_console.Logs)
        {
            logs = string.Join("\n", _console.Logs);
        }

        StringAssert.DoesNotMatch(logs, new Regex(Regex.Escape(deskKey)));
        StringAssert.DoesNotMatch(logs, new Regex(Regex.Escape(otherKey)));
    }
}
