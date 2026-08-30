using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Bakabase.Modules.RemoteAccess.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.RemoteAccess.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the remote-access gate and the discovery beacon. The application
    /// layer supplies <paramref name="defaultMode"/> and
    /// <paramref name="appVersion"/> because only it knows the runtime mode and
    /// its own version.
    /// </summary>
    /// <remarks>
    /// The caller must also register an
    /// <see cref="Abstractions.Components.IListeningAddressProvider"/>; only the
    /// application layer knows what the host is bound to.
    /// </remarks>
    public static IServiceCollection AddRemoteAccess(this IServiceCollection services,
        RemoteAccessMode defaultMode, string appVersion)
    {
        services.TryAddSingleton(new RemoteAccessDefaults(defaultMode));
        services.TryAddSingleton(new RemoteAccessHostInfo(appVersion));
        services.TryAddSingleton<IRemoteAccessService, RemoteAccessService>();
        services.TryAddSingleton<IMediaPathGuard, MediaPathGuard>();

        services.AddSingleton<IServableRootProvider, MediaLibraryServableRootProvider>();
        services.AddSingleton<IServableRootProvider, PathMarkServableRootProvider>();
        services.AddSingleton<IServableRootProvider, AppDataServableRootProvider>();

        // Idle while remote access is Disabled (the desktop default): no sockets
        // are opened and nothing is written to disk, so existing installs see no
        // behavior change until the user turns remote access on.
        services.AddHostedService<RemoteAccessDiscoveryService>();

        return services;
    }
}
