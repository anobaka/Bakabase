using Bakabase.Abstractions.Components.Gui;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Discovery.Clients;
using Bakabase.Remoting.Components.Forwarding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Remoting.Components.Console;

public static class RemoteConsoleServiceCollectionExtensions
{
    /// <summary>
    /// Lets this app manage other servers in full and show their UI in its own window.
    /// Called by the desktop app's host after the server's own services are registered;
    /// a headless server never calls it, and its endpoints then report management as
    /// unavailable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything here lives in the app's own container, but none of it is part of the
    /// server's request pipeline: the server reaches it only through
    /// <see cref="IManagedServerService"/>, and the shell only through
    /// <see cref="IMainViewSwitcher"/>. The relays it starts are separate hosts with
    /// containers of their own (<see cref="ManagedServerRelay"/>).
    /// </para>
    /// <para>
    /// The managed-server store is registered as itself and only as itself — never as the
    /// relay core's connection store — so nothing in the server can resolve "the client's
    /// connection" and come away holding the keys.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddRemoteConsole(this IServiceCollection services,
        Action<RemoteConsoleOptions>? configure = null)
    {
        var options = new RemoteConsoleOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // One set of tickets for the whole app: the page asking for a ticket and the relay
        // consuming it are in different containers, and have to agree.
        services.TryAddSingleton<RelayNavigationTokens>();

        services.TryAddSingleton(sp => new RemoteConsoleLocalOrigin(sp.GetService<AppContext>()));

        services.TryAddSingleton(sp => new ManagedServerStore(new ManagedServerDirectory(() =>
            options.ManagedDirectory ??
            Path.Combine(sp.GetRequiredService<IRemoteAccessDataDirectory>().Path, ManagedServerDirectory.DirectoryName))));

        services.TryAddSingleton(_ => new LegacyClientConnectionSource(
            options.LegacyClientConnectionFile ?? LegacyClientConnectionSource.ResolveDefaultFile));

        // Finding servers to manage: the remote-access beacons, both halves. The server's own
        // registrations usually got here first (library sharing's search starts from the same
        // beacons), and TryAdd keeps theirs; either way nothing probes until somebody asks —
        // these send nothing when constructed, and a headless server never composes this.
        services.TryAddSingleton<UdpProbeClient>();
        services.TryAddSingleton<MdnsBrowser>();
        services.TryAddSingleton<IServerDiscovery, ServerDiscovery>();

        services.TryAddSingleton<RemoteConsoleManager>();
        services.TryAddSingleton<IManagedServerService>(sp => sp.GetRequiredService<RemoteConsoleManager>());
        services.AddHostedService(sp => sp.GetRequiredService<RemoteConsoleManager>());

        // The shell's view, deliberately not the manager: the tray asks from the UI thread
        // and may ask before the host has started, and resolving the manager would build it
        // there — file reads and all. The manager attaches itself once it exists.
        services.TryAddSingleton<RemoteConsoleSwitcher>();
        services.TryAddSingleton<IMainViewSwitcher>(sp => sp.GetRequiredService<RemoteConsoleSwitcher>());

        return services;
    }
}
