using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    /// <summary>
    /// Answers ExHentai's torrent-priority questions for the whole queue at once.
    ///
    /// Two of them, and both used to cost a full start/stop lifecycle per task:
    ///
    /// 1. <em>Is this task already done?</em> A persisted torrent-download stamp can answer this
    ///    without starting the producer. A file with the same title cannot: another gallery may
    ///    have written it, so an unstamped task must run and verify its source identity.
    ///
    /// 2. <em>Will this task download images rather than a torrent?</em> Those go last under
    ///    torrent-priority. Unchanged in meaning from the per-task check it replaces — it is the same
    ///    predicate, asked once for the set instead of once per scheduling pass per task.
    ///
    /// Read-only by contract: it never probes the network and never writes. Anything it cannot settle
    /// locally stays <see cref="DownloadTaskPrecheckOutcome.Run"/> and the downloader decides.
    /// </summary>
    public class ExHentaiDownloadTaskPrecheck(
        IBOptions<ExHentaiOptions> options,
        ITransientTorrentVerdictCache torrentVerdicts,
        ILogger<ExHentaiDownloadTaskPrecheck> logger) : IDownloadTaskPrecheck
    {
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;

        public Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct)
        {
            var exOptions = options.Value;
            var verdicts = new Dictionary<int, DownloadTaskPrecheckVerdict>();

            foreach (var task in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var taskOptions = task.GetTypedOptions<ExHentaiTaskOptions>();

                var satisfied = IsTorrentAlreadyDownloaded(task, taskOptions);

                if (satisfied != null)
                {
                    verdicts[task.Id] =
                        new DownloadTaskPrecheckVerdict(DownloadTaskPrecheckOutcome.AlreadySatisfied, satisfied);
                    continue;
                }

                // Ordering only matters while torrent-priority is on; otherwise every task is equal
                // and the queue keeps its plain FIFO order.
                if (exOptions.PrioritizeTasksWithTorrent &&
                    IsImageOnly(task, taskOptions, exOptions.TorrentCheckValidityHours))
                {
                    verdicts[task.Id] = new DownloadTaskPrecheckVerdict(
                        DownloadTaskPrecheckOutcome.Defer, "will download images, not a torrent");
                }
            }

            return Task.FromResult<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>>(verdicts);
        }

        /// <summary>
        /// Why this task has nothing left to download, or null if it still has work.
        /// </summary>
        /// <remarks>
        /// The task's own <see cref="ExHentaiTaskOptions.TorrentDownloadedAt"/> answers this outright,
        /// with no I/O — that is what the stamp exists for. An old file without a source marker
        /// cannot prove which same-named gallery it came from.
        ///
        /// Deliberately not <see cref="ExHentaiTaskOptions.TorrentFoundAt"/>, which is stamped when a
        /// torrent is *seen*, before a download that may still fail — reading it here would complete
        /// tasks that never got their file.
        /// </remarks>
        private static string? IsTorrentAlreadyDownloaded(DownloadTask task, ExHentaiTaskOptions taskOptions)
        {
            // A task that opted out of torrents downloads images; none of this says anything about it.
            // A configured handoff must pass through the producer, which durably records or
            // recovers the metadata before completion. A file or old stamp alone is insufficient.
            if (taskOptions.DownloadResultWorkflowId.HasValue ||
                !taskOptions.PreferTorrent || task.Type != (int) ExHentaiDownloadTaskType.SingleWork)
            {
                return null;
            }

            return taskOptions.TorrentDownloadedAt.HasValue ? "torrent already downloaded" : null;
        }

        /// <summary>
        /// True when this task will end up downloading images: it has been probed and has no torrent,
        /// or it opts out of torrents, in which case we honour that and download its images.
        /// </summary>
        private bool IsImageOnly(DownloadTask task, ExHentaiTaskOptions taskOptions, int? torrentCheckValidityHours)
        {
            if (torrentVerdicts.IsKnownNoTorrent(task.Id))
            {
                return true;
            }

            if (!taskOptions.PreferTorrent)
            {
                return true;
            }

            // A still-valid persisted verdict counts the same as an in-memory one, so ordering
            // survives a restart instead of every task looking un-probed again.
            return ExHentaiTorrentCheckPolicy.IsNoTorrentVerdictFresh(taskOptions.NoTorrentCheckedAt,
                torrentCheckValidityHours, DateTime.Now);
        }
    }
}
