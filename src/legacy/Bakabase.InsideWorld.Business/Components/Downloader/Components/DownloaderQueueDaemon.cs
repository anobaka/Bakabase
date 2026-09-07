using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Keeps the download queue moving.
    ///
    /// The queue used to advance only as a side effect of a downloader reporting a terminal status.
    /// That made a single dropped event fatal: nothing else ever looked at the queue again, so the
    /// remaining tasks sat idle — often next to the step text of the download that died — until the
    /// user pressed "start all" by hand. (<c>DownloaderTriggerJob</c> was written to be that periodic
    /// safety net but was never scheduled: nothing in the app registers Quartz, so it has always been
    /// dead code.)
    ///
    /// This daemon is that safety net, and nothing more. It never starts work the scheduler would not
    /// have started on its own; it just makes sure somebody asks.
    /// </summary>
    public class DownloaderQueueDaemon(IServiceProvider serviceProvider, ILogger<DownloaderQueueDaemon> logger)
        : BackgroundService
    {
        /// <summary>
        /// How often the queue is re-examined. Frequent enough that a missed event costs seconds
        /// rather than a session, rare enough to be invisible next to real download work.
        /// </summary>
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Let the app finish starting before touching the queue — the first tick would otherwise
        /// race the initial task load.
        /// </summary>
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

        /// <summary>
        /// How long a downloader may show no sign of life at all — no progress, no step change, no
        /// status change — before it is treated as wedged rather than slow.
        ///
        /// Generous on purpose. A single ExHentai request can legitimately take minutes (the handler
        /// paces requests a second apart and retries timeouts three times), so this must be well
        /// clear of the slowest honest step; the cost of being wrong is only that a live download is
        /// requeued and picked up again.
        /// </summary>
        private static readonly TimeSpan StallThreshold = TimeSpan.FromMinutes(15);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    // A daemon that dies on one bad tick is worse than no daemon: the queue would go
                    // back to being one dropped event away from frozen, silently.
                    logger.LogError(e, "The download queue daemon failed a tick");
                }

                try
                {
                    await Task.Delay(TickInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task TickAsync(CancellationToken ct)
        {
            await using var scope = serviceProvider.CreateAsyncScope();

            var manager = scope.ServiceProvider.GetRequiredService<DownloaderManager>();
            var released = await manager.ReleaseStalledDownloaders(StallThreshold);

            ct.ThrowIfCancellationRequested();

            var service = scope.ServiceProvider.GetRequiredService<DownloadTaskService>();
            await service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null,
                DownloadTaskActionOnConflict.Ignore);

            if (released.Count > 0)
            {
                // Only worth a line when something was actually wrong; a healthy tick stays silent.
                logger.LogInformation("Released {Count} stalled download task(s) and re-ran the queue",
                    released.Count);
            }
        }
    }
}
