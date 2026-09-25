using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// Grant events with nobody listening. The pairing flow may raise them before the sync runtime's scheduler exists;
/// links then react at their next scheduled attempt instead of within seconds (spec §8.2).
/// </summary>
public sealed class NoOpDataSyncGrantEvents : IDataSyncGrantEvents
{
    public void OutboundGranted(string peerNodeId)
    {
    }

    public void InboundGranted(string peerNodeId, DataSyncRequestIntent intent, bool readBackStarted)
    {
    }
}
