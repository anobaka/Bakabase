using System;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.Federation.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// Whether a federation session to a peer is verified now, as the federation's own sessions say (§8.2 "a federation
/// session to it came online → now"): browsing a peer's library proves it is back before data sync's next attempt at
/// it. Read from memory on every scheduler tick. A host without federation has no sessions, and every peer reads as not
/// online.
/// </summary>
public sealed class FederationDataSyncPeerSessions(IServiceProvider services) : IDataSyncPeerSessions
{
    private const string Online = "Online";

    public bool IsOnline(string peerNodeId) =>
        string.Equals(services.GetService<PeerSessionFactory>()?.GetConnectionState(peerNodeId), Online,
            StringComparison.Ordinal);
}
