using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Abstractions.Models.Domain;
using Bakabase.Modules.Player.Abstractions.Services;
using Bakabase.Modules.Player.Components;
using Bakabase.Modules.Player.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bakabase.Modules.Player.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPlayerModule(this IServiceCollection services,
        Action<PlayerModuleOptions>? configure = null)
    {
        services.AddOptions<PlayerModuleOptions>();
        if (configure != null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IPlayerExecutableLocator, DefaultPlayerExecutableLocator>();
        services.TryAddSingleton<IBatchPlayProcessLauncher, ProcessBatchPlayLauncher>();
        // The all-in-one's answer: the files are right here. A host whose files are
        // somewhere else registers its own before calling this.
        services.TryAddSingleton<IBatchPlayFileResolver, LocalBatchPlayFileResolver>();
        services.TryAddSingleton<IPlayerDiscoveryService, PlayerDiscoveryService>();
        services.TryAddSingleton<IDevicePlaybackPreferencesStore, DevicePlaybackPreferencesStore>();
        // Both default to "the library and the files are on this machine". A host where
        // they are not registers its own before calling this.
        services.TryAddScoped<IBatchPlayResourceSource, ResourceBatchPlaySource>();
        services.TryAddScoped<IBatchPlayService, BatchPlayService>();
        return services;
    }
}
