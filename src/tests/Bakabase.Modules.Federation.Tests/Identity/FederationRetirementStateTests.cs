using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Tests.Security;

namespace Bakabase.Modules.Federation.Tests.Identity;

[TestClass]
public sealed class FederationRetirementStateTests
{
    [TestMethod]
    public async Task Ordinary_state_writes_keep_identity_and_definitions_credentials_but_drop_retired_library_state()
    {
        using var network = new DataSyncTestNetwork();
        var source = network.Add("source");
        var reader = network.Add("reader");
        await source.Store.SetDisplayNameAsync("NAS");
        var inbound = await source.GrantDataSyncAsync(reader);
        var outbound = await reader.GrantDataSyncAsync(source);
        var identity = await source.Identity.GetAsync();
        var path = Path.Combine(source.Path, FederationStateStore.FileName);
        var state = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        var syncBefore = state["inboundDataSyncGrants"]!.ToJsonString();
        state["sharingEnabled"] = true;
        state["outboundGrants"] = new JsonObject { ["retired-peer"] = JsonSerializer.SerializeToNode(outbound) };
        state["inboundGrants"] = new JsonObject { ["retired-grant"] = new JsonObject { ["credentials"] = JsonSerializer.SerializeToNode(inbound) } };
        state["incomingRequests"] = new JsonArray();
        state["peers"]!["reader"]!["address"] = "http://reader";
        state["peers"]!["reader"]!["dataSyncAddress"] = null;
        state["peers"]!["retired-peer"] = new JsonObject
        {
            ["nodeId"] = "retired-peer", ["label"] = "Old library only", ["address"] = "http://retired",
            ["libraryEpoch"] = "old-library", ["enabled"] = true
        };
        await File.WriteAllTextAsync(path, state.ToJsonString());

        var reopened = new FederationStateStore(source, source);
        var provider = new NodeIdentityProvider(reopened);
        var peers = new FederationPeerService(reopened, provider, source.Leases, source.Clock);
        Assert.AreEqual(identity, await provider.GetAsync());
        CollectionAssert.AreEqual(new[] { "reader" },
            (await peers.GetDataSyncStatusAsync()).Peers.Select(peer => peer.NodeId).ToArray(),
            "A retired library relation is not promoted into a definitions candidate.");
        CollectionAssert.AreEqual(new[] { "http://reader" }, (await peers.GetDataSyncAddressesAsync("reader")).ToArray());
        await reopened.SetDisplayNameAsync(identity.Name);

        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.AreEqual(identity, await provider.GetAsync());
        Assert.AreEqual(syncBefore, written["inboundDataSyncGrants"]!.ToJsonString());
        Assert.IsNotNull(written["outboundDataSyncGrants"]!["reader"]);
        Assert.IsNotNull(written["peers"]!["reader"]);
        foreach (var retired in new[] { "sharingEnabled", "inboundGrants", "outboundGrants", "incomingRequests", "outgoingRequests", "invitation", "reciprocalInvitations" })
            Assert.IsFalse(written.ContainsKey(retired), retired);
        var grants = new NodeGrantService(reopened, provider, source.Leases, source.Clock);
        Assert.AreEqual(FederationScopes.DataSyncRead, (await grants.ValidateAsync(inbound.GrantId, identity.LibraryEpoch)).Scope);
        Assert.AreEqual("GrantRevoked", (await Assert.ThrowsExactlyAsync<FederationAccessException>(() =>
            grants.ValidateAsync("retired-grant", identity.LibraryEpoch))).ErrorCode);
    }
}
