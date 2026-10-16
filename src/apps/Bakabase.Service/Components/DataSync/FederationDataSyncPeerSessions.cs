using System;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Management;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// A passive management connection is an independent reconnect signal for definitions sync.
/// Never observe sync's own transport: a failed feed after a successful handshake must retain its backoff.
/// </summary>
public sealed class FederationDataSyncPeerSessions(IServiceProvider services) : IDataSyncPeerSessions
{
    public bool IsOnline(string peerNodeId) =>
        services.GetService<IManagedServerService>() is ManagedServerManager manager &&
        manager.LastKnownState(peerNodeId) == ManagedServerState.Online;
}
