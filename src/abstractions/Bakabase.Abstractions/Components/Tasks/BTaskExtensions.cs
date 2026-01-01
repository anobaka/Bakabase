using System.Reflection;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Abstractions.Components.Tasks;

public static class BTaskExtensions
{
    /// <summary>
    /// Adds BTask services and automatically discovers and registers all IPredefinedBTaskBuilder implementations.
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="assemblies">Assemblies to scan for task builders. If empty, scans all loaded assemblies.</param>
    public static IServiceCollection AddBTask<TBTaskEventHandler>(this IServiceCollection services,
        params Assembly[] assemblies)
        where TBTaskEventHandler : class, IBTaskEventHandler
    {
        services.AddSingleton<BTaskManager>();
        services.AddSingleton<IBTaskEventHandler, TBTaskEventHandler>();

        // Discover and register all IPredefinedBTaskBuilder implementations
        var assembliesToScan = assemblies.Length > 0
            ? assemblies
            : AppDomain.CurrentDomain.GetAssemblies();

        var interfaceType = typeof(IPredefinedBTaskBuilder);
        var taskTypes = assembliesToScan
            .SelectMany(a =>
            {
                try
                {
                    return a.GetTypes();
                }
                catch
                {
                    return Array.Empty<Type>();
                }
            })
            .Where(t => t is { IsClass: true, IsAbstract: false } &&
                        interfaceType.IsAssignableFrom(t))
            .Distinct()
            .ToList();

        foreach (var type in taskTypes)
        {
            services.AddSingleton(interfaceType, type);
        }

        return services;
    }

    public static async Task InitializeBTasks(this IServiceProvider serviceProvider,
        IEnumerable<BTaskHandlerBuilder> predefinedTasks)
    {
        var manager = serviceProvider.GetRequiredService<BTaskManager>();
        await manager.Initialize();
        foreach (var t in predefinedTasks)
        {
            await manager.Enqueue(t);
        }
    }

    public static Func<int, Task>? ScaleInSubTask(this Func<int, Task>? onProgressChange, float baseOverallProgress,
        float subTaskTotalProgress)
    {
        if (onProgressChange == null)
        {
            return null;
        }

        var currentProgress = baseOverallProgress;
        return async p =>
        {
            var progressOverall = baseOverallProgress + p * subTaskTotalProgress / 100;
            currentProgress = await onProgressChange.TriggerOnJumpingOver(currentProgress, progressOverall - currentProgress);
        };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="onProgressChange"></param>
    /// <param name="currentProgress"></param>
    /// <param name="addingProgress"></param>
    /// <param name="stepLength"></param>
    /// <returns>New progress</returns>
    public static async Task<float> TriggerOnJumpingOver(this Func<int, Task>? onProgressChange, float currentProgress,
        float addingProgress, int stepLength = 1)
    {
        var np = currentProgress + addingProgress;
        if (onProgressChange != null)
        {
            var intNp = (int) np;
            var intCurrent = (int) currentProgress;
            if (intNp - intCurrent >= stepLength)
            {
                await onProgressChange(intNp);
            }
        }

        return np;
    }
}