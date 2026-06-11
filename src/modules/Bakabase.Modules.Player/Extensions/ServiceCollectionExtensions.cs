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
        services.TryAddSingleton<IPlayerDiscoveryService, PlayerDiscoveryService>();
        services.TryAddScoped<IBatchPlayService, BatchPlayService>();
        return services;
    }
}
