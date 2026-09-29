using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Microsoft.Extensions.Logging.Abstractions;
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
/// responder uses. This machine's addresses are the test's too (none, unless it says), so
/// what the machine running the test holds changes nothing.
/// </remarks>
[TestClass]
public class MdnsHostResolverTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress LinkLocal = IPAddress.Parse("fe80::1c2b:3d4e:5f60:7182");
    private static readonly IPAddress ProxyAddress = IPAddress.Parse("198.18.0.29");
    private static readonly IPAddress HostOnly = IPAddress.Parse("192.168.56.1");

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
        var resolver = Resolver(network, timeout: TimeSpan.FromMilliseconds(100));

        await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        CollectionAssert.AreEquivalent(new[]
        {
            ("jaxs-Mac-mini.local.", MdnsMessage.TypeA),
            ("jaxs-Mac-mini.local.", MdnsMessage.TypeAaaa),
            ("jaxs-mac-mini-bakabase.local.", MdnsMessage.TypeA)
        }, Questions(network.Exchanges.Single()[0]));
    }

    [TestMethod]
    public async Task A_names_IPv4_answer_comes_back_without_waiting_out_the_question()
    {
        var network = new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", Lan)], KeepListening = true};
        var resolver = Resolver(network, timeout: TimeSpan.FromSeconds(10), settle: TimeSpan.FromMilliseconds(50));

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
        var resolver = Resolver(network);

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
        var resolver = Resolver(network);

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
        var resolver = Resolver(network);

        var addresses = await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {Lan, new IPAddress(LinkLocal.GetAddressBytes(), 4)}, addresses.ToArray());

        // Its responder answers once a second at most: asked by that name next, the answer
        // already heard stands.
        CollectionAssert.AreEqual(new[] {Lan},
            (await resolver.ResolveAsync("jaxs-mac-mini-bakabase.local", CancellationToken.None)).ToArray());
        Assert.AreEqual(1, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task An_IPv6_answer_does_not_end_the_question_before_bakabases_IPv4_one_comes()
    {
        // The field report's Mac on Wi-Fi: its system responder answers the AAAA question
        // straight back at once, while the Bakabase on it only multicasts — which an access
        // point holds back until its next beacon. With the default timings, as in the app.
        var network = new FakeMdns
        {
            Answers =
            [
                Answer("jaxs-Mac-mini.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4, after: 5),
                Answer("jaxs-mac-mini-bakabase.local", Lan, on: 4, after: 250)
            ],
            KeepListening = true
        };
        var resolver = Resolver(network);

        var watch = Stopwatch.StartNew();
        var addresses = await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {Lan, new IPAddress(LinkLocal.GetAddressBytes(), 4)}, addresses.ToArray());
        // It ended once the IPv4 answer had settled, not at the timeout.
        Assert.IsTrue(watch.Elapsed < MdnsHostResolver.DefaultTimeout, $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task A_name_answering_IPv6_alone_waits_the_question_out_for_an_IPv4_answer()
    {
        var network = new FakeMdns
        {
            Answers = [Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)],
            KeepListening = true
        };
        var resolver = Resolver(network, timeout: TimeSpan.FromMilliseconds(400), settle: TimeSpan.FromMilliseconds(20));

        var watch = Stopwatch.StartNew();
        var addresses = await resolver.ResolveAsync("mac.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {new IPAddress(LinkLocal.GetAddressBytes(), 4)}, addresses.ToArray());
        Assert.IsTrue(watch.Elapsed >= TimeSpan.FromMilliseconds(350), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task A_question_lost_on_the_way_is_asked_again()
    {
        // Multicast is not retried by the link: the first send reaches nobody, the second does.
        var network = new FakeMdns {Answers = [Answer("nas.local", Lan, toSend: 1)], KeepListening = true};
        var resolver = Resolver(network);

        var watch = Stopwatch.StartNew();
        var addresses = await resolver.ResolveAsync("nas.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {Lan}, addresses.ToArray());
        Assert.IsTrue(watch.Elapsed < MdnsHostResolver.DefaultTimeout, $"took {watch.Elapsed}");

        var sends = network.Exchanges.Single();
        Assert.AreEqual(2, sends.Count, "answered after the second send, nothing left to ask a third time");
        CollectionAssert.AreEquivalent(Questions(sends[0]), Questions(sends[1]));
    }

    [TestMethod]
    public async Task Only_what_is_still_unanswered_is_asked_again()
    {
        var network = new FakeMdns
        {
            Answers = [Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)],
            KeepListening = true
        };
        var resolver = Resolver(network, timeout: TimeSpan.FromMilliseconds(800));

        await resolver.ResolveAsync("mac.local", CancellationToken.None);

        var sends = network.Exchanges.Single();
        Assert.AreEqual(1 + MdnsHostResolver.ResendAt.Count, sends.Count);
        Assert.AreEqual(3, sends[0].Count);

        foreach (var again in sends.Skip(1))
        {
            CollectionAssert.AreEquivalent(new[]
            {
                ("mac.local.", MdnsMessage.TypeA),
                ("mac-bakabase.local.", MdnsMessage.TypeA)
            }, Questions(again));
        }
    }

    [TestMethod]
    public async Task Nothing_is_asked_again_once_an_IPv4_address_is_in()
    {
        var network = new FakeMdns {Answers = [Answer("nas.local", Lan)], KeepListening = true};
        var resolver = Resolver(network, settle: TimeSpan.FromMilliseconds(400));

        await resolver.ResolveAsync("nas.local", CancellationToken.None);

        Assert.AreEqual(1, network.Exchanges.Single().Count);
    }

    [TestMethod]
    public async Task A_name_with_an_IPv4_answer_of_its_own_is_not_reached_elsewhere()
    {
        var other = IPAddress.Parse("192.168.1.99");
        var network = new FakeMdns
        {
            Answers = [Answer("desk.local", Lan), Answer("desk-bakabase.local", other)]
        };
        var resolver = Resolver(network);

        CollectionAssert.AreEqual(new[] {Lan},
            (await resolver.ResolveAsync("desk.local", CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task An_address_this_machine_holds_too_is_left_out()
    {
        // Another machine advertises every IPv4 address it has, a virtual machine host-only
        // one this machine has too among them: dialled, it would lead back here.
        var network = new FakeMdns {Answers = [Answer("nas.local", HostOnly), Answer("nas.local", Lan)]};
        var resolver = Resolver(network, held: [HostOnly]);

        CollectionAssert.AreEqual(new[] {Lan}, (await resolver.ResolveAsync("nas.local", CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task An_address_this_machine_holds_too_is_left_out_of_bakabases_own_name_as_well()
    {
        var network = new FakeMdns
        {
            Answers =
            [
                Answer("mac.local", HostOnly),
                Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4),
                Answer("mac-bakabase.local", HostOnly),
                Answer("mac-bakabase.local", Lan)
            ]
        };
        var resolver = Resolver(network, held: [HostOnly]);

        // Its own name's IPv4 address is this machine's, so it has none: Bakabase's name's goes first.
        CollectionAssert.AreEqual(new[] {Lan, new IPAddress(LinkLocal.GetAddressBytes(), 4)},
            (await resolver.ResolveAsync("mac.local", CancellationToken.None)).ToArray());
        CollectionAssert.AreEqual(new[] {Lan},
            (await resolver.ResolveAsync("mac-bakabase.local", CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task A_name_answering_with_this_machines_addresses_alone_keeps_them()
    {
        // This machine's own name: every address is here, and here is where it leads.
        var network = new FakeMdns {Answers = [Answer("desk.local", HostOnly), Answer("desk.local", Lan)]};
        var resolver = Resolver(network, held: [HostOnly, Lan]);

        CollectionAssert.AreEqual(new[] {HostOnly, Lan},
            (await resolver.ResolveAsync("desk.local", CancellationToken.None)).ToArray());
    }

    [TestMethod]
    public async Task A_goodbye_takes_the_address_back()
    {
        var network = new FakeMdns
        {
            Answers = [Answer("nas.local", Lan), Answer("nas.local", Lan, ttl: 0)]
        };
        var resolver = Resolver(network);

        Assert.AreEqual(0, (await resolver.ResolveAsync("nas.local", CancellationToken.None)).Count);
    }

    [TestMethod]
    public async Task Nothing_answering_ends_with_nothing_once_the_question_times_out()
    {
        var network = new FakeMdns {KeepListening = true};
        var resolver = Resolver(network, timeout: TimeSpan.FromMilliseconds(200));

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
        var resolver = Resolver(network, clock);

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
        var resolver = Resolver(network, clock);

        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        Assert.AreEqual(1, network.Exchanges.Count);

        clock.Advance(MdnsHostResolver.SilenceLifetime + TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync("nas.local", CancellationToken.None);
        Assert.AreEqual(2, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task An_answer_still_without_the_IPv4_address_it_asked_bakabases_name_for_is_kept_as_briefly_as_silence()
    {
        // Bakabase's answer lost on the way: kept for the answer's own lifetime, a proxy on this
        // computer would be said to be in the way for that long, though asking again would get it.
        var clock = new RelayNavigationTokensTests.ManualClock();
        var network = new FakeMdns {Answers = [Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, ttl: 120, on: 4)]};
        var resolver = Resolver(network, clock);

        await resolver.ResolveAsync("mac.local", CancellationToken.None);
        clock.Advance(MdnsHostResolver.SilenceLifetime - TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync("mac.local", CancellationToken.None);

        Assert.AreEqual(1, network.Exchanges.Count);

        clock.Advance(TimeSpan.FromSeconds(2));
        await resolver.ResolveAsync("mac.local", CancellationToken.None);

        Assert.AreEqual(2, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task An_IPv6_answer_with_nothing_more_to_ask_is_kept_as_long_as_any_answer()
    {
        // Not a machine name, so there is no Bakabase name to wait for.
        var clock = new RelayNavigationTokensTests.ManualClock();
        var network = new FakeMdns {Answers = [Answer("a.b.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)]};
        var resolver = Resolver(network, clock);

        await resolver.ResolveAsync("a.b.local", CancellationToken.None);
        clock.Advance(MdnsHostResolver.AnswerLifetime - TimeSpan.FromSeconds(1));
        await resolver.ResolveAsync("a.b.local", CancellationToken.None);

        Assert.AreEqual(1, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task Bakabases_answer_heard_again_replaces_its_own_stale_one()
    {
        // Its responder multicasts at most once a second: asked by that name right after, only
        // what the machine's question heard can answer — every time, not only the first.
        var clock = new RelayNavigationTokensTests.ManualClock();
        var network = new FakeMdns
        {
            Answers =
            [
                Answer("jaxs-Mac-mini.local", LinkLocal, MdnsMessage.TypeAaaa, ttl: 10, on: 4),
                Answer("jaxs-mac-mini-bakabase.local", Lan, ttl: 10, on: 4)
            ]
        };
        var resolver = Resolver(network, clock);

        await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(11));
        await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None);

        CollectionAssert.AreEqual(new[] {Lan},
            (await resolver.ResolveAsync("jaxs-mac-mini-bakabase.local", CancellationToken.None)).ToArray());
        Assert.AreEqual(2, network.Exchanges.Count);
    }

    [TestMethod]
    public async Task Bakabases_own_responder_answers_the_question_straight_back()
    {
        // Every question goes through a real responder: the IPv4 answer does not wait for, or
        // depend on, a multicast one — which the responder sends at most once a second, and
        // which Wi-Fi may lose.
        using var responder = new MdnsResponder(
            new MdnsAdvertisement(new RemoteAccessServerDescriptor("abc123", "jaxs-Mac-mini", 34567, "2.4.0", 1)),
            () => [Lan], NullLogger.Instance, _ => true);
        var network = new ResponderNetwork(responder,
            Answer("jaxs-Mac-mini.local", LinkLocal, MdnsMessage.TypeAaaa).Datagram with {InterfaceIndex = 4});
        var resolver = Resolver(network, timeout: TimeSpan.FromSeconds(5), settle: TimeSpan.FromMilliseconds(20));

        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            var addresses = await resolver.ResolveAsync($"jaxs-Mac-mini.local", CancellationToken.None);

            Assert.AreEqual(Lan, addresses[0], $"lookup {i}");
            Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(2), $"lookup {i} took {watch.Elapsed}");

            // Asked afresh each time, well within the second the multicast answers are limited to.
            resolver = Resolver(network, timeout: TimeSpan.FromSeconds(5), settle: TimeSpan.FromMilliseconds(20));
        }

        Assert.IsTrue(network.Unicast >= 3, $"{network.Unicast} answers straight back");
    }

    [TestMethod]
    public async Task Lookups_of_one_name_at_once_share_one_question()
    {
        var network = new FakeMdns
        {
            Answers = [Answer("nas.local", Lan)],
            AnswerAfter = TimeSpan.FromMilliseconds(200)
        };
        var resolver = Resolver(network);

        var all = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => resolver.ResolveAsync("nas.local", CancellationToken.None)));

        Assert.AreEqual(1, network.Exchanges.Count);
        Assert.IsTrue(all.All(a => a.SequenceEqual([Lan])));
    }

    [TestMethod]
    public async Task A_network_that_fails_is_no_answer_rather_than_an_error()
    {
        var resolver = Resolver(new FakeMdns {Fails = true});

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
        var mdns = Resolver(new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", Lan)]});
        var system = new FakeSystemResolver(ProxyAddress);
        var resolver = new LanHostResolver(mdns, system.ResolveAsync);

        CollectionAssert.AreEqual(new[] {Lan},
            await resolver.ResolveAsync("jaxs-Mac-mini.local", CancellationToken.None));
    }

    [TestMethod]
    public async Task A_local_name_nothing_on_the_LAN_answers_falls_back_to_the_system()
    {
        var mdns = Resolver(new FakeMdns(), timeout: TimeSpan.FromMilliseconds(100));
        var system = new FakeSystemResolver(Lan);
        var resolver = new LanHostResolver(mdns, system.ResolveAsync);

        CollectionAssert.AreEqual(new[] {Lan}, await resolver.ResolveAsync("nas.local", CancellationToken.None));
        Assert.AreEqual(1, system.Lookups);
    }

    [TestMethod]
    [DataRow("nas.local")]
    [DataRow("fileserver.corp.local", DisplayName = "A domain's DNS name under .local")]
    public async Task A_local_name_the_system_knows_is_not_kept_waiting_for_mdns(string host)
    {
        // The hosts file, the router's or a domain's DNS knows the name; nothing on the LAN
        // answers over mDNS. With the app's own timeout, which a lookup must not wait out.
        var network = new FakeMdns {KeepListening = true};
        var mdns = Resolver(network);
        var system = new FakeSystemResolver(IPAddress.Parse("192.168.1.5"));
        var resolver = new LanHostResolver(mdns, system.ResolveAsync);

        var watch = Stopwatch.StartNew();
        var addresses = await resolver.ResolveAsync(host, CancellationToken.None);

        CollectionAssert.AreEqual(new[] {IPAddress.Parse("192.168.1.5")}, addresses);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromMilliseconds(500), $"took {watch.Elapsed}");
        // The question still goes out, and its answer is kept for next time.
        Assert.IsTrue(SpinWait.SpinUntil(() => network.Exchanges.Count == 1, TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public async Task A_system_answer_without_IPv4_still_waits_for_mdns()
    {
        var mdns = Resolver(new FakeMdns {Answers = [Answer("mac.local", Lan, after: 50)], KeepListening = true});
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(IPAddress.Parse("fe80::5%3")).ResolveAsync);

        CollectionAssert.AreEqual(new[] {Lan}, await resolver.ResolveAsync("mac.local", CancellationToken.None));
    }

    [TestMethod]
    public async Task A_local_name_answering_IPv6_alone_keeps_the_systems_IPv4_address()
    {
        // mDNS answers the name with a link-local address only and nothing answers Bakabase's
        // name; the system (the hosts file, the router's DNS) knows an IPv4 address, later.
        var mdns = Resolver(new FakeMdns {Answers = [Answer("nas.local", IPAddress.Parse("fe80::1"), MdnsMessage.TypeAaaa, on: 2)]},
            timeout: TimeSpan.FromMilliseconds(100));
        var system = new FakeSystemResolver(IPAddress.Parse("192.168.1.5")) {Takes = TimeSpan.FromMilliseconds(300)};
        var resolver = new LanHostResolver(mdns, system.ResolveAsync);

        var addresses = await resolver.ResolveAsync("nas.local", CancellationToken.None);

        CollectionAssert.AreEquivalent(new[] {IPAddress.Parse("192.168.1.5"), IPAddress.Parse("fe80::1%2")}, addresses);
        CollectionAssert.AreEqual(new[] {IPAddress.Parse("192.168.1.5"), IPAddress.Parse("fe80::1%2")},
            DualStackConnector.Order(addresses).ToArray());
    }

    [TestMethod]
    public async Task A_proxy_answering_a_local_name_nothing_on_the_LAN_answers_is_said_to_be_one()
    {
        // mDNS finds nothing, the system resolver answers a proxy's address: the connector
        // refuses it as that, and dials nothing.
        var mdns = Resolver(new FakeMdns(), timeout: TimeSpan.FromMilliseconds(100));
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var dialled = 0;
        var connector = new DualStackConnector(resolver, (_, _) =>
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
    public async Task A_proxy_taking_over_a_name_that_answers_IPv6_alone_is_said_once_those_addresses_fail()
    {
        // The field report's Mac with Bakabase's own answer missed: mDNS gives the name's
        // link-local address, which a Bakabase server does not listen on, and the system gives
        // the proxy's. Tried there first, then said to be the proxy's doing.
        var mdns = Resolver(new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)]},
            timeout: TimeSpan.FromMilliseconds(100));
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var dialled = new ConcurrentQueue<IPEndPoint>();
        var connector = new DualStackConnector(resolver, (to, _) =>
        {
            dialled.Enqueue(to);
            return ValueTask.FromException<Stream>(new SocketException((int) SocketError.ConnectionRefused));
        });

        var refused = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint("jaxs-Mac-mini.local", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual(ProxyAddress, refused.Address);
        Assert.AreEqual(SocketError.ConnectionRefused, ((SocketException) refused.ConnectError!).SocketErrorCode);
        CollectionAssert.AreEqual(new[] {new IPEndPoint(new IPAddress(LinkLocal.GetAddressBytes(), 4), 34567)},
            dialled.ToArray());
    }

    [TestMethod]
    public async Task A_proxy_taking_over_a_name_that_answers_IPv6_alone_is_said_through_an_http_client_too()
    {
        var mdns = Resolver(new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)]},
            timeout: TimeSpan.FromMilliseconds(100));
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var connector = new DualStackConnector(resolver, (_, _) =>
            ValueTask.FromException<Stream>(new SocketException((int) SocketError.ConnectionRefused)));
        using var http = new HttpClient(new SocketsHttpHandler {UseProxy = false, ConnectCallback = connector.ConnectCallback});

        var failed = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            http.GetAsync("http://jaxs-Mac-mini.local:34567/remote-access/server-info"));

        Assert.AreEqual(ProxyAddress, ProxyFakeAddresses.Find(failed)?.Address);
    }

    [TestMethod]
    public async Task A_proxy_taking_over_a_name_whose_LAN_addresses_hang_is_said_once_they_have_had_their_time()
    {
        // A firewall in stealth mode drops the link-local attempt without a word. Left to the
        // caller — server switching gives no connect timeout of its own — its budget would end
        // it first, and the proxy would never be named: the connector bounds it itself.
        var mdns = Resolver(new FakeMdns {Answers = [Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)]},
            timeout: TimeSpan.FromMilliseconds(100));
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var connector = new DualStackConnector(resolver, async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new MemoryStream();
        });

        var watch = Stopwatch.StartNew();
        var refused = await Assert.ThrowsExactlyAsync<ProxyFakeAddressException>(() =>
            connector.ConnectAsync(new DnsEndPoint("mac.local", 34567), CancellationToken.None).AsTask());

        Assert.AreEqual(SocketError.TimedOut, ((SocketException) refused.ConnectError!).SocketErrorCode);
        Assert.IsTrue(watch.Elapsed < DualStackConnector.ProxiedLanConnectLimit + TimeSpan.FromSeconds(1),
            $"took {watch.Elapsed}");

        // Through an HTTP client as the console composes one: ten seconds to connect.
        using var http = new HttpClient(new SocketsHttpHandler
        {
            UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(10), ConnectCallback = connector.ConnectCallback
        });

        watch.Restart();
        var failed = await Assert.ThrowsExactlyAsync<HttpRequestException>(() =>
            http.GetAsync("http://mac.local:34567/remote-access/server-info"));

        Assert.AreEqual(ProxyAddress, ProxyFakeAddresses.Find(failed)?.Address);
        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
    }

    [TestMethod]
    public async Task A_proxy_taking_over_a_name_that_answers_IPv6_alone_still_connects_there_when_that_answers()
    {
        var mdns = Resolver(new FakeMdns {Answers = [Answer("mac.local", LinkLocal, MdnsMessage.TypeAaaa, on: 4)]},
            timeout: TimeSpan.FromMilliseconds(100));
        var resolver = new LanHostResolver(mdns, new FakeSystemResolver(ProxyAddress).ResolveAsync);
        var connector = new DualStackConnector(resolver, (_, _) => new ValueTask<Stream>(new MemoryStream()));

        var (stream, reached) = await connector.ConnectReachingAsync(new DnsEndPoint("mac.local", 34567), null,
            CancellationToken.None);
        await using var _ = stream;

        Assert.AreEqual(new IPEndPoint(new IPAddress(LinkLocal.GetAddressBytes(), 4), 34567), reached);
    }

    [TestMethod]
    public async Task A_local_name_mdns_answers_is_connected_to_there_past_the_proxy()
    {
        var mdns = Resolver(new FakeMdns {Answers = [Answer("jaxs-Mac-mini.local", Lan)]});
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

    /// <summary>A resolver over <paramref name="network"/> on a machine holding <paramref name="held"/> and nothing else.</summary>
    private static MdnsHostResolver Resolver(IMdnsQueryTransport network, TimeProvider? clock = null, TimeSpan? timeout = null,
        TimeSpan? settle = null, IPAddress[]? held = null) =>
        new(network, clock, timeout, settle, () => new ThisMachineAddresses(held ?? []));

    /// <summary>What one send asked.</summary>
    private static (string Name, ushort Type)[] Questions(IReadOnlyList<byte[]> sent) =>
        sent.Select(q =>
        {
            Assert.IsTrue(MdnsMessage.TryParseQuestions(q, out var parsed));
            // A one-shot query: an id of its own, and the unicast-response bit on its class.
            Assert.AreNotEqual(0, (q[0] << 8) | q[1]);
            Assert.AreEqual(0x80, q[^2] & 0x80);
            return parsed.Single();
        }).ToArray();

    /// <summary>
    /// A responder's answer giving <paramref name="name"/> <paramref name="address"/>, arriving on
    /// interface <paramref name="on"/> <paramref name="after"/> ms after send number
    /// <paramref name="toSend"/> (the first is 0; one never made is never answered).
    /// </summary>
    private static Reply Answer(string name, IPAddress address, ushort type = MdnsMessage.TypeA, uint ttl = 120,
        int on = 0, int after = 0, int toSend = 0) =>
        new(new MdnsDatagram(MdnsMessage.BuildResponse([
            new MdnsMessage.Record(name, type, true, ttl, MdnsMessage.ARdata(address))
        ]), 0), on, TimeSpan.FromMilliseconds(after), toSend);

    private sealed record Reply(MdnsDatagram Datagram, int Interface, TimeSpan After, int ToSend);

    /// <summary>
    /// A LAN where every question reaches <paramref name="responder"/> from a one-shot port, and
    /// only what it sends straight back to that port comes back; <paramref name="others"/> are
    /// what the other responders answer the first question with.
    /// </summary>
    private sealed class ResponderNetwork(MdnsResponder responder, params MdnsDatagram[] others) : IMdnsQueryTransport
    {
        private static readonly IPEndPoint Querier = new(IPAddress.Parse("192.168.1.30"), 50000);
        private int _unicast;

        /// <summary>How many answers came straight back.</summary>
        public int Unicast => Volatile.Read(ref _unicast);

        public async IAsyncEnumerable<MdnsDatagram> ExchangeAsync(IAsyncEnumerable<IReadOnlyList<byte[]>> questions,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var first = true;

            await foreach (var batch in questions.WithCancellation(ct))
            {
                if (first)
                {
                    first = false;

                    foreach (var other in others)
                    {
                        yield return other;
                    }
                }

                foreach (var query in batch)
                {
                    if (responder.Respond(query, Querier, Environment.TickCount64) is { } reply &&
                        reply.To.Equals(Querier))
                    {
                        Interlocked.Increment(ref _unicast);
                        yield return new MdnsDatagram(reply.Packet, 4);
                    }
                }
            }

            await Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }

    private sealed class FakeSystemResolver(params IPAddress[] answer)
    {
        private int _lookups;

        public int Lookups => Volatile.Read(ref _lookups);

        /// <summary>How long a lookup takes.</summary>
        public TimeSpan Takes { get; init; }

        public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct)
        {
            Interlocked.Increment(ref _lookups);

            if (Takes > TimeSpan.Zero)
            {
                await Task.Delay(Takes, ct);
            }

            return answer;
        }
    }

    /// <summary>
    /// Records what each question sent, send by send, and answers each send with the
    /// <see cref="Answers"/> for it; then says nothing more — ending the exchange once every
    /// answer is out (at once when there are none), or, with <see cref="KeepListening"/>,
    /// waiting as the real network would.
    /// </summary>
    private sealed class FakeMdns : IMdnsQueryTransport
    {
        private readonly ConcurrentQueue<ConcurrentQueue<IReadOnlyList<byte[]>>> _exchanges = new();

        public List<Reply> Answers { get; init; } = [];
        public bool KeepListening { get; init; }
        public TimeSpan AnswerAfter { get; init; }
        public bool Fails { get; init; }

        /// <summary>Each question's sends, in order.</summary>
        public List<List<IReadOnlyList<byte[]>>> Exchanges => _exchanges.Select(e => e.ToList()).ToList();

        public async IAsyncEnumerable<MdnsDatagram> ExchangeAsync(IAsyncEnumerable<IReadOnlyList<byte[]>> questions,
            [EnumeratorCancellation] CancellationToken ct)
        {
            var sends = new ConcurrentQueue<IReadOnlyList<byte[]>>();
            _exchanges.Enqueue(sends);

            if (Fails)
            {
                throw new SocketException((int) SocketError.NetworkDown);
            }

            var heard = Channel.CreateUnbounded<MdnsDatagram>();
            var due = Answers.Count;
            var delivered = 0;
            var firstSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task AnswerAsync(IEnumerable<Reply> replies)
            {
                var since = Stopwatch.StartNew();

                foreach (var reply in replies.OrderBy(r => r.After))
                {
                    var wait = AnswerAfter + reply.After - since.Elapsed;

                    try
                    {
                        if (wait > TimeSpan.Zero)
                        {
                            await Task.Delay(wait, ct);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    heard.Writer.TryWrite(reply.Datagram with {InterfaceIndex = reply.Interface});

                    if (Interlocked.Increment(ref delivered) == due && !KeepListening)
                    {
                        heard.Writer.TryComplete();
                    }
                }
            }

            _ = Task.Run(async () =>
            {
                var index = 0;

                try
                {
                    await foreach (var batch in questions.WithCancellation(ct))
                    {
                        sends.Enqueue(batch);
                        var number = index++;
                        var replies = Answers.Where(r => r.ToSend == number).ToList();

                        if (replies.Count > 0)
                        {
                            _ = AnswerAsync(replies);
                        }

                        firstSend.TrySetResult();
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }, CancellationToken.None);

            if (due == 0 && !KeepListening)
            {
                await firstSend.Task.WaitAsync(ct);
                heard.Writer.TryComplete();
            }

            await foreach (var datagram in heard.Reader.ReadAllAsync(ct))
            {
                yield return datagram;
            }
        }
    }
}
