using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Whether an address is this machine's — what tells this device apart from a copy of its data
/// directory running on another computer, which answers with the same identity.
/// </summary>
[TestClass]
public class ThisMachineTests
{
    /// <summary>A documentation address (TEST-NET-1): never this machine's.</summary>
    private const string Elsewhere = "192.0.2.10";

    private static IPAddress[] OwnAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .ToArray();

    [TestMethod]
    public void Loopback_and_every_address_of_its_own_interfaces_are_this_machine()
    {
        Assert.IsTrue(ThisMachine.Holds(IPAddress.Loopback));
        Assert.IsTrue(ThisMachine.Holds(IPAddress.IPv6Loopback));
        Assert.IsTrue(ThisMachine.Holds(IPAddress.Parse("127.0.0.2")));
        Assert.IsTrue(ThisMachine.Holds(IPAddress.Loopback.MapToIPv6()));

        foreach (var own in OwnAddresses())
        {
            Assert.IsTrue(ThisMachine.Holds(own), own.ToString());
        }
    }

    [TestMethod]
    public void An_address_no_interface_here_holds_is_another_machine()
    {
        Assert.IsFalse(ThisMachine.Holds(IPAddress.Parse(Elsewhere)));
        Assert.IsFalse(ThisMachine.Holds(IPAddress.Parse(Elsewhere).MapToIPv6()));
        Assert.IsFalse(ThisMachine.Holds(IPAddress.Parse("2001:db8::10")));
    }

    [TestMethod]
    public async Task An_address_is_judged_by_its_host_whether_written_as_an_address_or_a_name()
    {
        Assert.IsTrue(await ThisMachine.ReachedByAsync(new Uri("http://127.0.0.1:34567")));
        Assert.IsTrue(await ThisMachine.ReachedByAsync(new Uri("http://[::1]:34567")));
        Assert.IsTrue(await ThisMachine.ReachedByAsync(new Uri("http://localhost:34567")));

        foreach (var own in OwnAddresses().Where(a => a.AddressFamily == AddressFamily.InterNetwork))
        {
            Assert.IsTrue(await ThisMachine.ReachedByAsync(new Uri($"http://{own}:34567")), own.ToString());
        }

        Assert.IsFalse(await ThisMachine.ReachedByAsync(new Uri($"http://{Elsewhere}:34567")));
        Assert.IsFalse(await ThisMachine.ReachedByAsync(new Uri("http://[2001:db8::10]:34567")));
    }

    [TestMethod]
    public async Task A_name_that_does_not_resolve_is_not_known_to_be_this_machine()
    {
        Assert.IsFalse(await ThisMachine.ReachedByAsync(new Uri("http://no-such-device.invalid:34567")));
        Assert.IsFalse(await ThisMachine.ReachedByAsync(new Uri("http://pc2.local:34567"),
            (_, _) => Task.FromResult(Array.Empty<IPAddress>())));
    }

    [TestMethod]
    public async Task A_name_is_this_machine_only_when_every_address_it_resolves_to_is()
    {
        // Another computer's name can resolve to an address this machine holds too — a VPN or
        // proxy adapter's, a virtual machine host's — beside its own.
        static Func<string, CancellationToken, Task<IPAddress[]>> To(params string[] addresses) =>
            (_, _) => Task.FromResult(addresses.Select(IPAddress.Parse).ToArray());

        var name = new Uri("http://pc2.local:34567");

        Assert.IsTrue(await ThisMachine.ReachedByAsync(name, To("127.0.0.1", "::1")));
        Assert.IsFalse(await ThisMachine.ReachedByAsync(name, To("127.0.0.1", Elsewhere)));
        Assert.IsFalse(await ThisMachine.ReachedByAsync(name, To(Elsewhere, "127.0.0.1")));
    }
}
