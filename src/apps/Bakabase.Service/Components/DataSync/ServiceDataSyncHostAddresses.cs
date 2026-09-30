using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.DataSync.Runtime;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;

namespace Bakabase.Service.Components.DataSync;

/// <summary>
/// The addresses other devices can reach this one at, as remote access reports them: one per host, as the library
/// shows next to its code. The overview lists them so a person knows what to type on the other device (§10.1).
/// </summary>
public sealed class ServiceDataSyncHostAddresses(IRemoteAccessService remoteAccess) : IDataSyncHostAddresses
{
    public IReadOnlyList<string> GetReachableAddresses() => remoteAccess.GetReachableAddresses().Select(a => a.Url)
        .GroupBy(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url)
        .Select(g => g.First())
        .ToList();
}
