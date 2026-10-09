using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// The complete endpoints remote access reports, as the library shows next to its code.
/// The overview lists them so a person knows what to type on the other device (§10.1).
/// </summary>
public sealed class ServiceDataSyncHostAddresses(IRemoteAccessService remoteAccess) : IDataSyncHostAddresses
{
    public IReadOnlyList<string> GetReachableAddresses() => remoteAccess.GetReachableAddresses().Select(a => a.Url)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
