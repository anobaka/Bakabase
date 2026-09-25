using System;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bootstrap.Models.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// What kind of install this server tells other devices it is (<c>server-info</c>, the
/// discovery beacon, federation's node info), for showing on their device maps.
/// </summary>
public static class ServiceSelfDescription
{
    /// <summary>
    /// The desktop builds are the desktop app and the container a headless server. A development
    /// run is whichever it was composed as: with the desktop app's server manager
    /// (<see cref="IManagedServerService"/>), which only the desktop app registers, or without.
    /// </summary>
    public static ServerKind KindOf(RuntimeMode mode, bool managesServers) => mode switch
    {
        RuntimeMode.WinForms or RuntimeMode.MacOS => ServerKind.Desktop,
        RuntimeMode.Docker => ServerKind.Headless,
        _ => managesServers ? ServerKind.Desktop : ServerKind.Headless
    };

    /// <summary>
    /// Asked when somebody wants it, from what is registered rather than resolved: the server
    /// manager itself depends on remote access, which is what asks.
    /// </summary>
    public static IServerSelfDescription Create(IServiceProvider services, RuntimeMode mode) =>
        new ServerSelfDescription(() => KindOf(mode,
            services.GetService<IServiceProviderIsService>()?.IsService(typeof(IManagedServerService)) == true));
}
