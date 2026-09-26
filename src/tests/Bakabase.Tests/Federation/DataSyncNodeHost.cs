using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Controllers;
using Bakabase.Tests.RemoteAccess.Service;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Tests.Federation;

/// <summary>
/// One node on a real loopback listener with the Service's own gates (<see cref="ServiceGateHost"/>), the node
/// controllers data sync pairing and the feed go through, and the Service's data sync bridge
/// (<see cref="FederationDataSyncGrants"/>). Two of them talk to each other over real HTTP.
/// </summary>
internal sealed class DataSyncNodeHost : IAsyncDisposable
{
    private readonly ServiceGateHost _host;
    private readonly Network _network;

    private DataSyncNodeHost(ServiceGateHost host, AddressedRemoteAccess remote, RecordingGrantEvents events,
        Network network)
    {
        _host = host;
        Remote = remote;
        Events = events;
        _network = network;
    }

    /// <param name="dataSync">Whether this build has data sync: without it, its info says nothing about it.</param>
    /// <param name="feed">
    /// What this node's feed serves; without one its feed answers 501, as a build whose feed is not there yet.
    /// </param>
    /// <param name="publicDeadline">
    /// How long this node waits for another's info, pairing answer or handshake
    /// (<see cref="FederationHttpClient.PublicDeadline"/>): shorter for a test that waits for a device that never
    /// answers to be given up on.
    /// </param>
    /// <param name="configure">
    /// Last word on the node's container: its data folders seeded before it starts, a log a test reads, what pairing
    /// tells data sync.
    /// </param>
    public static async Task<DataSyncNodeHost> StartAsync(string nodeId, string name, bool dataSync = true,
        IDataSyncFeedSource? feed = null, TimeSpan? publicDeadline = null, Action<IServiceCollection>? configure = null)
    {
        var remote = new AddressedRemoteAccess();
        var events = new RecordingGrantEvents();
        var network = new Network();
        var host = await ServiceGateHost.StartAsync(
            [typeof(FederationPeerController), typeof(FederationDataSyncPairingController), typeof(DataSyncNodeController)],
            services =>
            {
                services.AddSingleton<IStartupFilter>(network);
                services.AddSingleton<INodeIdSource>(new FixedNodeId(nodeId));
                services.AddSingleton<IRemoteAccessService>(remote);
                services.AddSingleton<IBOptionsManager<RemoteAccessOptions>>(
                    new TestBOptionsManager<RemoteAccessOptions>(new RemoteAccessOptions()));
                services.AddSingleton<INodePeerDiscovery, NoPeerDiscovery>();
                services.AddSingleton<FederationPairingFlow>();
                services.AddSingleton<IDataSyncGrantEvents>(events);
                if (feed != null) services.AddSingleton(feed);
                // The typed client as registered (its handler), with the test's deadline; registered last, it wins.
                if (publicDeadline is { } wait)
                    services.AddTransient(sp => new FederationHttpClient(sp.GetRequiredService<IHttpClientFactory>()
                        .CreateClient(nameof(FederationHttpClient))) { PublicDeadline = wait });
                if (dataSync)
                {
                    services.AddSingleton<INodeInfoContributor, DataSyncNodeInfoContributor>();
                    services.AddSingleton<FederationDataSyncGrants>();
                    services.AddSingleton<FederationDataSyncPeerClient>();
                }

                configure?.Invoke(services);
            });
        remote.Addresses = [$"http://127.0.0.1:{host.Port}"];
        var node = new DataSyncNodeHost(host, remote, events, network);
        await node.Store.SetDisplayNameAsync(name);
        return node;
    }

    /// <summary>
    /// A <c>publicDeadline</c> for a node that must give up on a device that never answers: well above what a loopback
    /// exchange takes, a quarter of the Service's own.
    /// </summary>
    public static readonly TimeSpan GiveUpSoon = TimeSpan.FromSeconds(2);

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
    public FederationHttpClient Http => Services.GetRequiredService<FederationHttpClient>();

    /// <summary>
    /// While set, answers every request that reaches this node's listener in its place, before any gate: what the
    /// network shows of the node. <see cref="Silent"/> is a node that accepts connections and never answers.
    /// </summary>
    public Func<HttpContext, CancellationToken, Task>? Answer
    {
        get => _network.Answer;
        set => _network.Answer = value;
    }

    /// <summary>
    /// An <see cref="Answer"/>: holds every request until its caller gives up (or the node stops), so the caller's own
    /// deadline is what ends it.
    /// </summary>
    public static async Task Silent(HttpContext context, CancellationToken stopping)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, stopping);
        try { await Task.Delay(Timeout.Infinite, either.Token); }
        catch (OperationCanceledException) { context.Abort(); }
    }

    /// <summary>An <see cref="Answer"/>: drops every connection at once, as an address where the node is not.</summary>
    public static Task Refuse(HttpContext context, CancellationToken stopping)
    {
        context.Abort();
        return Task.CompletedTask;
    }

    /// <summary>This node's reader of other nodes' feeds, as the Service registers it.</summary>
    public FederationDataSyncPeerClient PeerClient => Services.GetRequiredService<FederationDataSyncPeerClient>();

    /// <summary>
    /// A reader of its own, with fresh sessions (nothing verified yet, so the next call asks <c>/info</c> and
    /// handshakes again), and the wait and deadlines a test sets.
    /// </summary>
    public FederationDataSyncPeerClient NewPeerClient(TimeSpan? fetchWait = null, TimeSpan? deadline = null)
    {
        var sessions = new PeerSessionFactory(Store, Services.GetRequiredService<INodeIdentityProvider>(), Http,
            TimeProvider.System);
        var leases = Services.GetRequiredService<GrantLeaseRegistry>();
        var defaults = new FederationDataSyncPeerClient(sessions, Transport, Http, Peers, leases, TimeProvider.System);
        return new FederationDataSyncPeerClient(sessions, Transport, Http, Peers, leases, TimeProvider.System)
        {
            FetchWait = fetchWait ?? defaults.FetchWait,
            HeadDeadline = deadline ?? defaults.HeadDeadline,
            ManifestDeadline = deadline ?? defaults.ManifestDeadline,
            PageDeadline = deadline ?? defaults.PageDeadline
        };
    }

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

    /// <summary>The node as the network shows it: the node itself, unless a test answers in its place.</summary>
    private sealed class Network : IStartupFilter
    {
        public Func<HttpContext, CancellationToken, Task>? Answer { get; set; }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            var stopping = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
            app.Use((context, rest) => Answer is { } answer ? answer(context, stopping) : rest(context));
            next(app);
        };
    }

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
