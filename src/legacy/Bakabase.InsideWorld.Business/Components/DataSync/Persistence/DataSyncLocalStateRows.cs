using System;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Identity;
using Bakabase.Modules.DataSync.Models.Db;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>The one <c>DataSyncLocalStates</c> row (§4.1), created lazily by the first Refresh (§4.5).</summary>
public static class DataSyncLocalStateRows
{
    public const int SingletonId = 1;

    /// <summary>
    /// A fresh row for <paramref name="device"/>: generation 1, a new random salt and the actor it derives
    /// (§5.6), no counters and no sequence numbers issued yet, and a new database instance id.
    /// </summary>
    public static DataSyncLocalStateDbModel New(DataSyncDevice device, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(device);
        var salt = DataSyncActorId.NewSalt();
        return new DataSyncLocalStateDbModel
        {
            Id = SingletonId,
            NodeId = device.NodeId,
            LibraryEpoch = device.LibraryEpoch,
            ActorGeneration = 1,
            ActorSalt = salt,
            ActorId = DataSyncActorId.Derive(device.NodeId, device.LibraryEpoch, salt).Value,
            ActorCounter = 0,
            DbInstanceId = Guid.NewGuid().ToString("N"),
            LastSeq = 0,
            UpdatedAtUtc = nowUtc,
        };
    }
}
