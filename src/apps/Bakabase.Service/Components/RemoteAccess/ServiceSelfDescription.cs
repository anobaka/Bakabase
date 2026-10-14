using System;
using Bakabase.Abstractions.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
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
    /// run is whichever it was composed as: with a desktop window switcher
    /// (<see cref="IMainViewSwitcher"/>), or without. Management connections alone do not
    /// imply a desktop, because direct Service hosts register them too.
    /// </summary>
    public static ServerKind KindOf(RuntimeMode mode, bool hasDesktopWindow) => mode switch
    {
        RuntimeMode.WinForms or RuntimeMode.MacOS => ServerKind.Desktop,
        RuntimeMode.Docker => ServerKind.Headless,
        _ => hasDesktopWindow ? ServerKind.Desktop : ServerKind.Headless
    };

    /// <summary>
    /// Asked from what is registered rather than resolved, without constructing the desktop
    /// switcher or its connection manager while remote access itself is being composed.
    /// </summary>
    public static IServerSelfDescription Create(IServiceProvider services, RuntimeMode mode) =>
        new ServerSelfDescription(() => KindOf(mode,
            services.GetService<IServiceProviderIsService>()?.IsService(typeof(IMainViewSwitcher)) == true));
}
