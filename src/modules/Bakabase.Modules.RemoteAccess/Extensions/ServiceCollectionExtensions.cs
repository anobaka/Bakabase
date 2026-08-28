using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components;
using Bakabase.Modules.RemoteAccess.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.RemoteAccess.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the remote-access gate. The application layer supplies
    /// <paramref name="defaultMode"/> because only it knows the runtime mode.
    /// </summary>
    public static IServiceCollection AddRemoteAccess(this IServiceCollection services,
        RemoteAccessMode defaultMode)
    {
        services.TryAddSingleton(new RemoteAccessDefaults(defaultMode));
        services.TryAddSingleton<IRemoteAccessService, RemoteAccessService>();
        services.TryAddSingleton<IMediaPathGuard, MediaPathGuard>();

        services.AddSingleton<IServableRootProvider, MediaLibraryServableRootProvider>();
        services.AddSingleton<IServableRootProvider, PathMarkServableRootProvider>();
        services.AddSingleton<IServableRootProvider, AppDataServableRootProvider>();

        return services;
    }
}
