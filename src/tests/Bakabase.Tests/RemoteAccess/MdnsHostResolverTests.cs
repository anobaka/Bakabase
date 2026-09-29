using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Resolving a <c>.local</c> name over mDNS, past a proxy on this computer that answers the
/// system's lookups with its own addresses: what is asked, which answers are kept, and when
/// the system resolver is used instead.
/// </summary>
/// <remarks>
/// The network is a fake transport that answers with the packets a test builds — no multicast
/// leaves the machine. The packets are built by the same <see cref="MdnsMessage"/> the
/// responder uses.
/// </remarks>
[TestClass]
public class MdnsHostResolverTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress LinkLocal = IPAddress.Parse("fe80::1c2b:3d4e:5f60:7182");
    private static readonly IPAddress ProxyAddress = IPAddress.Parse("198.18.0.29");

    [TestMethod]
    [DataRow("jaxs-Mac-mini.local", true)]
    [DataRow("NAS.LOCAL.", true)]
    [DataRow("a.b.local", true)]
    [DataRow(".local", false)]
    [DataRow("local", false)]
    [DataRow("nas.localdomain", false)]
    [DataRow("nas.lan", false)]
    [DataRow("192.168.1.5", false)]
    public void Only_names_under_local_are_asked_over_mdns(string host, bool mdns)
    {
        Assert.AreEqual(mdns, MdnsHostResolver.IsMdnsName(host));
    }

    [TestMethod]
    [DataRow("jaxs-Mac-mini.local", "jaxs-mac-mini-bakabase.local")]
    [DataRow("DESK.local.", "desk-bakabase.local")]
    [DataRow("desk-bakabase.local", null, DisplayName = "Already Bakabase's own name")]
    [DataRow("a.b.local", null, DisplayName = "Not a machine name")]
    [DataRow("nas.lan", null)]
    public void Bakabase_advertises_the_machine_name_with_its_own_suffix(string host, string? expected)
    {
        Assert.AreEqual(expected, MdnsHostResolver.BakabaseNameOf(host));
    }

    [TestMethod]
    public async Task Asks_for_the_names_addresses_and_bakabases_own_name_on_that_machine_with_the_QU_bit()
    {
        var network = new FakeMdns();
        var resolver = new MdnsHostResolver(network, timeout: TimeSpan.FromMilliseconds(100));

        await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        var queries = network.Exchanges.Single();
        var questions = queries.Select(q =>
        {
            Assert.IsTrue(MdnsMessage.TryParseQuestions(q, out var parsed));
            // A one-shot query: an id of its own, and the unicast-response bit on its class.
            Assert.AreNotEqual(0, (q[0] << 8) | q[1]);
            Assert.AreEqual(0x80, q[^2] & 0x80);
            return parsed.Single();
        }).ToList();

        CollectionAssert.AreEquivalent(new[]
        {
            ("jaxs-Mac-mini.local.", MdnsMessage.TypeA),
            ("jaxs-Mac-mini.local.", MdnsMessage.TypeAaaa),
            ("jaxs-mac-mini-bakabase.local.", MdnsMessage.TypeA)
        }, questions.ToArray());
    }

    [TestMethod]
    public async Task A_names_IPv4_answer_comes_back_without_waiting_out_the_question()
    {
        var network = new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", Lan)], KeepListening = true};
        var resolver = new MdnsHostResolver(network, timeout: TimeSpan.FromSeconds(10),
            settle: TimeSpan.FromMilliseconds(50));

        var watch = Stopwatch.StartNew();
        var addresses = await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {Lan}, addresses.ToArray());
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task A_link_local_answer_takes_the_interface_it_came_in_on_as_its_scope()
    {
        var network = new FakeMdns
        {
            Answers =
            [
                Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, on: 7),
                // Where the interface is not known, nothing says which link the address is on.
                Answer("mac.local", IPAddress.Parse("fe80::99"), MdnsMessage.TypeAaaa)
            ]
        };
        var resolver = new MdnsHostResolver(network);

        var addresses = await resolver.ResolveAsync("mac.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {new IPAddress(LinkLocal.GetAddressBytes(), 7)}, addresses.ToArray());
        Assert.AreEqual(7, addresses.Single().ScopeId);
    }

    [TestMethod]
    public async Task A_proxys_loopback_and_other_names_answers_are_not_taken()
    {
        var network = new FakeMdns
        {
            Answers =
            [
                Answer("nas.local", ProxyAddress),
                Answer("nas.local", IPAddress.Loopback),
                Answer("nas.local", IPAddress.Parse("224.0.0.251")),
                Answer("other.local", Lan)
            ]
        };
        var resolver = new MdnsHostResolver(network);

        Assert.AreEqual(0, (await resolver.ResolveAsync("nas.local", CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task A_name_answering_only_IPv6_link_local_is_reached_at_bakabases_own_name_on_it_first()
    {
        // What the Mac in the field report did: its own name answered fe80:: only, while the
        // Bakabase on it answered its own name with IPv4 — which is what a Bakabase listens on.
        var network = new FakeMdns
        {
            Answers =
            [
                Answer("jaxs-Mac-mini.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4),
                Answer("jaxs-mac-mini-bakabase.local", ProxyAddress, on: 4),
                Answer("jaxs-mac-mini-bakabase.local", Lan, on: 4),
                Answer("jaxs-mac-mini-bakabase.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)
            ]
        };
        var resolver = new MdnsHostResolver(network);

        var addresses = await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {Lan, new IPAddress(LinkLocal.GetAddressBytes(), 4)}, addresses.ToArray());

        // Its responder answers once a second at most: asked by that name next, the answer
        // already heard stands.
        CollectionAssert.AreEqual(new[] {Lan},
            (await resolver.ResolveAsync("jaxs-mac-mini-bakabase.local", CancellationToken.None)).ToArray());
        Assert.AreEqual(1, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task A_name_with_an_IPv4_answer_of_its_own_is_not_reached_elsewhere()
    {
        var other = IPAddress.Parse("192.168.1.99");
        var network = new FakeMdns
        {
            Answers = [Answer("desk.local", Lan), Answer("desk-bakabase.local", other)]
        };
        var resolver = new MdnsHostResolver(network);

        CollectionAssert.AreEqual(new[] {Lan},
            (await resolver.ResolveAsync("desk.local", CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task A_goodbye_takes_the_address_back()
    {
        var network = new FakeMdns
        {
            Answers = [Answer("nas.local", Lan), Answer("nas.local", Lan, ttl: 0)]
        };
        var resolver = new MdnsHostResolver(network);

        Assert.AreEqual(0, (await resolver.ResolveAsync("nas.local", CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task Nothing_answering_ends_with_nothing_once_the_question_times_out()
    {
        var network = new FakeMdns {KeepListening = true};
        var resolver = new MdnsHostResolver(network, timeout: TimeSpan.FromMilliseconds(200));

        var watch = Stopwatch.StartNew();
        var addresses = await resolver.ResolveAsync("nas.local", CancellationToken.None);

        Assert.AreEqual(0, addresses.Count);
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(150), $"took {watch.Elapsed}");
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task An_answer_is_kept_for_a_while_and_asked_again_after()
    {
        var clock = new RelayNavigationTokensTests.ManualClock();
        var network = new FakeMdns {Answers = [Answer("nas.local", Lan)]};
        var resolver = new MdnsHostResolver(network, clock);

        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        clock.Advance(MdnsHostResolver.AnswerLifetime - TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync("NAS.local.", CancellationToken.None);

        Assert.AreEqual(1, network.Exchanges.Count);

        clock.Advance(TimeSpan.FromSeconds(2));
        await resolver.ResolveAsync("nas.local", CancellationToken.None);

        Assert.AreEqual(2, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task No_answer_is_kept_for_less_long()
    {
        var clock = new RelayNavigationTokensTests.ManualClock();
        var network = new FakeMdns();
        var resolver = new MdnsHostResolver(network, clock);

        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        Assert.AreEqual(1, network.Exchanges.Count);

        clock.Advance(MdnsHostResolver.SilenceLifetime + TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        Assert.AreEqual(2, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task Lookups_of_one_name_at_once_share_one_question()
    {
        var network = new FakeMdns
        {
            Answers = [Answer("nas.local", Lan)],
            AnswerAfter = TimeSpan.FromMilliseconds(200)
        };
        var resolver = new MdnsHostResolver(network);

        var all = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => resolver.ResolveAsync("nas.local", CancellationToken.None)));

        Assert.AreEqual(1, network.Exchanges.Count);
        Assert.IsTrue(all.All(a => a.SequenceEqual([Lan])));
    }

    [TestMethod]
    public async Task A_network_that_fails_is_no_answer_rather_than_an_error()
    {
        var resolver = new MdnsHostResolver(new FakeMdns {Fails = true});

        Assert.AreEqual(0, (await resolver.ResolveAsync("nas.local", CancellationToken.None)).Count);
    }

    [TestMethod]
    public void An_AAAA_record_is_read_back_as_its_address()
    {
        Assert.IsTrue(MdnsMessage.TryParseResponse(
            Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa).Datagram.Data, out var records));

        var record = records.Single();
        Assert.AreEqual(MdnsMessage.TypeAaaa, record.Type);
        Assert.AreEqual(LinkLocal, record.Address);
    }

    #region The system resolver beside it

    [TestMethod]
    public async Task A_local_name_mdns_answers_ignores_what_the_system_says()
    {
        // The system resolver is a proxy here: it answers every name with its own address.
        var mdns = new MdnsHostResolver(new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", Lan)]});
        var system = new FakeSystemResolver(ProxyAddress);
        var resolver = new LanHostResolver(mdns, system.ResolveAsync);

        CollectionAssert.AreEqual(new[] {Lan},
            await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None));
    }

    [TestMethod]
    public async Task A_local_name_nothing_on_the_LAN_answers_falls_back_to_the_system()
    {
        var mdns = new MdnsHostResolver(new FakeMdns(), timeout: TimeSpan.FromMilliseconds(100));
        var system = new FakeSystemResolver(Lan);
        var resolver = new LanHostResolver(mdns, system.ResolveAsync);

        CollectionAssert.AreEqual(new[] {Lan}, await resolver.ResolveAsync("nas.local", CancellationToken.None));
        Assert.AreEqual(1, system.Lookups);
    }

    [TestMethod]
    public async Task Any_other_name_is_the_systems_alone()
    {
        var network = new FakeMdns {Answers = [Answer("PC1", IPAddress.Parse("192.168.1.99"))]};
        var system = new FakeSystemResolver(Lan);
        var resolver = new LanHostResolver(new MdnsHostResolver(network), system.ResolveAsync);

        CollectionAssert.AreEqual(new[] {Lan}, await resolver.ResolveAsync("PC1", CancellationToken.None));
        Assert.AreEqual(0, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task A_proxy_answering_a_local_name_nothing_on_the_LAN_answers_is_said_to_be_one()
    {
        // mDNS finds nothing, the system resolver answers a proxy's address: the connector
        // refuses it as that, and dials nothing.
        var mdns = new MdnsHostResolver(new FakeMdns(), timeout: TimeSpan.FromMilliseconds(100));
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var dialled = 0;
        var connector = new DualStackConnector(resolver.ResolveAsync, (_, _) =>
        {
            Interlocked.Increment(ref dialled);
            return new ValueTask<Stream>(new MemoryStream());
        });

        var refused = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint("jaxs-Mac-mini.local", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual(ProxyAddress, refused.Address);
        Assert.AreEqual(0, dialled);
    }

    [TestMethod]
    public async Task A_local_name_mdns_answers_is_connected_to_there_past_the_proxy()
    {
        var mdns = new MdnsHostResolver(new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", Lan)]});
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = new DualStackConnector(resolver.ResolveAsync, (to, _) =>
        {
            dialled.Enqueue(to);
            return new ValueTask<Stream>(new MemoryStream());
        });

        var (stream, reached) = await connector.ConnectReachingAsync(new DnsEndPoint("jaxs-Mac-mini.local", 34567),
            null, CancellationToken.None);
        await using var _ = stream;

        Assert.AreEqual(new IPEndPoint(Lan, 34567), reached);
        CollectionAssert.AreEqual(new[] {reached}, dialled.ToArray());
    }

    #endregion

    /// <summary>A responder's answer giving <paramref name="name"/> <paramref name="address"/>, arriving on interface <paramref name="on"/>.</summary>
    private static (MdnsDatagram Datagram, int Interface) Answer(string name, IPAddress address,
        ushort type = MdnsMessage.TypeA, uint ttl = 120, int on = 0) =>
        (new MdnsDatagram(MdnsMessage.BuildResponse([
            new MdnsMessage.Record(name, type, true, ttl, MdnsMessage.ARdata(address))
        ]), 0), on);

    private sealed class FakeSystemResolver(params IPAddress[] answer)
    {
        private int _lookups;

        public int Lookups => Volatile.Read(ref _lookups);

        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            Interlocked.Increment(ref _lookups);
            return Task.FromResult(answer);
        }
    }

    /// <summary>
    /// Records what each question sent and answers it with <see cref="Answers"/>, each arriving
    /// on the interface its entry names; then says nothing more — ending the exchange, or, with
    /// <see cref="KeepListening"/>, waiting as the real network would.
    /// </summary>
    private sealed class FakeMdns : IMdnsQueryTransport
    {
        private readonly ConcurrentQueue<IReadOnlyList<byte[]>> _exchanges = new();

        public List<(MdnsDatagram Datagram, int Interface)> Answers { get; init; } = [];
        public bool KeepListening { get; init; }
        public TimeSpan AnswerAfter { get; init; }
        public bool Fails { get; init; }

        public List<IReadOnlyList<byte[]>> Exchanges => _exchanges.ToList();

        public async IAsyncEnumerable<MdnsDatagram> ExchangeAsync(IReadOnlyList<byte[]> queries,
            [EnumeratorCancellation] CancellationToken ct)
        {
            _exchanges.Enqueue(queries);

            if (Fails)
            {
                throw new System.Net.Sockets.SocketException((int) System.Net.Sockets.SocketError.NetworkDown);
            }

            if (AnswerAfter > TimeSpan.Zero)
            {
                await Task.Delay(AnswerAfter, ct);
            }

            foreach (var (datagram, index) in Answers)
            {
                yield return datagram with {InterfaceIndex = index};
            }

            if (KeepListening)
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
        }
    }
}
