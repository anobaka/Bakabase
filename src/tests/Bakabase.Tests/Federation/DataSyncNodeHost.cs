using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests.Federation;

/// <summary>
/// One node on a real loopback listener with the Service's own gates (<see cref="ServiceGateHost"/>), the node
/// controllers data sync pairing and the feed go through, and the Service's data sync bridge
/// (<see cref="FederationDataSyncGrants"/>). Two of them talk to each other over real HTTP.
/// </summary>
internal sealed class DataSyncNodeHost : IAsyncDisposable
{
    private readonly ServiceGateHost _host;

    private DataSyncNodeHost(ServiceGateHost host, AddressedRemoteAccess remote, RecordingGrantEvents events)
    {
        _host = host;
        Remote = remote;
        Events = events;
    }

    /// <param name="dataSync">Whether this build has data sync: without it, its info says nothing about it.</param>
    public static async Task<DataSyncNodeHost> StartAsync(string nodeId, string name, bool dataSync = true)
    {
        var remote = new AddressedRemoteAccess();
        var events = new RecordingGrantEvents();
        var host = await ServiceGateHost.StartAsync(
            [typeof(FederationPeerController), typeof(FederationDataSyncPairingController), typeof(DataSyncNodeController)],
            services =>
            {
                services.AddSingleton<INodeIdSource>(new FixedNodeId(nodeId));
                services.AddSingleton<IRemoteAccessService>(remote);
                services.AddSingleton<IBOptionsManager<RemoteAccessOptions>>(
                    new TestBOptionsManager<RemoteAccessOptions>(new RemoteAccessOptions()));
                services.AddSingleton<INodePeerDiscovery, NoPeerDiscovery>();
                services.AddSingleton<FederationPairingFlow>();
                services.AddSingleton<IDataSyncGrantEvents>(events);
                if (!dataSync) return;
                services.AddSingleton<INodeInfoContributor, DataSyncNodeInfoContributor>();
                services.AddSingleton<FederationDataSyncGrants>();
            });
        remote.Addresses = [$"http://127.0.0.1:{host.Port}"];
        var node = new DataSyncNodeHost(host, remote, events);
        await node.Store.SetDisplayNameAsync(name);
        return node;
    }

    public string Address => $"http://127.0.0.1:{_host.Port}";
    public int Port => _host.Port;
    public IServiceProvider Services => _host.Services;
    public AddressedRemoteAccess Remote { get; }
    public RecordingGrantEvents Events { get; }
    public RecordingNotificationService Notifications => _host.Notifications;
    public FederationStateStore Store => Services.GetRequiredService<FederationStateStore>();
    public FederationPeerService Peers => Services.GetRequiredService<FederationPeerService>();
    public FederationDataSyncGrants Grants => Services.GetRequiredService<FederationDataSyncGrants>();
    public FederationPairingFlow Flow => Services.GetRequiredService<FederationPairingFlow>();
    public PeerSessionFactory Sessions => Services.GetRequiredService<PeerSessionFactory>();
    public INodeTransport Transport => Services.GetRequiredService<INodeTransport>();

    /// <summary>Waits for an event raised in the background (a read-back that follows a code), at most ten seconds.</summary>
    public async Task WaitForEventAsync(string raised)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!Events.Raised.Contains(raised))
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"'{raised}' was not raised; got: {string.Join(", ", Events.Raised)}");
            await Task.Delay(20);
        }
    }

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private sealed class FixedNodeId(string id) : INodeIdSource
    {
        public Task<string> GetNodeIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(id);
    }

    private sealed class NoPeerDiscovery : INodePeerDiscovery
    {
        public Task<IReadOnlyList<NodeDiscoveryCandidate>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NodeDiscoveryCandidate>>([]);
    }
}

/// <summary>Remote access whose mode a test sets and whose reachable address is the host's own listener.</summary>
internal sealed class AddressedRemoteAccess : IRemoteAccessService
{
    public RemoteAccessMode Mode { get; set; } = RemoteAccessMode.Enabled;
    public string[] Addresses { get; set; } = [];
    public RemoteAccessMode GetEffectiveMode() => Mode;

    public Task SetModeAsync(RemoteAccessMode? mode)
    {
        Mode = mode ?? RemoteAccessMode.Enabled;
        return Task.CompletedTask;
    }

    public IReadOnlyList<RemoteAccessAddress> GetReachableAddresses() =>
        Addresses.Select(url => new RemoteAccessAddress(url, "Loopback")).ToArray();

    public Task<string> GetOrCreateServerIdAsync() => Task.FromResult("legacy-server-id");
    public bool GetAllowLiveTranscode() => false;
    public Task SetAllowLiveTranscodeAsync(bool allow) => Task.CompletedTask;
    public bool GetRequirePairing() => true;
    public Task SetRequirePairingAsync(bool require) => Task.CompletedTask;
    public Task<RemoteAccessServerDescriptor> GetServerDescriptorAsync() => throw new NotSupportedException();
}

/// <summary>What pairing told data sync, in order, as short strings.</summary>
internal sealed class RecordingGrantEvents : IDataSyncGrantEvents
{
    private readonly List<string> _raised = [];

    public IReadOnlyList<string> Raised
    {
        get { lock (_raised) return [.._raised]; }
    }

    public void OutboundGranted(string peerNodeId)
    {
        lock (_raised) _raised.Add("outbound " + peerNodeId);
    }

    public void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted)
    {
        lock (_raised) _raised.Add($"inbound {peerNodeId} {intent} {readBackStarted}");
    }

    public void ReadBackFailed(string peerNodeId, string errorCode)
    {
        lock (_raised) _raised.Add($"readBackFailed {peerNodeId} {errorCode}");
    }
}
