﻿using Bootstrap.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Tasks;

public static class BTaskExtensions
{
    public static IServiceCollection AddBTask<TBTaskEventHandler>(this IServiceCollection services)
        where TBTaskEventHandler : class, IBTaskEventHandler
    {
        services.AddSingleton<BTaskManager>();
        services.AddSingleton<IBTaskEventHandler, TBTaskEventHandler>();
        return services;
    }

    public static async Task InitializeBTasks(this IServiceProvider serviceProvider, IEnumerable<BTaskHandlerBuilder> predefinedTasks)
    {
        var manager = serviceProvider.GetRequiredService<BTaskManager>();
        await manager.Initialize();
        foreach (var t in predefinedTasks)
        {
            manager.Enqueue(t);
        }
    }
}