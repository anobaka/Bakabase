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
    /// 1. <em>Is this task already done?</em> A SingleWork task that has downloaded its torrent
    ///    leaves the file on disk, and the downloader's own first act is to notice that and return.
    ///    Reaching that point costs a downloader, a background task, a database write, a UI push and
    ///    a detail-page request paced a second behind the last one — for a task with nothing to do.
    ///    Across a re-run of a thousand mostly-finished tasks that is the entire run. Here it is one
    ///    directory listing per download folder, shared by every task in it.
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

            // One listing per distinct download folder, built lazily so a queue that needs no
            // satisfied-check touches the disk not at all.
            var torrentsByFolder = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var task in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var taskOptions = task.GetTypedOptions<ExHentaiTaskOptions>();

                var satisfied = IsTorrentAlreadyDownloaded(task, taskOptions, torrentsByFolder);

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
        /// with no I/O — that is what the stamp exists for. The folder listing below is only for tasks
        /// downloaded before that stamp existed: they carry no record at all, and they are exactly the
        /// backlog this pre-check was added to spare.
        ///
        /// Deliberately not <see cref="ExHentaiTaskOptions.TorrentFoundAt"/>, which is stamped when a
        /// torrent is *seen*, before a download that may still fail — reading it here would complete
        /// tasks that never got their file.
        /// </remarks>
        private string? IsTorrentAlreadyDownloaded(DownloadTask task, ExHentaiTaskOptions taskOptions,
            IDictionary<string, HashSet<string>> torrentsByFolder)
        {
            // A task that opted out of torrents downloads images; none of this says anything about it.
            // A configured handoff must pass through the producer, which durably records or
            // recovers the metadata before completion. A file or old stamp alone is insufficient.
            if (taskOptions.DownloadResultWorkflowId.HasValue ||
                !taskOptions.PreferTorrent || task.Type != (int) ExHentaiDownloadTaskType.SingleWork)
            {
                return null;
            }

            if (taskOptions.TorrentDownloadedAt.HasValue)
            {
                return "torrent already downloaded";
            }

            // Fallback, and only decidable once the task has run far enough to learn the gallery's
            // name — that name *is* the file name (see AbstractExHentaiDownloader.DownloadSingleWork).
            // A task that has never run has no name, so it is never skipped, which is the correct
            // answer rather than a limitation.
            if (task.Name.IsNullOrEmpty() || task.DownloadPath.IsNullOrEmpty())
            {
                return null;
            }

            var expected = $"{task.Name!.RemoveInvalidFileNameChars()}.torrent";

            return ListTorrents(task.DownloadPath, torrentsByFolder).Contains(expected)
                ? "torrent file found in the download folder"
                : null;
        }

        private HashSet<string> ListTorrents(string folder, IDictionary<string, HashSet<string>> cache)
        {
            if (cache.TryGetValue(folder, out var names))
            {
                return names;
            }

            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (Directory.Exists(folder))
                {
                    foreach (var file in Directory.EnumerateFiles(folder, "*.torrent", SearchOption.TopDirectoryOnly))
                    {
                        names.Add(Path.GetFileName(file));
                    }
                }
            }
            catch (Exception e)
            {
                // An unreadable folder just means nothing can be skipped from it. Downloading a
                // torrent that already exists is cheap; wrongly skipping one is not.
                logger.LogWarning(e, "Could not list torrents in {Folder}; not skipping its tasks", folder);
            }

            cache[folder] = names;

            return names;
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
