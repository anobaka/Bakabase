using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// What the client makes of a datagram, and where it sends one.
/// </summary>
/// <remarks>
/// The socket work itself needs a network to mean anything, so what is checked here is
/// the part that is wrong on a developer's machine as readily as on a user's: which
/// addresses get probed, and how a reply becomes a usable server address.
/// </remarks>
[TestClass]
public class UdpProbeClientTests
{
    private static UdpReceiveResult Reply(RemoteAccessServerDescriptor descriptor, string from) =>
        new(DiscoveryProtocol.BuildProbeResponse(descriptor), new IPEndPoint(IPAddress.Parse(from), 33333));

    [TestMethod]
    public void The_address_comes_from_the_sender_not_from_what_the_server_said()
    {
        // A server with several interfaces cannot know which of its addresses this
        // machine can route to. The datagram that arrived came back over one that works,
        // so that is the only address worth keeping.
        var server = UdpProbeClient.Interpret(
            Reply(new RemoteAccessServerDescriptor("abc", "My-PC", 34567, "2.4.0", 1), "192.168.1.5"));

        Assert.IsNotNull(server);
        Assert.AreEqual("http://192.168.1.5:34567", server!.BaseAddress);
        Assert.AreEqual("My-PC", server.ServerName);
        Assert.IsFalse(server.IsThisMachine);
    }

    [TestMethod]
    public void A_reply_from_loopback_is_marked_as_this_computer()
    {
        // The most common thing a fresh client connects to is the all-in-one on the same
        // machine, and "connect to my own computer" is an odd thing to have to type an
        // address for.
        var server = UdpProbeClient.Interpret(
            Reply(new RemoteAccessServerDescriptor("abc", "My-PC", 34567, "2.4.0", 1), "127.0.0.1"));

        Assert.IsTrue(server!.IsThisMachine);
    }

    [TestMethod]
    public void An_ipv6_sender_is_bracketed_so_the_address_is_a_usable_url()
    {
        var server = UdpProbeClient.Interpret(
            Reply(new RemoteAccessServerDescriptor("abc", "My-PC", 34567, "2.4.0", 1), "fe80::1"));

        Assert.AreEqual("http://[fe80::1]:34567", server!.BaseAddress);
    }

    [TestMethod]
    public void Something_else_on_the_network_is_ignored()
    {
        Assert.IsNull(UdpProbeClient.Interpret(
            new UdpReceiveResult(Encoding.UTF8.GetBytes("SSDP/1.0 whatever"),
                new IPEndPoint(IPAddress.Parse("192.168.1.9"), 1900))));
    }

    // ---- where the probe goes ----

    [TestMethod]
    public void Loopback_is_always_probed()
    {
        // A broadcast does not reach a server on this same machine, and that is the
        // single most likely thing a new client is looking for.
        CollectionAssert.Contains(UdpProbeClient.BroadcastAddresses().ToArray(), IPAddress.Loopback);
    }

    [TestMethod]
    public void The_global_broadcast_is_not_relied_on_alone()
    {
        // A host with several interfaces sends 255.255.255.255 out of exactly one,
        // chosen by the routing table — on a machine with a VPN or a virtual switch,
        // regularly the wrong one.
        var addresses = UdpProbeClient.BroadcastAddresses();

        CollectionAssert.Contains(addresses.ToArray(), IPAddress.Broadcast);
        Assert.AreEqual(addresses.Count, addresses.Distinct().Count(), "the same address is probed twice");
    }

    [TestMethod]
    public void A_directed_broadcast_fills_the_host_bits()
    {
        Assert.AreEqual(IPAddress.Parse("192.168.1.255"),
            UdpProbeClient.DirectedBroadcast(IPAddress.Parse("192.168.1.5"), IPAddress.Parse("255.255.255.0")));

        Assert.AreEqual(IPAddress.Parse("10.255.255.255"),
            UdpProbeClient.DirectedBroadcast(IPAddress.Parse("10.1.2.3"), IPAddress.Parse("255.0.0.0")));
    }

    [TestMethod]
    public void Loopback_and_maskless_interfaces_produce_no_directed_broadcast()
    {
        // Broadcasting to 127.255.255.255 reaches nothing; loopback is probed directly
        // instead.
        Assert.IsNull(UdpProbeClient.DirectedBroadcast(IPAddress.Loopback, IPAddress.Parse("255.0.0.0")));
        Assert.IsNull(UdpProbeClient.DirectedBroadcast(IPAddress.Parse("192.168.1.5"), null));
        Assert.IsNull(UdpProbeClient.DirectedBroadcast(IPAddress.Parse("fe80::1"), IPAddress.Parse("255.255.255.0")));
    }
}
