using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess.Console;

/// <summary>
/// A managed server's address handed to someone else — another install, or this device
/// itself — while its relay would still forward there, signed with this device's key.
/// </summary>
/// <remarks>
/// <para>
/// Found on a real machine: after both installs restarted, a managed server's stored
/// <c>http://127.0.0.1:&lt;port&gt;</c> belonged to another Bakabase on the same machine, and
/// the window showed that one's library — its <c>server-info</c> answered with the wrong
/// identity, and every write would have landed on the wrong server. Nothing refused it: a
/// server takes any loopback caller as local and never looks at the key, and an unrestricted
/// one may let anyone in, so the device key being "accepted" said nothing about who accepted
/// it. The same happens on a LAN when a DHCP lease moves a NAS's address to another NAS.
/// </para>
/// <para>
/// Each test swaps two real servers on one port and drives the console's real relay over
/// HTTP, asserting on what reached the server that took the address over: the question
/// "who are you" (unsigned, <c>/remote-access/server-info</c>) and nothing else.
/// </para>
/// </remarks>
[TestClass]
public class RelayIdentityTests
{
    /// <summary>How long a refusal stands before the address is asked again. Short, so a test can outwait it.</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How recent an answer a new connection needs. The app's two seconds, shortened so a
    /// test can let it pass: a swap inside it is the one case the connect step alone cannot
    /// see, and the tests below are about swaps after it.
    /// </summary>
    private static readonly TimeSpan ConnectionWindow = TimeSpan.FromMilliseconds(100);

    private ConsoleHarness _console = null!;
    private FakeServer? _desk;
    private FakeServer? _other;
    private string _deskAddress = null!;
    private int _deskPort;

    private static void Options(Bakabase.Remoting.Components.Console.RemoteConsoleOptions o)
    {
        o.IdentityRetryInterval = RetryInterval;
        o.IdentityConnectionWindow = ConnectionWindow;
    }

    /// <summary>Lets the latest answer grow too old to open a new connection on.</summary>
    private static Task PastTheConnectionWindow() => Task.Delay(ConnectionWindow + TimeSpan.FromMilliseconds(50));

    [TestInitialize]
    public async Task Setup()
    {
        _console = await ConsoleHarness.StartAsync(options: Options);
        _desk = await FakeServer.StartAsync("server-desk", "Desk", 47100);
        _deskPort = _desk.Port;
        _deskAddress = _desk.BaseAddress;

        await _console.AddManagedAsync(_desk);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _console.DisposeAsync();

        if (_desk != null)
        {
            await _desk.DisposeAsync();
        }

        if (_other != null)
        {
            await _other.DisposeAsync();
        }
    }

    private async Task<int> OpenAsync() =>
        ConsoleHarness.PortOf((await _console.Manager.OpenAsync("server-desk", null))!.Url);

    /// <summary>The desk goes away and <paramref name="serverId"/> takes its port, as a relaunched install does.</summary>
    private async Task<FakeServer> ReplaceDeskAsync(string serverId = "server-other", string name = "Other",
        Action<FakeServer>? configure = null)
    {
        await _desk!.DisposeAsync();
        _desk = null;

        _other = await FakeServer.TakeOverAsync(serverId, name, _deskPort);
        configure?.Invoke(_other);

        return _other;
    }

    private static string? FailureOf(HttpResponseMessage response) =>
        response.Headers.TryGetValues(UpstreamForwarder.FailureHeader, out var values) ? values.Single() : null;

    /// <summary>Everything that reached <paramref name="server"/> other than being asked who it is.</summary>
    private static string[] BeyondTheQuestion(FakeServer server) =>
        server.Requests.Where(r => !(r.Method == "GET" && r.Path == "/remote-access/server-info" && r.DeviceId == null))
            .Select(r => $"{r.Method} {r.Path} (signed as {r.DeviceId ?? "nobody"})")
            .ToArray();

    private static async Task AssertRefusedAsWrongServerAsync(HttpResponseMessage response, string answeredBy)
    {
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.WrongServer), FailureOf(response));

        var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("message").GetString()!;
        StringAssert.Contains(message, $"now answers as another server ({answeredBy})");
        StringAssert.Contains(message, "not Desk");
    }

    private async Task<int?> SwitcherStateAsync(int relayPort)
    {
        var body = JsonDocument.Parse(await (await ConsoleHarness.SendToRelayAsync(relayPort, "/client/switcher"))
            .Content.ReadAsStringAsync()).RootElement;

        var entry = body.GetProperty("data").GetProperty("targets").EnumerateArray()
            .Single(t => t.GetProperty("id").GetString() == "server-desk");

        return entry.TryGetProperty("state", out var state) ? state.GetInt32() : null;
    }

    [TestMethod]
    public async Task The_listing_says_what_the_server_last_said_it_is_never_what_answers_in_its_place()
    {
        _desk!.SaysItIs = (ServerKind.Desktop, RemoteDevicePlatform.MacOS);

        var listed = (await _console.Manager.GetAsync(true)).Servers.Single();
        Assert.AreEqual(ManagedServerState.Online, listed.State);
        Assert.AreEqual(ServerKind.Desktop, listed.Kind);
        Assert.AreEqual(RemoteDevicePlatform.MacOS, listed.Platform);

        // Another install takes its port and says it is something else: like the mode and the
        // version, the desk's own word stands, and nothing the newcomer said is shown as the desk's.
        await ReplaceDeskAsync(configure: s => s.SaysItIs = (ServerKind.Headless, RemoteDevicePlatform.Linux));

        listed = (await _console.Manager.GetAsync(true)).Servers.Single();
        Assert.AreEqual(ManagedServerState.WrongServer, listed.State);
        Assert.AreEqual(ServerKind.Desktop, listed.Kind);
        Assert.AreEqual(RemoteDevicePlatform.MacOS, listed.Platform);
    }

    [TestMethod]
    public async Task A_server_from_before_it_said_what_it_is_is_listed_without_it()
    {
        var listed = (await _console.Manager.GetAsync(true)).Servers.Single();

        Assert.AreEqual(ManagedServerState.Online, listed.State);
        Assert.IsNull(listed.Kind);
        Assert.IsNull(listed.Platform);
    }

    [TestMethod]
    public async Task After_a_restart_a_loopback_trusting_install_at_the_old_port_gets_nothing()
    {
        // The case found on a real machine: both apps restart, and the port the desk had now
        // belongs to another install on this machine, which takes any loopback caller as
        // sitting at it and never looks at a signature.
        var root = _console.Root;
        await _console.StopAsync();

        var other = await ReplaceDeskAsync(configure: s => s.TrustsLoopback = true);

        _console = await ConsoleHarness.StartAsync(root, options: Options);

        // Opening still hands out the relay — the window is told why when it gets there.
        var port = await OpenAsync();

        await AssertRefusedAsWrongServerAsync(await ConsoleHarness.SendToRelayAsync(port, "/resource/search"), "Other");
        await AssertRefusedAsWrongServerAsync(
            await ConsoleHarness.SendToRelayAsync(port, "/custom-property", HttpMethod.Post, "{\"name\":\"made-here\"}"),
            "Other");

        // Not a read, not a write, not a signed byte: only ever asked who it is.
        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(other),
            string.Join("\n", other.Requests));
        Assert.IsTrue(other.Requests.Any(), "the address was never asked who it is");

        // And both the listing and the window's switcher say so.
        var listed = (await _console.Manager.GetAsync(false)).Servers.Single();
        Assert.AreEqual(ManagedServerState.WrongServer, listed.State);
        Assert.AreEqual("server-other", listed.AnsweredBy!.ServerId);
        Assert.AreEqual("Other", listed.AnsweredBy.Name);
        Assert.IsFalse(listed.AnsweredBy.IsThisDevice);
        Assert.AreEqual((int) ManagedServerState.WrongServer, await SwitcherStateAsync(port));

        // Never renamed after whoever answered, never re-keyed.
        Assert.AreEqual("Desk", _console.Store.Find("server-desk")!.ServerName);
        Assert.AreEqual(_deskAddress, _console.Store.Find("server-desk")!.BaseAddress);
    }

    [TestMethod]
    public async Task A_running_relay_stops_the_moment_an_unrestricted_server_takes_its_address()
    {
        var port = await OpenAsync();

        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(port, "/resource/search")).StatusCode);

        // The desk goes away and an unrestricted server — one that lets anybody in — takes
        // its port while the desk's page is open. The relay's last answer is seconds old.
        var other = await ReplaceDeskAsync(configure: s => s.Mode = RemoteAccessMode.Unrestricted);

        // Every request needs a new connection now, and a new connection is asked about.
        await PastTheConnectionWindow();

        await AssertRefusedAsWrongServerAsync(await ConsoleHarness.SendToRelayAsync(port, "/resource/search"), "Other");
        await AssertRefusedAsWrongServerAsync(
            await ConsoleHarness.SendToRelayAsync(port, "/resource/search", HttpMethod.Post, "{\"keyword\":\"x\"}"),
            "Other");

        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(other),
            string.Join("\n", other.Requests));
        Assert.AreEqual((int) ManagedServerState.WrongServer, await SwitcherStateAsync(port));
    }

    [TestMethod]
    public async Task No_hub_connection_opens_to_whoever_took_the_address()
    {
        var port = await OpenAsync();
        var other = await ReplaceDeskAsync(configure: s => s.TrustsLoopback = true);

        await PastTheConnectionWindow();

        using var socket = new ClientWebSocket();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await Assert.ThrowsExceptionAsync<WebSocketException>(() =>
            socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/hub/echo"), timeout.Token));

        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(other),
            string.Join("\n", other.Requests));
    }

    [TestMethod]
    public async Task The_relays_own_calls_are_held_back_too()
    {
        var port = await OpenAsync();
        var other = await ReplaceDeskAsync(configure: s => s.TrustsLoopback = true);

        await PastTheConnectionWindow();

        // What the window is told about the server: not reachable, without asking whoever
        // is there what it makes of this device.
        var status = JsonDocument.Parse(await (await ConsoleHarness.SendToRelayAsync(port, "/client/status"))
            .Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.IsFalse(status.GetProperty("serverReachable").GetBoolean());

        var context = JsonDocument.Parse(await (await ConsoleHarness.SendToRelayAsync(port, "/remote-access/context"))
            .Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.IsFalse(context.GetProperty("serverReachable").GetBoolean());

        // An action run on this machine asks the server which folder a resource is; not this one.
        await ConsoleHarness.SendToRelayAsync(port, "/resource/directory?id=42");

        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(other),
            string.Join("\n", other.Requests));
    }

    [TestMethod]
    public async Task A_window_navigating_there_is_shown_why_with_the_way_back()
    {
        var port = await OpenAsync();
        await ReplaceDeskAsync("server-other", "<script>alert(1)</script>");

        await PastTheConnectionWindow();

        var page = await ConsoleHarness.NavigateAsync($"http://127.0.0.1:{port}/", "same-origin");
        var html = await page.Content.ReadAsStringAsync();

        // At the address asked for, so a reload once it is put right asks again.
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, page.StatusCode);
        Assert.AreEqual("text/html; charset=utf-8", page.Content.Headers.ContentType!.ToString());
        StringAssert.Contains(page.Headers.GetValues("Content-Security-Policy").Single(), "default-src 'none'");
        StringAssert.Contains(html, "Desk is not at its address any more");
        StringAssert.Contains(html, $"127.0.0.1:{_deskPort} now answers as another server");
        StringAssert.Contains(html, "Desk 已不在原来的地址");
        StringAssert.Contains(html, $"http://localhost:{_console.ServicePort}/");

        // The name is the other server's to choose, and never becomes markup.
        StringAssert.Contains(html, "&lt;script&gt;alert(1)&lt;/script&gt;");
        Assert.IsFalse(html.Contains("<script>", StringComparison.OrdinalIgnoreCase), html);
    }

    [TestMethod]
    public async Task An_address_that_now_answers_as_this_device_is_refused_as_this_device()
    {
        // This device's own identity, answering from an address this app does not know as
        // its own — a LAN address of this machine, a name that resolves here.
        var port = await OpenAsync();
        var self = await ReplaceDeskAsync(ConsoleHarness.OwnServerId, "This computer",
            s => s.TrustsLoopback = true);

        await PastTheConnectionWindow();

        var response = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");
        var message = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement
            .GetProperty("message").GetString()!;

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.WrongServer), FailureOf(response));
        StringAssert.Contains(message, "now reaches this computer itself, not Desk");
        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(self), string.Join("\n", self.Requests));

        var listed = (await _console.Manager.GetAsync(false)).Servers.Single();
        Assert.AreEqual(ManagedServerState.WrongServer, listed.State);
        Assert.IsTrue(listed.AnsweredBy!.IsThisDevice);
    }

    [TestMethod]
    public async Task An_address_that_is_one_of_this_apps_own_ports_is_refused_without_a_request()
    {
        // The stored address is this app's own server port now — the case where the desk's
        // old port went to this device on relaunch. Nothing is even asked: the handshake
        // recognises its own ports before it sends anything.
        await _console.Store.MutateAsync(data =>
            data.Servers.Single().BaseAddress = $"http://127.0.0.1:{_console.ServicePort}");

        var port = await OpenAsync();
        var response = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.WrongServer), FailureOf(response));
        Assert.AreEqual(0, _desk!.Requests.Count(r => r.Path == "/resource/search"));

        var listed = (await _console.Manager.GetAsync(false)).Servers.Single();
        Assert.AreEqual(ManagedServerState.WrongServer, listed.State);
        Assert.IsTrue(listed.AnsweredBy!.IsThisDevice);

        // Probing says the same, and still sends nothing.
        listed = (await _console.Manager.GetAsync(true)).Servers.Single();
        Assert.AreEqual(ManagedServerState.WrongServer, listed.State);
    }

    [TestMethod]
    public async Task A_probe_that_finds_someone_else_stops_a_relay_whose_answer_was_still_fresh()
    {
        var port = await OpenAsync();
        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(port, "/resource/search")).StatusCode);

        // The relay's own answer is seconds old and its connections are pooled. A probe from
        // the devices page asks the address itself, and the relay acts on what it heard.
        var other = await ReplaceDeskAsync(configure: s => s.TrustsLoopback = true);

        var listed = (await _console.Manager.GetAsync(true)).Servers.Single();
        Assert.AreEqual(ManagedServerState.WrongServer, listed.State);

        await AssertRefusedAsWrongServerAsync(await ConsoleHarness.SendToRelayAsync(port, "/resource/search"), "Other");
        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(other),
            string.Join("\n", other.Requests));
    }

    [TestMethod]
    public async Task The_server_back_at_its_address_is_forwarded_to_again()
    {
        var port = await OpenAsync();
        await ReplaceDeskAsync(configure: s => s.TrustsLoopback = true);
        await PastTheConnectionWindow();

        await AssertRefusedAsWrongServerAsync(await ConsoleHarness.SendToRelayAsync(port, "/resource/search"), "Other");

        // The other install goes and the desk comes back on its port.
        await _other!.DisposeAsync();
        _other = null;
        _desk = await FakeServer.TakeOverAsync("server-desk", "Desk", _deskPort);

        var entry = _console.Store.Find("server-desk")!;
        _desk.KnownDevices[entry.DeviceId] = entry.DeviceKey;

        // Asked again once the refusal has stood its retry interval.
        await Task.Delay(RetryInterval + TimeSpan.FromMilliseconds(100));

        var again = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode, string.Join("\n", _console.Logs));
        Assert.IsTrue(_desk.Requests.Single(r => r.Path == "/resource/search").SignatureValid);
        Assert.AreNotEqual((int) ManagedServerState.WrongServer, await SwitcherStateAsync(port));

        var listed = (await _console.Manager.GetAsync(false)).Servers.Single();
        Assert.AreNotEqual(ManagedServerState.WrongServer, listed.State);
        Assert.IsNull(listed.AnsweredBy);
    }

    [TestMethod]
    public async Task Stopping_managing_it_asks_nothing_of_whoever_took_its_address()
    {
        // Forgetting asks the server to revoke this device, signed as it. Sent to the wrong
        // server, a loopback-trusting one would carry the request out.
        var other = await ReplaceDeskAsync(configure: s => s.TrustsLoopback = true);

        Assert.IsTrue(await _console.Manager.ForgetAsync("server-desk"));

        Assert.IsNull(_console.Store.Find("server-desk"));
        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(other),
            string.Join("\n", other.Requests));
    }

    [TestMethod]
    public async Task A_window_navigating_to_a_server_nobody_answers_for_is_shown_why()
    {
        var port = await OpenAsync();

        await _desk!.DisposeAsync();
        _desk = null;
        await PastTheConnectionWindow();

        var page = await ConsoleHarness.NavigateAsync($"http://127.0.0.1:{port}/", "same-origin");
        var html = await page.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, page.StatusCode);
        StringAssert.Contains(html, "Desk is not answering");
        StringAssert.Contains(html, $"127.0.0.1:{_deskPort}");

        // A fetch gets the refusal the frontend reads, not a page.
        var fetch = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, fetch.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.ServerUnreachable), FailureOf(fetch));
        Assert.AreEqual((int) ManagedServerState.Offline, await SwitcherStateAsync(port));
    }

    [TestMethod]
    public async Task A_confirmed_server_costs_no_question_per_request()
    {
        // The hot path: a page loading makes dozens of requests on pooled connections, and
        // the address is asked once for all of them.
        var port = await OpenAsync();
        var before = _desk!.Requests.Count(r => r.Path == "/remote-access/server-info");

        for (var i = 0; i < 30; i++)
        {
            Assert.AreEqual(HttpStatusCode.OK,
                (await ConsoleHarness.SendToRelayAsync(port, $"/resource/search?page={i}")).StatusCode);
        }

        var asked = _desk.Requests.Count(r => r.Path == "/remote-access/server-info") - before;

        Assert.AreEqual(30, _desk.Requests.Count(r => r.Path == "/resource/search"));
        Assert.IsTrue(asked <= 2, $"asked {asked} times for 30 requests");
    }

    [TestMethod]
    public async Task A_store_that_cannot_be_written_never_stops_the_right_server_being_forwarded_to()
    {
        // Noting that the server answered — its name, when it was last seen — is bookkeeping.
        // A full disk, or a file a scanner holds, must not turn a server that answered as
        // itself into one nobody answers for.
        var lifetime = TimeSpan.FromMilliseconds(300);
        var root = _console.Root;
        await _console.StopAsync();
        _console = await ConsoleHarness.StartAsync(root, options: o =>
        {
            Options(o);
            // Short, so a request can be made to wait for a fresh answer.
            o.IdentityCheckInterval = lifetime;
            o.StoreRetryInterval = TimeSpan.FromMilliseconds(100);
        });

        var port = await OpenAsync();
        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(port, "/resource/search")).StatusCode);
        await WaitUntil(() => _console.Store.Find("server-desk")!.LastConnectedAt != null);

        // Last seen long enough ago that its next answer is written down, and the file gone
        // unwritable: a directory where it was, which every write's final rename fails on.
        var longAgo = DateTime.UtcNow.AddHours(-1);
        await _console.Store.MutateAsync(data => data.Servers.Single().LastConnectedAt = longAgo);
        File.Delete(_console.ManagedFile);
        Directory.CreateDirectory(Path.Combine(_console.ManagedFile, "held"));

        for (var i = 0; i < 3; i++)
        {
            // Past the answer's lifetime: the request waits for the address to be asked again.
            await Task.Delay(lifetime + TimeSpan.FromMilliseconds(100));

            var response = await ConsoleHarness.SendToRelayAsync(port, $"/resource/search?round={i}");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
                $"{await response.Content.ReadAsStringAsync()}\n{string.Join("\n", _console.Logs)}");
        }

        Assert.AreEqual(4, _desk!.Requests.Count(r => r.Path == "/resource/search" && r.SignatureValid == true));

        // A probe says the same: the server answered, and takes this device.
        Assert.AreEqual(ManagedServerState.Online, (await _console.Manager.GetAsync(true)).Servers.Single().State);

        // Not lost silently: the failure is logged, and the write is tried again on its own —
        // nothing below asks the server anything — until the file can be written.
        Assert.IsTrue(_console.Logs.Any(l => l.StartsWith("Warning", StringComparison.Ordinal) &&
                                             l.Contains("server-desk", StringComparison.Ordinal) &&
                                             l.Contains("managed-server store", StringComparison.Ordinal)),
            string.Join("\n", _console.Logs));
        Assert.AreEqual(longAgo, _console.Store.Find("server-desk")!.LastConnectedAt);

        var asked = _desk.Requests.Count;
        Directory.Delete(_console.ManagedFile, true);

        await WaitUntil(() => _console.Store.Find("server-desk")!.LastConnectedAt > longAgo.AddMinutes(30));
        Assert.AreEqual(asked, _desk.Requests.Count);
        StringAssert.Contains(await File.ReadAllTextAsync(_console.ManagedFile), "server-desk");
    }

    [TestMethod]
    public async Task A_server_with_remote_access_turned_off_is_shown_how_to_turn_it_on()
    {
        // Switched off at the desk: its gate answers a caller from another machine 403 with
        // the reason, server-info included, so it never says who it is. That is not a server to
        // go looking for — it is one to turn remote access back on at.
        _desk!.RemoteAccessOff = true;

        var port = await OpenAsync();

        var page = await ConsoleHarness.NavigateAsync($"http://127.0.0.1:{port}/", "same-origin");
        var html = await page.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, page.StatusCode);
        Assert.AreEqual("text/html; charset=utf-8", page.Content.Headers.ContentType!.ToString());
        StringAssert.Contains(html, $"Remote access is turned off at 127.0.0.1:{_deskPort}");
        StringAssert.Contains(html, "under “Let other devices manage this device”");
        StringAssert.Contains(html, $"127.0.0.1:{_deskPort} 已关闭远程访问");
        StringAssert.Contains(html, "开启“允许其他设备管理本机”");
        Assert.IsFalse(html.Contains("is running", StringComparison.Ordinal), html);
        Assert.IsFalse(html.Contains("正在运行", StringComparison.Ordinal), html);

        // A fetch is told the same, in the refusal the frontend reads.
        var fetch = await ConsoleHarness.SendToRelayAsync(port, "/resource/search");
        var message = JsonDocument.Parse(await fetch.Content.ReadAsStringAsync()).RootElement
            .GetProperty("message").GetString()!;

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, fetch.StatusCode);
        Assert.AreEqual(nameof(ClientForwardingFailure.ServerUnreachable), FailureOf(fetch));
        StringAssert.Contains(message, $"Remote access is turned off at 127.0.0.1:{_deskPort}");
        StringAssert.Contains(message, "under “Let other devices manage this device”");
        Assert.IsFalse(message.Contains("is running", StringComparison.Ordinal), message);

        // Asked who it is, and nothing else.
        CollectionAssert.AreEqual(Array.Empty<string>(), BeyondTheQuestion(_desk), string.Join("\n", _desk.Requests));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        Assert.IsTrue(condition());
    }
}
