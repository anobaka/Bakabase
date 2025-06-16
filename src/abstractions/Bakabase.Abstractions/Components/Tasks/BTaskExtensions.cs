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