using Bootstrap.Extensions;
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

    public static Func<int, Task>? ScaleInSubTask(this Func<int, Task>? onProgressChange, decimal baseOverallProgress,
        decimal subTaskTotalProgress)
    {
        if (onProgressChange == null)
        {
            return null;
        }

        var currentProgress = (int)baseOverallProgress;
        return async p =>
        {
            var progressOverall = (int)(baseOverallProgress + p / subTaskTotalProgress);
            if (progressOverall > currentProgress)
            {
                currentProgress = progressOverall;
            }

            await onProgressChange(progressOverall);
        };
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="onProgressChange"></param>
    /// <param name="currentProgress"></param>
    /// <param name="addingProgress"></param>
    /// <returns>New progress</returns>
    public static async Task<decimal> TriggerWithStep1(this Func<int, Task>? onProgressChange, decimal currentProgress,
        decimal addingProgress)
    {
        var np = currentProgress + addingProgress;
        if (onProgressChange != null)
        {
            var intNp = (int)np;
            var intCurrent = (int)currentProgress;
            if (intNp != intCurrent)
            {
                await onProgressChange(intNp);
                
            }
        }

        return np;
    }
}