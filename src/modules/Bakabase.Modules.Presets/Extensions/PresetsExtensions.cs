using Bakabase.Modules.Presets.Abstractions;
using Bakabase.Modules.Presets.Components;
using Bakabase.Modules.Presets.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Presets.Extensions;

public static class PresetsExtensions
{
    public static IServiceCollection AddPresets(this IServiceCollection services)
    {
        return services.AddScoped<IPresetsService, PresetsService>()
            .AddTransient<IPresetsLocalizer, PresetsLocalizer>();
    }
}