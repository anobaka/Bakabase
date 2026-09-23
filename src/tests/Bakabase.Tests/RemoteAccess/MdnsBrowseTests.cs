using System;
using System.Linq;
using System.Net;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The browsing half of mDNS discovery, driven with the packets the responder
/// actually builds.
/// </summary>
/// <remarks>
/// Both halves live in this repository, which is what makes it worth testing them
/// against each other rather than against a fixture: a change to what the server
/// advertises that the client cannot read is exactly the failure neither side's own
/// tests would show.
/// </remarks>
[TestClass]
public class MdnsBrowseTests
{
    private static readonly RemoteAccessServerDescriptor Descriptor =
        new("abc123", "Desk PC", 34567, "2.4.0-beta", 1);

    private static readonly IPAddress[] Addresses = [IPAddress.Parse("192.168.1.5")];

    private static byte[] Announcement(RemoteAccessServerDescriptor descriptor, bool goodbye = false,
        IPAddress[]? addresses = null) =>
        MdnsMessage.BuildResponse(new MdnsAdvertisement(descriptor)
            .BuildRecords(addresses ?? Addresses, goodbye));

    [TestMethod]
    public void What_the_responder_announces_is_read_back_as_a_server()
    {
        Assert.IsTrue(MdnsMessage.TryParseResponse(Announcement(Descriptor), out var records));

        var collector = new MdnsBrowser.InstanceCollector();
        collector.Add(records);

        var server = collector.Build().Single();

        Assert.AreEqual("abc123", server.ServerId);
        Assert.AreEqual("Desk PC", server.ServerName);
        Assert.AreEqual("2.4.0-beta", server.AppVersion);
        Assert.AreEqual(1, server.ProtocolVersion);

        // The port comes from the SRV record and the host from the A record it points
        // at — the two have to be joined through the advertisement's own hostname, and
        // getting that wrong produces an address nothing answers on.
        Assert.AreEqual("http://192.168.1.5:34567", server.BaseAddress);
        Assert.IsFalse(server.IsThisMachine);
    }

    [TestMethod]
    public void Records_that_arrive_in_separate_packets_still_make_one_server()
    {
        // Which is the normal case, not an edge one: responders answer a PTR question
        // with what they have to hand and send the rest afterwards.
        var all = MdnsMessage.TryParseResponse(Announcement(Descriptor), out var records)
            ? records
            : throw new InvalidOperationException("the announcement did not parse");

        var collector = new MdnsBrowser.InstanceCollector();

        foreach (var record in all)
        {
            collector.Add([record]);
        }

        Assert.AreEqual(1, collector.Build().Count);
    }

    [TestMethod]
    public void An_instance_with_no_address_is_not_offered()
    {
        // A server that published its name and port but no reachable address would
        // produce a row that cannot be connected to, which is worse than no row.
        Assert.IsTrue(MdnsMessage.TryParseResponse(Announcement(Descriptor, addresses: []), out var records));

        var collector = new MdnsBrowser.InstanceCollector();
        collector.Add(records);

        Assert.AreEqual(0, collector.Build().Count);
    }

    [TestMethod]
    public void A_server_saying_goodbye_is_dropped()
    {
        var collector = new MdnsBrowser.InstanceCollector();

        Assert.IsTrue(MdnsMessage.TryParseResponse(Announcement(Descriptor), out var hello));
        collector.Add(hello);
        Assert.AreEqual(1, collector.Build().Count);

        // TTL 0 is a server shutting down. Listing it afterwards offers an address that
        // has already gone.
        Assert.IsTrue(MdnsMessage.TryParseResponse(Announcement(Descriptor, goodbye: true), out var farewell));
        collector.Add(farewell);

        Assert.AreEqual(0, collector.Build().Count);
    }

    [TestMethod]
    public void A_server_on_this_machine_is_recognised()
    {
        Assert.IsTrue(MdnsMessage.TryParseResponse(
            Announcement(Descriptor, addresses: [IPAddress.Loopback]), out var records));

        var collector = new MdnsBrowser.InstanceCollector();
        collector.Add(records);

        Assert.IsTrue(collector.Build().Single().IsThisMachine);
    }

    [TestMethod]
    public void A_loopback_address_loses_to_one_that_means_something_here()
    {
        // Read from another machine, 127.0.0.1 points at the reader. A server that
        // publishes it alongside a real address must be reached at the real one — and
        // the order the records happen to arrive in is not a reason to pick either.
        foreach (var addresses in new[]
                 {
                     new[] {IPAddress.Loopback, IPAddress.Parse("192.168.1.5")},
                     [IPAddress.Parse("192.168.1.5"), IPAddress.Loopback]
                 })
        {
            Assert.IsTrue(MdnsMessage.TryParseResponse(Announcement(Descriptor, addresses: addresses),
                out var records));

            var collector = new MdnsBrowser.InstanceCollector();
            collector.Add(records);

            Assert.AreEqual("http://192.168.1.5:34567", collector.Build().Single().BaseAddress);
        }
    }

    [TestMethod]
    public void A_query_is_not_read_as_a_response()
    {
        // Both arrive on the same socket, and reading one as the other is how a browser
        // ends up listing itself.
        var query = MdnsMessage.BuildQuery(RemoteAccessProtocol.MdnsServiceType, MdnsMessage.TypePtr);

        Assert.IsFalse(MdnsMessage.TryParseResponse(query, out _));
        Assert.IsTrue(MdnsMessage.TryParseQuestions(query, out var questions));
        Assert.AreEqual(RemoteAccessProtocol.MdnsServiceType, questions.Single().Name);
    }

    [TestMethod]
    public void Anything_else_on_the_network_parses_to_nothing_rather_than_throwing()
    {
        // Every printer, speaker and phone on the network answers on this port.
        Assert.IsFalse(MdnsMessage.TryParseResponse([], out _));
        Assert.IsFalse(MdnsMessage.TryParseResponse([1, 2, 3], out _));
        Assert.IsFalse(MdnsMessage.TryParseResponse(new byte[64], out _));

        // A well-formed response for somebody else's service: parses, says nothing.
        var other = MdnsMessage.BuildResponse([
            new MdnsMessage.Record("_printer._tcp.local.", MdnsMessage.TypePtr, false, 120,
                MdnsMessage.PtrRdata("Office._printer._tcp.local."))
        ]);

        Assert.IsTrue(MdnsMessage.TryParseResponse(other, out var records));

        var collector = new MdnsBrowser.InstanceCollector();
        collector.Add(records);

        Assert.AreEqual(0, collector.Build().Count);
    }

    [TestMethod]
    public void A_truncated_packet_keeps_what_was_readable()
    {
        // Sooner than dropping the whole datagram: a browse that discards everything
        // because the last record was cut off finds nothing on a busy network.
        var full = Announcement(Descriptor);
        var cut = full.AsSpan(0, full.Length - 3).ToArray();

        Assert.IsTrue(MdnsMessage.TryParseResponse(cut, out var records));
        Assert.IsTrue(records.Count > 0);
    }

    [TestMethod]
    public void The_two_channels_report_a_server_once()
    {
        var probed = new DiscoveredServer("abc123", "Desk PC", "http://192.168.1.5:34567", "2.4.0", 1, false);
        var browsed = new DiscoveredServer("abc123", "Desk PC", "http://10.0.0.2:34567", "2.4.0", 1, false);

        var merged = ServerDiscovery.Merge([probed], [browsed]).Single();

        // The probe's address won: it is the one a datagram actually came back over,
        // while the advertised one can belong to an interface this network cannot reach.
        Assert.AreEqual("http://192.168.1.5:34567", merged.BaseAddress);
    }

    [TestMethod]
    public void A_server_on_this_machine_outranks_the_same_server_found_over_the_network()
    {
        var routed = new DiscoveredServer("abc123", "Desk PC", "http://192.168.1.5:34567", "2.4.0", 1, false);
        var local = new DiscoveredServer("abc123", "Desk PC", "http://127.0.0.1:34567", "2.4.0", 1, true);

        // Both reach it, and only one of them can tell the user it is their own computer.
        Assert.IsTrue(ServerDiscovery.Merge([routed], [local]).Single().IsThisMachine);
        Assert.IsTrue(ServerDiscovery.Merge([local], [routed]).Single().IsThisMachine);
    }
}
