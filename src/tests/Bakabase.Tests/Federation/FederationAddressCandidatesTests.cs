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
    public async Task DefinitionsReadBackContinuesAfterASilentCandidateAndRemembersTheWorkingEndpoint()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS",
            publicDeadline: DataSyncNodeHost.GiveUpSoon);
        await using var silent = await DataSyncNodeHost.StartAsync("node-silent", "Silent");
        silent.Answer = DataSyncNodeHost.Silent;
        await desk.Peers.SetDataSyncSharingAsync(true);
        await nas.Peers.SetDataSyncSharingAsync(true);
        var code = await desk.Peers.CreateDataSyncReciprocalInvitationAsync("node-nas");
        var request = await nas.Peers.SubmitDataSyncRequestAsync(new NodeDataSyncPairRequest("node-desk", "Desk",
            NodeRequestSignature.RandomToken(18), NodeRequestSignature.RandomToken(), NodeDataSyncIntents.TwoWay,
            new NodeReciprocalOffer([silent.Address, desk.Address], code)));
        await nas.Peers.ApproveDataSyncAsync(request.Exchange.RequestId, true);
        var readBack = await nas.Flow.ReadBackDataSyncAsync("node-desk");
        Assert.IsTrue(readBack.Granted, readBack.ErrorCode);
        var peer = (await nas.Peers.GetDataSyncStatusAsync()).Peers.Single(p => p.NodeId == "node-desk");
        Assert.AreEqual(desk.Address, peer.Address);
        Assert.IsTrue(await nas.Peers.HasOutboundDataSyncGrantAsync("node-desk"));
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
        var flow = new FederationPairingFlow(null!, null!, remote,
            NullLogger<FederationPairingFlow>.Instance);

        CollectionAssert.AreEqual(new[] {
            "https://bakabase.example.com", "http://192.168.3.23:34567", "http://192.168.3.23:45678",
            "http://10.0.0.2:34567", "http://10.0.0.2:34568"
        }, flow.GetShareBackAddresses("http://10.0.0.9:34567").ToArray());
    }
}
