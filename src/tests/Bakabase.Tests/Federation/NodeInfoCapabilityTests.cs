using System.Reflection;
using System.Text.Json;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Service.Components.Federation;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// What a node says about data sync in <c>/info</c> and its handshake (§7.4): outside the proof, filled only where
/// the host has data sync, absent on older nodes, and enough for discovery to list a node that shares only its
/// definitions. The budgets a reader applies are in <c>PeerTransportTests</c>.
/// </summary>
[TestClass]
public sealed class NodeInfoCapabilityTests
{
    [TestMethod]
    public async Task TheContributorSaysTheContractTheKindsInApplyOrderAndTheSwitch()
    {
        using var directory = new Directory();
        var store = new FederationStateStore(directory, directory);
        var peers = new FederationPeerService(store, new NodeIdentityProvider(store), new(), TimeProvider.System);
        var services = new ServiceCollection();
        services.AddScoped(_ => Kind("futureKind", 2));
        services.AddScoped(_ => Kind(DataSyncKindIds.CustomProperty, 1));
        services.AddScoped(_ => Kind(DataSyncKindIds.ExtensionGroup, 1));
        await using var provider = services.BuildServiceProvider();
        var contributor = new DataSyncNodeInfoContributor(store, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DataSyncNodeInfoContributor>.Instance);
        var plain = new NodeInfo("node", "epoch", "Node", 1, DateTimeOffset.UtcNow);

        var info = await contributor.ContributeAsync(plain, default);
        Assert.AreEqual((DataSyncContract.Version, DataSyncContract.MinimumPeerVersion, (bool?)false),
            (info.DataSyncContractVersion, info.DataSyncMinimumPeerContract, info.SharesDefinitions));
        CollectionAssert.AreEqual(new[] { "extensionGroup@1", "customProperty@1", "futureKind@2" }, info.DataSyncKinds);
        await peers.SetDataSyncSharingAsync(true);
        Assert.IsTrue((await contributor.ContributeAsync(plain, default)).SharesDefinitions);
        // What it adds is outside the handshake proof.
        var key = Bakabase.Modules.Federation.Security.NodeRequestSignature.RandomToken();
        Assert.AreEqual(Bakabase.Modules.Federation.Security.NodeRequestSignature.HandshakeProof(key, plain, "challenge-0123456789"),
            Bakabase.Modules.Federation.Security.NodeRequestSignature.HandshakeProof(key, info, "challenge-0123456789"));
    }

    [TestMethod]
    public async Task KindsThatCannotBeReadLeaveInfoWorking()
    {
        using var directory = new Directory();
        var store = new FederationStateStore(directory, directory);
        var services = new ServiceCollection();
        services.AddScoped<IDataSyncKind>(_ => throw new InvalidOperationException("No database."));
        await using var provider = services.BuildServiceProvider();
        var contributor = new DataSyncNodeInfoContributor(store, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DataSyncNodeInfoContributor>.Instance);

        var info = await contributor.ContributeAsync(new NodeInfo("node", "epoch", "Node", 1, DateTimeOffset.UtcNow), default);
        Assert.IsNull(info.DataSyncKinds);
        Assert.AreEqual(DataSyncContract.Version, info.DataSyncContractVersion);
    }

    [TestMethod]
    public void AnOlderNodeSaysNothingAboutDataSync()
    {
        var older = JsonSerializer.Deserialize<NodeInfo>(
            "{\"nodeId\":\"remote\",\"libraryEpoch\":\"e\",\"name\":\"NAS\",\"protocolVersion\":1," +
            "\"serverTimeUtc\":\"2026-09-20T12:00:00+00:00\",\"kind\":\"headless\"}", FederationJson.Options)!;
        Assert.AreEqual(((int?)null, (int?)null, (string[]?)null, (bool?)null),
            (older.DataSyncContractVersion, older.DataSyncMinimumPeerContract, older.DataSyncKinds, older.SharesDefinitions));
        Assert.ThrowsExactly<Bakabase.Modules.Federation.Security.FederationAccessException>(() =>
            NodePairingClient.RequireDataSyncCapability(older, FederationPairingFlow.DataSyncContractOfThisBuild));
    }

    /// <summary>A node sharing only its definitions answers info, and discovery lists it with what it says.</summary>
    [TestMethod]
    public async Task DiscoveryListsANodeThatSharesOnlyItsDefinitionsAndAnOlderNodeWithout()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await using var old = await DataSyncNodeHost.StartAsync("node-old", "Old NAS", dataSync: false);
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        await old.Peers.SetSharingAsync(true);

        var discovery = new FederationNodeDiscovery(new FixedServers(nas.Address, old.Address),
            desk.Services.GetRequiredService<INodeIdentityProvider>());
        var found = await discovery.DiscoverAsync(default);

        var definitions = found.Single(c => c.NodeId == "node-nas");
        Assert.AreEqual(("NAS", 1, (bool?)true), (definitions.Name, definitions.DataSyncContractVersion, definitions.SharesDefinitions));
        var older = found.Single(c => c.NodeId == "node-old");
        Assert.AreEqual(((int?)null, (bool?)null), (older.DataSyncContractVersion, older.SharesDefinitions));

        // What the desk's data sync offers to link with, from the same read.
        var grants = new FederationDataSyncGrants(desk.Peers, desk.Services.GetRequiredService<NodePairingClient>(),
            desk.Flow, desk.Store, desk.Services.GetRequiredService<INodeIdentityProvider>(), desk.Remote,
            desk.Services.GetRequiredService<IBOptionsManager<RemoteAccessOptions>>(), desk.Sessions, discovery);
        var candidate = (await grants.GetPeersAsync(true, default)).Single(p => p.NodeId == "node-nas");
        Assert.AreEqual((false, true, (int?)1, (bool?)true, false),
            (candidate.Known, candidate.Discovered, candidate.ContractVersion, candidate.SharesDefinitions, candidate.WeMayRead));
        Assert.AreEqual(0, (await grants.GetPeersAsync(false, default)).Count, "Without discovering, only known devices.");
    }

    /// <summary>
    /// Anyone on the network can answer info, and data sync's peer candidates are read by paired devices too (§12): an
    /// identity a session would refuse — a node id that is not one, a name that is blank, too long or carries control
    /// characters — is not listed.
    /// </summary>
    [TestMethod]
    public async Task DiscoveryListsNoIdentityASessionWouldRefuse()
    {
        await using var desk = await DataSyncNodeHost.StartAsync("node-desk", "Desk");
        await using var nas = await DataSyncNodeHost.StartAsync("node-nas", "NAS");
        await nas.Grants.SetSharingEnabledAsync(true, false, default);
        var liars = new List<DataSyncNodeHost>();
        try
        {
            foreach (var (nodeId, name) in new[]
                     {
                         ("node-bell", "NAS\u0007\nclick here"), ("../node", "NAS"), ("node blank", "NAS"),
                         ("node-long", new string('N', 129)), ("node-empty", " ")
                     })
            {
                var liar = await DataSyncNodeHost.StartAsync("node-liar-" + liars.Count, "Liar");
                liars.Add(liar);
                liar.Answer = (context, _) => context.Response.WriteAsJsonAsync(
                    new NodeInfo(nodeId, "epoch", name, 1, DateTimeOffset.UtcNow), FederationJson.Options);
            }
            var discovery = new FederationNodeDiscovery(
                new FixedServers([nas.Address, ..liars.Select(l => l.Address)]),
                desk.Services.GetRequiredService<INodeIdentityProvider>());

            CollectionAssert.AreEqual(new[] { "node-nas" }, (await discovery.DiscoverAsync(default)).Select(c => c.NodeId).ToArray());
            var grants = new FederationDataSyncGrants(desk.Peers, desk.Services.GetRequiredService<NodePairingClient>(),
                desk.Flow, desk.Store, desk.Services.GetRequiredService<INodeIdentityProvider>(), desk.Remote,
                desk.Services.GetRequiredService<IBOptionsManager<RemoteAccessOptions>>(), desk.Sessions, discovery);
            CollectionAssert.AreEqual(new[] { "node-nas" },
                (await grants.GetPeersAsync(true, default)).Select(p => p.NodeId).ToArray());
        }
        finally
        {
            foreach (var liar in liars) await liar.DisposeAsync();
        }
    }

    private static IDataSyncKind Kind(string kind, int schemaVersion)
    {
        var descriptor = new DataSyncKindDescriptor(kind, schemaVersion, [], typeof(object), false, false, false, false,
            "item");
        var codec = DispatchProxy.Create<IDataSyncKindCodec, Answering>();
        ((Answering)(object)codec).Answer = m => m.Name == "get_Descriptor" ? descriptor : throw new NotSupportedException(m.Name);
        var adapter = DispatchProxy.Create<IDataSyncKind, Answering>();
        ((Answering)(object)adapter).Answer = m => m.Name == "get_Codec" ? codec : throw new NotSupportedException(m.Name);
        return adapter;
    }

    /// <summary>An interface whose members answer as the test says; only what the contributor reads is asked.</summary>
    public class Answering : DispatchProxy
    {
        public Func<MethodInfo, object?> Answer { get; set; } = m => throw new NotSupportedException(m.Name);
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Answer(targetMethod!);
    }

    private sealed class FixedServers(params string[] addresses) : IServerDiscovery
    {
        public Task<IReadOnlyList<DiscoveredServer>> DiscoverAsync(TimeSpan timeout, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DiscoveredServer>>(addresses
                .Select(a => new DiscoveredServer("legacy-" + a, "Server", a, "1.0.0", 1, false)).ToArray());
    }

    private sealed class Directory : IFederationDataDirectory, INodeIdSource, IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "federation-node-info-" + Guid.NewGuid().ToString("N"));
        public string Ensure() { System.IO.Directory.CreateDirectory(Path); return Path; }
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("node");
        public void Dispose() { if (System.IO.Directory.Exists(Path)) System.IO.Directory.Delete(Path, true); }
    }
}
