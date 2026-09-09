using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// The heartbeat behind the acquisitions page: brings task rows back in line with the runs that
/// carry them, then starts as many queued acquisitions as the limit allows.
/// <para>
/// Both halves need something outside a run to do them. Nothing runs after a recipe's last step, so
/// only an outside observer can notice that it finished; and a task that had to queue when it was
/// created has nobody to start it later. A slow tick is enough for both — the work here is a couple
/// of indexed queries.
/// </para>
/// </summary>
public class AcquisitionQueueTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
    : AbstractPredefinedBTaskBuilder(serviceProvider, localizer)
{
    public override string Id => "AcquisitionQueue";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => TimeSpan.FromSeconds(30);

    /// <summary>
    /// It is bookkeeping, not work the user asked for; keeping it out of the task list stops the
    /// panel filling up with a row that says "nothing happened" every half minute.
    /// </summary>
    public override bool IsPersistent => false;

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var acquisitions = scope.ServiceProvider.GetService<IAcquisitionService>();

        if (acquisitions is not IAcquisitionQueue queue) return;

        try
        {
            await queue.ReconcileAsync(args.CancellationToken);
            await queue.PumpAsync(args.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            scope.ServiceProvider.GetRequiredService<ILogger<AcquisitionQueueTask>>()
                .LogError(ex, "[Acquisition] The queue tick failed");
        }
    }
}

/// <summary>
/// The half of the acquisition service that only the queue tick calls. Separated so the shape of
/// what a user can ask for stays free of the machinery that keeps it running.
/// </summary>
public interface IAcquisitionQueue
{
    /// <summary>Brings task rows back in line with the runs that do the work.</summary>
    Task ReconcileAsync(CancellationToken ct = default);

    /// <summary>Starts queued acquisitions while there is room under the concurrency limit.</summary>
    Task PumpAsync(CancellationToken ct = default);
}
