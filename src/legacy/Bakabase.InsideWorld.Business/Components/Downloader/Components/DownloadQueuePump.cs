using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Serialises and coalesces "look at the queue again" requests.
    ///
    /// A task finishing used to run the next scheduling pass inline, from inside the status-change
    /// handler that announced it. So starting the next task happened <em>underneath</em> the previous
    /// one's completion, and if that task also finished immediately — which is the norm when a queue
    /// is being drained of tasks that turn out to have nothing to do — the passes nested. Hundreds of
    /// tasks meant hundreds of frames of live stack, a scheduling pass re-entered while an earlier one
    /// was still choosing what to run, and a burst of duplicate database reads and UI pushes for what
    /// is only ever one question: what should run next?
    ///
    /// Here that question is asked at most once at a time. Requests arriving during a pass collapse
    /// into a single follow-up pass, so a hundred simultaneous completions cost two passes rather than
    /// a hundred nested ones — and no request is ever dropped, which matters because a missed pass is
    /// a queue that stops until something else happens to poke it.
    /// </summary>
    public class DownloadQueuePump(IServiceProvider serviceProvider, ILogger<DownloadQueuePump> logger)
    {
        private int _requested;
        private int _running;

        /// <summary>
        /// Asks for a scheduling pass. Returns immediately; the pass runs on its own. Safe to call
        /// from anywhere, including from inside a pass.
        /// </summary>
        public void Request()
        {
            Interlocked.Exchange(ref _requested, 1);

            if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            {
                // A loop is already running and will pick the flag up before it exits.
                return;
            }

            _ = Task.Run(RunAsync);
        }

        /// <summary>One scheduling pass. Virtual so the coalescing above can be tested on its own.</summary>
        protected virtual async Task RunPassAsync()
        {
            await using var scope = serviceProvider.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<DownloadTaskService>();
            await service.TryStartAllTasks(DownloadTaskStartMode.AutoStart, null,
                DownloadTaskActionOnConflict.Ignore);
        }

        private async Task RunAsync()
        {
            try
            {
                while (Interlocked.Exchange(ref _requested, 0) == 1)
                {
                    try
                    {
                        await RunPassAsync();
                    }
                    catch (Exception e)
                    {
                        // Keep looping: giving up on one bad pass would leave the queue idle until
                        // something else happened to request another.
                        logger.LogError(e, "A download scheduling pass failed");
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);

                // A request that arrived between the loop's last check and clearing the flag above
                // would otherwise be the one that never gets served.
                if (Interlocked.CompareExchange(ref _requested, 0, 0) == 1)
                {
                    Request();
                }
            }
        }
    }
}
