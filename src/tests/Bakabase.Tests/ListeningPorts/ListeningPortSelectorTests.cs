using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Ports;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.Remoting.Components.Console;
using Bakabase.Remoting.Components.Forwarding;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.ListeningPorts;

[TestClass]
public class ListeningPortSelectorTests
{
    private static readonly Func<int, bool> AllFree = _ => true;

    [TestMethod]
    public void Remembered_ports_come_back_in_their_order()
    {
        var chosen = ListeningPortSelector.Select(3, [34571, 34569, 34570], [], AllFree);
        CollectionAssert.AreEqual(new[] {34571, 34569, 34570}, chosen.ToArray(),
            "the first is the window's origin; it must be the same one as last time");
    }

    [TestMethod]
    public void Without_memory_the_window_is_scanned_from_its_start()
    {
        CollectionAssert.AreEqual(new[] {34567, 34568, 34569},
            ListeningPortSelector.Select(3, [], [], AllFree).ToArray());
    }

    [TestMethod]
    public void A_taken_remembered_port_is_skipped_and_the_rest_keep_their_places()
    {
        var chosen = ListeningPortSelector.Select(3, [34570, 34571, 34572], [], port => port != 34571);
        CollectionAssert.AreEqual(new[] {34570, 34572, 34567}, chosen.ToArray());
    }

    [TestMethod]
    public void Explicit_ports_are_never_picked_again()
    {
        var chosen = ListeningPortSelector.Select(2, [34567], [34567, 34568], AllFree);
        CollectionAssert.AreEqual(new[] {34569, 34570}, chosen.ToArray());
    }

    [TestMethod]
    public void The_window_stays_clear_of_every_other_port_the_app_hands_out()
    {
        // A main port inside the relays' range would change which server an origin belongs to.
        Assert.IsTrue(ListeningPortSelector.WindowEnd <= RemoteConsoleOptions.DefaultFirstRelayPort);
        Assert.IsTrue(ListeningPortSelector.OverflowStart >=
                      RemoteConsoleOptions.DefaultFirstRelayPort + new RemoteConsoleOptions().RelayPortRange);
        // Where the desktop app has always started, so existing windows keep their origin.
        Assert.AreEqual(34567, ListeningPortSelector.WindowStart);

        // A remembered port in those ranges (or anywhere else odd) is not honoured…
        var chosen = ListeningPortSelector.Select(1, [34600, 34650, 34700, 80], [], AllFree);
        CollectionAssert.AreEqual(new[] {34567}, chosen.ToArray());

        // …and a full window continues past the relays rather than into them.
        var full = ListeningPortSelector.Select(1, [], [], port => port >= ListeningPortSelector.WindowEnd);
        CollectionAssert.AreEqual(new[] {ListeningPortSelector.OverflowStart}, full.ToArray());
    }

    [TestMethod]
    public void A_port_in_TIME_WAIT_is_reused()
    {
        var port = FreeWindowPort();

        // The server side closes first, which is what leaves the server's port in TIME_WAIT —
        // exactly the state a plain restart finds its own port in.
        using (var listener = new TcpListener(IPAddress.Any, port))
        {
            listener.Start();
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, port);
            using (var accepted = listener.AcceptTcpClient())
            {
                accepted.Client.Shutdown(SocketShutdown.Both);
            }

            Thread.Sleep(100);
            listener.Stop();
        }

        Thread.Sleep(100);
        var inTimeWait = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections()
            .Any(c => c.LocalEndPoint.Port == port && c.State == TcpState.TimeWait);
        if (!inTimeWait) Assert.Inconclusive($"Could not put {port} into TIME_WAIT on this machine.");

        Assert.AreNotEqual(port, NetworkUtils.GetFreeTcpPortFrom(port),
            "the old connection-table check read TIME_WAIT as taken — why the port drifted on restart");
        Assert.IsTrue(ListeningPortSelector.IsFree(port, IPAddress.Any));
        CollectionAssert.AreEqual(new[] {port}, ListeningPortSelector.Select(1, [port], []).ToArray());
    }

    [TestMethod]
    public void A_port_another_program_listens_on_is_skipped()
    {
        foreach (var address in new[] {IPAddress.Any, IPAddress.Loopback})
        {
            var port = FreeWindowPort();
            using var other = new TcpListener(address, port);
            other.Start();

            Assert.IsFalse(ListeningPortSelector.IsFree(port, IPAddress.Any),
                $"a listener on {address}:{port} must count as taken");
            var chosen = ListeningPortSelector.Select(1, [port], []);
            Assert.AreNotEqual(port, chosen[0]);
        }
    }

    [TestMethod]
    public void A_port_taken_for_one_launch_is_back_the_launch_after()
    {
        // The verifier's scenario: another program held the first port for one launch. That
        // launch had to move; the next one must not stay moved, or the window's origin and the
        // browser storage under it are lost for good.
        var dir = Path.Combine(Path.GetTempPath(), "bakabase-ports-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ports = FreeWindowPorts(3);
            var memory = new ListeningPortMemory(dir);
            memory.Record(ports);

            using (var other = new TcpListener(IPAddress.Loopback, ports[0]))
            {
                other.Start();
                var displaced = ListeningPortSelector.Select(3, memory.Read().Candidates, []);
                CollectionAssert.DoesNotContain(displaced.ToArray(), ports[0]);
                Assert.AreEqual(ports[1], displaced[0], "the rest of the preference keeps its order");
                memory.Record(displaced);
            }

            CollectionAssert.AreEqual(ports, memory.Read().Preferred.ToArray());
            CollectionAssert.AreEqual(ports, ListeningPortSelector.Select(3, memory.Read().Candidates, []).ToArray());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void A_conflict_that_persists_keeps_landing_on_the_same_fallback()
    {
        // Preferred all taken: the last used ports come next, ahead of the scan, so the fallback
        // origin does not wander with whatever else happens to be free.
        var chosen = ListeningPortSelector.Select(2,
            new ListeningPortMemory.Remembered([34567, 34568], [34590, 34591]).Candidates, [],
            port => port is not (34567 or 34568));
        CollectionAssert.AreEqual(new[] {34590, 34591}, chosen.ToArray());
    }

    [TestMethod]
    public void A_port_another_program_listens_on_over_IPv6_loopback_is_skipped()
    {
        if (!Socket.OSSupportsIPv6) Assert.Inconclusive("No IPv6 here.");

        var port = FreeWindowPort();
        using var other = new TcpListener(IPAddress.IPv6Loopback, port);
        try
        {
            other.Start();
        }
        catch (SocketException)
        {
            Assert.Inconclusive("Cannot listen on ::1 here.");
        }

        // The window opens localhost, which a browser may well resolve to ::1 first.
        Assert.IsFalse(ListeningPortSelector.IsFree(port, IPAddress.Any));
    }

    [TestMethod]
    public void Explicit_configuration_is_taken_as_is()
    {
        var picked = new List<(int Count, int[] Reserved)>();
        IReadOnlyList<int> Pick(int count, IReadOnlyCollection<int> reserved)
        {
            picked.Add((count, reserved.ToArray()));
            return Enumerable.Range(40000, count).ToList();
        }

        // Configured ports first and in their order, the automatic ones after.
        var ports = AppHost.ComposeListeningPorts(RuntimeMode.MacOS,
            new AppOptions {ListeningPorts = [5000, 34567], AutoListeningPortCount = 2}, new EnvOptions(), 3, Pick,
            out var automatic);
        CollectionAssert.AreEqual(new[] {5000, 34567, 40000, 40001}, ports);
        CollectionAssert.AreEqual(new[] {40000, 40001}, automatic.ToArray());
        CollectionAssert.AreEqual(new[] {5000, 34567}, picked.Single().Reserved);

        // No automatic ports asked for: none picked, nothing to remember.
        picked.Clear();
        ports = AppHost.ComposeListeningPorts(RuntimeMode.WinForms,
            new AppOptions {ListeningPorts = [5000], AutoListeningPortCount = 0}, new EnvOptions(), 3, Pick,
            out automatic);
        CollectionAssert.AreEqual(new[] {5000}, ports);
        Assert.AreEqual(0, automatic.Count);
        Assert.AreEqual(0, picked.Count);

        // The host's default count when the user has not set one.
        ports = AppHost.ComposeListeningPorts(RuntimeMode.MacOS, new AppOptions(), new EnvOptions(), 3, Pick,
            out _);
        Assert.AreEqual(3, ports.Count);

        // Docker: exactly the variable's ports, never an automatic one.
        picked.Clear();
        var env = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {["API_LISTENING_PORTS"] = "8080,9090"})
            .Build()
            .Get<EnvOptions>(o => o.BindNonPublicProperties = true)!;
        ports = AppHost.ComposeListeningPorts(RuntimeMode.Docker, new AppOptions {AutoListeningPortCount = 3}, env,
            3, Pick, out automatic);
        CollectionAssert.AreEqual(new[] {8080, 9090}, ports);
        Assert.AreEqual(0, picked.Count);
        Assert.AreEqual(0, automatic.Count);
    }

    /// <summary>A port in the window nothing on this machine is using right now.</summary>
    private static int FreeWindowPort() => FreeWindowPorts(1)[0];

    /// <summary><paramref name="count"/> ports in the window nothing on this machine is using right now.</summary>
    private static int[] FreeWindowPorts(int count)
    {
        var found = new List<int>(count);

        // From the top of the window down, away from where a running copy of the app sits.
        for (var port = ListeningPortSelector.WindowEnd - 1;
             port >= ListeningPortSelector.WindowStart && found.Count < count;
             port--)
        {
            if (ListeningPortSelector.IsFree(port, IPAddress.Any) &&
                !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(c => c.LocalEndPoint.Port == port))
            {
                found.Add(port);
            }
        }

        return found.Count == count
            ? found.ToArray()
            : throw new IOException($"Only {found.Count} free port(s) in the window to test with.");
    }
}
