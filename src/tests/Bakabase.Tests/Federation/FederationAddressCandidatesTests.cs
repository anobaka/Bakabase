using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.Federation;
using Bakabase.Tests.DataSync.Api;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationAddressCandidatesTests
{
    [TestMethod]
    public async Task LibraryReadBackContinuesAfterASilentCandidateAndRemembersTheWorkingEndpoint()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS",
            publicDeadline: DataSyncNodeHost.GiveUpSoon);
        await using var silent = await DataSyncNodeHost.StartAsync("node-silent", "Silent");
        silent.Answer = DataSyncNodeHost.Silent;
        await desk.Peers.SetSharingAsync(true);
        await nas.Peers.SetSharingAsync(true);
        var code = await desk.Peers.CreateReciprocalInvitationAsync("node-nas");
        var request = await nas.Peers.SubmitPairingRequestAsync(new NodePairRequest("node-desk", "Desk",
            NodeRequestSignature.RandomToken(18), NodeRequestSignature.RandomToken(),
            new NodeReciprocalOffer([silent.Address, desk.Address], code)));

        nas.Flow.ReadBack(await nas.Peers.ApproveAsync(request.Exchange.RequestId));

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var peer = (await nas.Peers.GetStatusAsync(deadline.Token)).Peers.SingleOrDefault(p => p.NodeId == "node-desk");
            if (peer?.OutboundGrant != null)
            {
                Assert.AreEqual(desk.Address, peer.Address);
                break;
            }
            await Task.Delay(25, deadline.Token);
        }
    }

    [TestMethod]
    public void ReadBackKeepsPublishedEndpointsAndRespectsTheConfiguredEntryBeforeSubnetPreference()
    {
        var remote = DataSyncServiceComponentsTests.AddressesOnly.Create([
            new RemoteAccessAddress("https://bakabase.example.com", "", Source: "configured"),
            new RemoteAccessAddress("http://192.168.3.23:34567", "", Source: "browser"),
            new RemoteAccessAddress("http://192.168.3.23:45678", "", Source: "deployment"),
            new RemoteAccessAddress("http://10.0.0.2:34567", "en0"),
            new RemoteAccessAddress("http://10.0.0.2:34568", "en0"),
            new RemoteAccessAddress("http://10.0.0.2:34567", "en0")]);
        var flow = new FederationPairingFlow(null!, null!, null!, remote,
            NullLogger<FederationPairingFlow>.Instance);

        CollectionAssert.AreEqual(new[] {
            "https://bakabase.example.com", "http://192.168.3.23:34567", "http://192.168.3.23:45678",
            "http://10.0.0.2:34567", "http://10.0.0.2:34568"
        }, flow.GetShareBackAddresses("http://10.0.0.9:34567").ToArray());
    }
}
