using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Remoting.Components.Console;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// Forgetting every managed server here only, as "Make this a new device" does on a copy of
/// another install's data directory: every key here is the original's too, and the original
/// still manages with it, so nothing may reach any of those servers — least of all a revoke.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ForgetAllLocallyTests
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

    private async Task<FakeServer> Server(string id, string name)
    {
        var server = await FakeServer.StartAsync(id, name, 46900);
        _servers.Add(server);
        return server;
    }

    private static bool Listening(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    [TestMethod]
    public async Task Every_server_and_filed_request_is_dropped_here_and_none_of_them_is_told()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        var study = await Server("server-study", "Study");
        var (deskDevice, _) = await _console.AddManagedAsync(desk);
        var (nasDevice, _) = await _console.AddManagedAsync(nas);

        // Both relays running, as they are once their servers were opened in this window.
        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);
        var nasPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(nas.ServerId, null))!.Url);

        // And a request filed with a third, still being collected.
        Assert.AreEqual(ManagedServerOutcome.AwaitingApproval,
            (await _console.Manager.PairAsync(study.BaseAddress, null)).Outcome);
        await WaitUntilAsync(() => study.Requests.Any(r => r.Path == "/remote-access/pair/claim"),
            "the request to be claimed once");

        var deskSeen = desk.Requests.Count;
        var nasSeen = nas.Requests.Count;

        Assert.AreEqual(2, await _console.Manager.ForgetAllLocallyAsync());

        // Gone here: no key, no relay, nothing waited on.
        Assert.AreEqual(0, _console.Store.Read().Servers.Count);
        Assert.AreEqual(0, _console.Manager.RunningRelays.Count);
        Assert.IsFalse(Listening(deskPort), "the desk's relay outlived its key");
        Assert.IsFalse(Listening(nasPort), "the NAS's relay outlived its key");
        var listing = await _console.Manager.GetAsync(false);
        Assert.AreEqual(0, listing.Servers.Count);
        Assert.AreEqual(0, listing.Requests.Count, "a filed request is still listed");
        Assert.IsNull(await _console.Manager.OpenAsync(desk.ServerId, null));

        // Approved now, it is never collected: this copy is no longer waiting for it.
        study.Approved = true;
        await Task.Delay(300);
        var studySeen = study.Requests.Count;
        await Task.Delay(500);
        Assert.AreEqual(studySeen, study.Requests.Count, "the filed request was still being claimed");
        Assert.IsNull(_console.Store.Find(study.ServerId), "an approval was collected after the reset");

        // Not a word to the servers — and so each still lets in the device the original install
        // manages it as.
        Assert.AreEqual(deskSeen, desk.Requests.Count, string.Join("\n", desk.Requests.Skip(deskSeen)));
        Assert.AreEqual(nasSeen, nas.Requests.Count, string.Join("\n", nas.Requests.Skip(nasSeen)));
        Assert.IsTrue(desk.KnownDevices.ContainsKey(deskDevice), "the desk was asked to revoke the original's key");
        Assert.IsTrue(nas.KnownDevices.ContainsKey(nasDevice), "the NAS was asked to revoke the original's key");
        Assert.IsFalse(_servers.SelectMany(s => s.Requests).Any(r => r.Method == HttpMethod.Delete.Method),
            "something was revoked");
    }

    [TestMethod]
    public async Task A_server_forgotten_here_pairs_again_and_gets_its_origin_back()
    {
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        var (copied, _) = await _console.AddManagedAsync(desk);
        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);

        await _console.Manager.ForgetAllLocallyAsync();

        // The browser still holds the desk's storage under its origin: kept from any other
        // server, as when it is forgotten on its own.
        Assert.AreEqual(deskPort, _console.Store.Read().RetiredRelayPorts?[desk.ServerId]);
        await _console.AddManagedAsync(nas);
        Assert.AreNotEqual(deskPort,
            ConsoleHarness.PortOf((await _console.Manager.OpenAsync(nas.ServerId, null))!.Url));

        // Paired again by this install, with a key of its own.
        desk.PairingCode = "123456";
        var paired = await _console.Manager.PairAsync(desk.BaseAddress, "123456");
        Assert.AreEqual(ManagedServerOutcome.Ok, paired.Outcome);
        var entry = _console.Store.Find(desk.ServerId)!;
        Assert.AreNotEqual(copied, entry.DeviceId);
        Assert.IsTrue(desk.KnownDevices.ContainsKey(copied), "the original's key went with the reset");

        var reopened = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);
        Assert.AreEqual(deskPort, reopened, "the desk came back on another origin");
        Assert.IsNull(_console.Store.Read().RetiredRelayPorts, "the origin went back to its server");

        // And it is managed as before: its relay forwards, signed with the new key.
        using var response = await ConsoleHarness.SendToRelayAsync(reopened, "/resource");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var forwarded = desk.Requests.Last(r => r.Path == "/resource");
        Assert.AreEqual(entry.DeviceId, forwarded.DeviceId);
        Assert.IsTrue(forwarded.SignatureValid);
    }

    [TestMethod]
    public async Task A_store_that_cannot_be_written_forgets_nothing_now_or_on_a_later_write()
    {
        // The reset is refused then, and the install must be as it was: its servers, their keys
        // and relays — not listed still while the next write of anything, the relay's own
        // record of an answer included, saves them away without a reset ever happening.
        var desk = await Server("server-desk", "Desk");
        var nas = await Server("server-nas", "NAS");
        var (deskDevice, deskKey) = await _console.AddManagedAsync(desk);
        await _console.AddManagedAsync(nas);
        var deskPort = ConsoleHarness.PortOf((await _console.Manager.OpenAsync(desk.ServerId, null))!.Url);

        // A directory where the file was, which every write's final rename fails on.
        File.Delete(_console.ManagedFile);
        Directory.CreateDirectory(Path.Combine(_console.ManagedFile, "held"));

        await Assert.ThrowsAsync<Exception>(() => _console.Manager.ForgetAllLocallyAsync());

        Assert.AreEqual(2, _console.Store.Read().Servers.Count);
        Assert.AreEqual(2, (await _console.Manager.GetAsync(false)).Servers.Count);
        Assert.AreEqual(1, _console.Manager.RunningRelays.Count);
        Assert.IsTrue(Listening(deskPort), "the desk's relay was stopped although its key was kept");

        // Writable again: an unrelated write saves both servers as they were.
        Directory.Delete(_console.ManagedFile, true);
        Assert.IsTrue(await _console.Manager.SetPathMappingsAsync(desk.ServerId,
            [new ManagedServerPathMapping("/data/media", "/Volumes/media")]));

        var saved = new ManagedServerStore(_console.Store.Directory).Read();
        CollectionAssert.AreEquivalent(new[] { desk.ServerId, nas.ServerId },
            saved.Servers.Select(s => s.ServerId).ToArray(), await File.ReadAllTextAsync(_console.ManagedFile));
        Assert.AreEqual(deskKey, saved.Servers.Single(s => s.ServerId == desk.ServerId).DeviceKey);
        Assert.IsNull(saved.RetiredRelayPorts, "a relay origin was retired by the reset that failed");
        Assert.AreEqual(2, (await _console.Manager.GetAsync(false)).Servers.Count);

        // Still managed: the desk's relay forwards, signed with the key it had.
        using var response = await ConsoleHarness.SendToRelayAsync(deskPort, "/resource");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(deskDevice, desk.Requests.Last(r => r.Path == "/resource").DeviceId);

        // And tried again, the reset forgets both.
        Assert.AreEqual(2, await _console.Manager.ForgetAllLocallyAsync());
        Assert.AreEqual(0, new ManagedServerStore(_console.Store.Directory).Read().Servers.Count);
    }

    [TestMethod]
    public async Task Nothing_managed_is_nothing_to_forget()
    {
        Assert.AreEqual(0, await _console.Manager.ForgetAllLocallyAsync());
        Assert.AreEqual(0, (await _console.Manager.GetAsync(false)).Servers.Count);
        // A device that manages nothing still has no store.
        Assert.IsFalse(File.Exists(_console.ManagedFile));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            Assert.IsTrue(DateTime.UtcNow < deadline, $"timed out waiting for {what}");
            await Task.Delay(20);
        }
    }
}
