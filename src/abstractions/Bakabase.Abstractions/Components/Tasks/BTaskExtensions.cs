using Bootstrap.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Tasks;

public static class BTaskExtensions
{
    public static IServiceCollection AddBTask<TBTaskEventHandler>(this IServiceCollection services)
        where TBTaskEventHandler : class, IBTaskEventHandler
    {
        var predefinedTaskTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => typeof(IPredefinedBTask).IsAssignableFrom(p) &&
                        p is {IsClass: true, IsPublic: true, IsAbstract: false});

        foreach (var t in predefinedTaskTypes)
        {
            services.AddSingleton(SpecificTypeUtils<IPredefinedBTask>.Type, t);
        }

        services.AddSingleton<BTaskManager>();
        services.AddSingleton<IBTaskEventHandler, TBTaskEventHandler>();
        return services;
    }
}