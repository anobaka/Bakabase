using System.Net;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Which of this machine's addresses the devices page suggests another device types. A
/// private address alone is not enough: host-only and overlay adapters carry them too, and
/// only this machine can tell them from the network a router serves.
/// </summary>
[TestClass]
public class RemoteAccessAddressClassifierTests
{
    [TestMethod]
    // The home network: a private address whose interface has a default gateway.
    [DataRow("192.168.1.5", "en0", "", true, RemoteAccessAddressKind.Lan)]
    [DataRow("10.0.0.8", "Ethernet", "Realtek PCIe GbE Family Controller", true, RemoteAccessAddressKind.Lan)]
    // Where the platform cannot read the routing table, a private address is still the LAN.
    [DataRow("172.20.1.4", "eth0", "", null, RemoteAccessAddressKind.Lan)]
    // Host-only and overlay adapters with private addresses: named, or without a way out.
    [DataRow("10.211.55.2", "vnic0", "", false, RemoteAccessAddressKind.Virtual)]
    [DataRow("192.168.56.1", "Ethernet 2", "VirtualBox Host-Only Ethernet Adapter", false,
        RemoteAccessAddressKind.Virtual)]
    [DataRow("192.168.128.1", "bridge100", "", false, RemoteAccessAddressKind.Virtual)]
    [DataRow("10.147.17.3", "ZeroTier One [8056c2e21c000001]", "", false, RemoteAccessAddressKind.Vpn)]
    [DataRow("10.147.17.3", "feth2137", "", false, RemoteAccessAddressKind.Vpn)]
    [DataRow("10.8.0.2", "Ethernet 3", "TAP-Windows Adapter V9", false, RemoteAccessAddressKind.Vpn)]
    [DataRow("192.168.99.1", "en7", "", false, RemoteAccessAddressKind.Unknown)]
    // Ranges that say it outright, whatever the adapter is called.
    [DataRow("100.101.102.103", "utun4", "", false, RemoteAccessAddressKind.Vpn)]
    [DataRow("198.18.0.1", "utun6", "", true, RemoteAccessAddressKind.Virtual)]
    [DataRow("169.254.10.2", "en5", "", false, RemoteAccessAddressKind.LinkLocal)]
    [DataRow("8.8.4.4", "eth0", "", true, RemoteAccessAddressKind.Unknown)]
    public void An_address_is_classified_by_its_range_its_adapter_and_its_gateway(string address,
        string interfaceName, string description, bool? hasGateway, RemoteAccessAddressKind expected)
    {
        Assert.AreEqual(expected,
            RemoteAccessAddressClassifier.Classify(IPAddress.Parse(address), interfaceName, description,
                hasGateway));
    }

    [TestMethod]
    public void The_first_lan_address_with_a_gateway_is_recommended_before_one_listed_earlier()
    {
        var recommended = RemoteAccessAddressClassifier.Recommend(new List<(RemoteAccessAddressKind, bool?)>
        {
            (RemoteAccessAddressKind.Virtual, false),
            (RemoteAccessAddressKind.Lan, null),
            (RemoteAccessAddressKind.Lan, true),
            (RemoteAccessAddressKind.Lan, true)
        });

        Assert.AreEqual(2, recommended);
    }

    [TestMethod]
    public void Without_a_gateway_anywhere_the_first_lan_address_is_recommended_and_nothing_without_one()
    {
        Assert.AreEqual(1, RemoteAccessAddressClassifier.Recommend(new List<(RemoteAccessAddressKind, bool?)>
        {
            (RemoteAccessAddressKind.Vpn, true),
            (RemoteAccessAddressKind.Lan, null),
            (RemoteAccessAddressKind.Lan, null)
        }));
        Assert.IsNull(RemoteAccessAddressClassifier.Recommend(new List<(RemoteAccessAddressKind, bool?)>
        {
            (RemoteAccessAddressKind.Vpn, true),
            (RemoteAccessAddressKind.Unknown, false)
        }));
    }

    /// <summary>
    /// The operating system lists a proxy's TUN adapter, a bridge or a VPN before the home
    /// network as often as not. The list is offered recommended first, then by what can reach
    /// it — a device reading this one back tries the addresses in this order and keeps only the
    /// first few — and never loses a host.
    /// </summary>
    [TestMethod]
    public void Hosts_are_offered_recommended_first_then_lan_vpn_unknown_virtual_and_link_local()
    {
        var kinds = new List<RemoteAccessAddressKind>
        {
            RemoteAccessAddressKind.Virtual, // 0: Clash's TUN, 198.18.0.1
            RemoteAccessAddressKind.LinkLocal, // 1
            RemoteAccessAddressKind.Vpn, // 2: Tailscale
            RemoteAccessAddressKind.Lan, // 3: a second LAN without a gateway
            RemoteAccessAddressKind.Unknown, // 4
            RemoteAccessAddressKind.Virtual, // 5: a bridge
            RemoteAccessAddressKind.Lan, // 6: the home network, recommended
            RemoteAccessAddressKind.Vpn // 7: ZeroTier
        };

        CollectionAssert.AreEqual(new[] {6, 3, 2, 7, 4, 0, 5, 1},
            RemoteAccessAddressClassifier.Order(kinds, recommended: 6).ToArray());
    }

    [TestMethod]
    public void Without_a_recommendation_the_hosts_are_still_ordered_by_kind_and_none_is_dropped()
    {
        var kinds = new List<RemoteAccessAddressKind>
        {
            RemoteAccessAddressKind.LinkLocal,
            RemoteAccessAddressKind.Virtual,
            RemoteAccessAddressKind.Unknown,
            RemoteAccessAddressKind.Vpn
        };

        CollectionAssert.AreEqual(new[] {3, 2, 1, 0},
            RemoteAccessAddressClassifier.Order(kinds, recommended: null).ToArray());
        Assert.AreEqual(0, RemoteAccessAddressClassifier.Order([], recommended: null).Count);
    }
}
