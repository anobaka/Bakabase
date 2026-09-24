using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bakabase.Tests.RemoteAccess.Console;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// What a relay writes to the log of the app it runs in.
/// </summary>
/// <remarks>
/// <para>
/// A relay forwards every asset, API call and range request its window makes, and YARP
/// writes two Information lines for each — "Proxying to http://&lt;server&gt;/&lt;path&gt;" and
/// "Received HTTP/1.1 response 200." In the desktop app those went to the app's persistent
/// log, a pair per request, with every managed server's address in them.
/// </para>
/// <para>
/// The fix must not buy that silence with anything that matters: YARP's own warnings and
/// errors, and the relay's own lines — the guard refusing a caller, a switch ticket taken
/// off an address — are how a failure is diagnosed after the fact.
/// </para>
/// </remarks>
[TestClass]
public class RelayLoggingTests
{
    private const string ForwarderCategory = "Yarp.ReverseProxy.Forwarder.HttpForwarder";

    /// <summary>A line from any YARP category below Warning, as the capture formats it.</summary>
    private static bool IsQuietYarpLine(string line) =>
        (line.StartsWith("Trace Yarp", StringComparison.Ordinal) ||
         line.StartsWith("Debug Yarp", StringComparison.Ordinal) ||
         line.StartsWith("Information Yarp", StringComparison.Ordinal));

    private static List<string> Snapshot(List<string> logs)
    {
        lock (logs)
        {
            return [.. logs];
        }
    }

    [TestMethod]
    public async Task A_managed_servers_relay_forwards_without_logging_each_request_but_still_logs_refusals()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        await console.AddManagedAsync(desk);

        var url = (await console.Manager.OpenAsync(desk.ServerId, "/#/resource"))!.Url;
        var port = ConsoleHarness.PortOf(url);

        // Everything a window does: arrive with its ticket, load a page and an asset, fetch.
        var landing = await ConsoleHarness.NavigateAsync(url, "cross-site");
        Assert.AreEqual(HttpStatusCode.OK, landing.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await ConsoleHarness.SendToRelayAsync(port, "/")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await ConsoleHarness.SendToRelayAsync(port, "/assets/index-abc123.js")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await ConsoleHarness.SendToRelayAsync(port, "/resource/search", HttpMethod.Post, "{}")).StatusCode);

        // And a page elsewhere trying its luck, which the guard turns away.
        var refused = await ConsoleHarness.NavigateAsync($"http://127.0.0.1:{port}/resource", "cross-site");
        Assert.AreEqual(HttpStatusCode.BadRequest, refused.StatusCode);

        Assert.IsTrue(desk.Requests.Count(r => r.Path is "/" or "/assets/index-abc123.js" or "/resource/search") >= 3,
            "the requests were not forwarded, so their logging was not checked");

        var logs = Snapshot(console.Logs);
        var all = string.Join("\n", logs);

        Assert.IsFalse(logs.Any(IsQuietYarpLine), all);
        Assert.IsFalse(logs.Any(l => l.Contains(desk.BaseAddress + "/assets", StringComparison.Ordinal)), all);

        // The relay's own lines are all still there, Information included.
        Assert.IsTrue(logs.Any(l => l.StartsWith("Warning Bakabase.Remoting.Components.Relay.RelayComposition: Refused",
            StringComparison.Ordinal)), all);
        Assert.IsTrue(logs.Any(l => l.StartsWith(
            "Information Bakabase.Remoting.Components.Relay.RelayComposition: Took a switch ticket off",
            StringComparison.Ordinal)), all);
    }

    [TestMethod]
    public async Task A_relays_forwarder_still_reports_warnings_and_errors_into_this_apps_log()
    {
        await using var console = await ConsoleHarness.StartAsync();
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        await console.AddManagedAsync(desk);
        await console.Manager.OpenAsync(desk.ServerId, null);

        var relay = console.Manager.RelayServices(desk.ServerId)!;
        var logger = relay.GetRequiredService<ILoggerFactory>().CreateLogger(ForwarderCategory);

        Assert.IsFalse(logger.IsEnabled(LogLevel.Information));
        Assert.IsTrue(logger.IsEnabled(LogLevel.Warning));
        Assert.IsTrue(logger.IsEnabled(LogLevel.Error));

        logger.LogInformation("an Information line from the forwarder");
        logger.LogWarning("a Warning from the forwarder");
        logger.LogError("an Error from the forwarder");

        // Only YARP's categories: anything else through the same factory keeps its level.
        var other = relay.GetRequiredService<ILoggerFactory>().CreateLogger("YarpLookalike.Category");
        other.LogInformation("not the forwarder");

        var logs = Snapshot(console.Logs);
        var all = string.Join("\n", logs);

        Assert.IsFalse(logs.Any(l => l.Contains("an Information line from the forwarder")), all);
        Assert.IsTrue(logs.Contains($"Warning {ForwarderCategory}: a Warning from the forwarder"), all);
        Assert.IsTrue(logs.Contains($"Error {ForwarderCategory}: an Error from the forwarder"), all);
        Assert.IsTrue(logs.Contains("Information YarpLookalike.Category: not the forwarder"), all);
    }

    [TestMethod]
    public async Task A_stopped_relay_leaves_this_apps_logging_working()
    {
        // The relay logs through the app's own factory. Its container is disposed when the
        // relay stops, and must not take the app's logging down with it.
        await using var console = await ConsoleHarness.StartAsync();
        await using var desk = await FakeServer.StartAsync("server-desk", "Desk", 46900);
        await console.AddManagedAsync(desk);
        await console.Manager.OpenAsync(desk.ServerId, null);

        Assert.IsTrue(await console.Manager.ForgetAsync(desk.ServerId));
        Assert.AreEqual(0, console.Manager.RunningRelays.Count);

        console.Get<ILoggerFactory>().CreateLogger("After.The.Relay").LogWarning("still logging");

        Assert.IsTrue(Snapshot(console.Logs).Contains("Warning After.The.Relay: still logging"));
    }
}
