using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>When the discovery beacon has to start again with what it answers.</summary>
[TestClass]
public class RemoteAccessDiscoveryServiceTests
{
    private static readonly RemoteAccessServerDescriptor Descriptor = new("old-id", "Desk PC", 34567, "2.4.0", 1);

    [TestMethod]
    public void A_beacon_answering_as_the_install_is_left_up()
    {
        Assert.IsTrue(RemoteAccessDiscoveryService.IsCurrent(34567, "old-id", Descriptor));
    }

    [TestMethod]
    public void A_new_identity_at_the_same_port_starts_the_beacon_again()
    {
        // "Create a new device identity" keeps the port: a beacon left up would go on answering
        // under the identity the install just gave up — the copy's original's.
        Assert.IsFalse(RemoteAccessDiscoveryService.IsCurrent(34567, "old-id", Descriptor with {Id = "new-id"}));
    }

    [TestMethod]
    public void Another_port_or_no_beacon_at_all_starts_it()
    {
        Assert.IsFalse(RemoteAccessDiscoveryService.IsCurrent(34567, "old-id", Descriptor with {Port = 34568}));
        Assert.IsFalse(RemoteAccessDiscoveryService.IsCurrent(null, null, Descriptor));
    }
}
