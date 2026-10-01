using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai
{
    /// <summary>
    /// Answers ExHentai's torrent-priority questions for the whole queue at once.
    ///
    /// Two of them, and both used to cost a full start/stop lifecycle per task:
    ///
    /// 1. <em>Is this task already done?</em> A persisted result with verified metadata and a
    ///    matching user copy can answer this without starting the producer. A title or timestamp
    ///    alone cannot establish ownership or prove that the files remain available.
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
        ILogger<ExHentaiDownloadTaskPrecheck> logger,
        IServiceScopeFactory? scopes = null) : IDownloadTaskPrecheck
    {
        public ThirdPartyId ThirdPartyId => ThirdPartyId.ExHentai;

        public async Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct)
        {
            var exOptions = options.Value;
            var verdicts = new Dictionary<int, DownloadTaskPrecheckVerdict>();
            // The precheck is shared by the queue; each evaluation needs its own scoped DbContext.
            await using var scope = scopes?.CreateAsyncScope();
            var results = scope?.ServiceProvider.GetRequiredService<DownloadResultService>();

            foreach (var task in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var taskOptions = task.GetTypedOptions<ExHentaiTaskOptions>();

                var satisfied = await IsTorrentAlreadyDownloaded(task, taskOptions, results, ct);

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

            return verdicts;
        }

        /// <summary>
        /// Why this task has nothing left to download, or null if it still has work.
        /// </summary>
        /// <remarks>
        /// A timestamp alone cannot prove files are still available. Both the managed metadata
        /// and user copy must still match the source result; otherwise the producer recovers them.
        ///
        /// Deliberately not <see cref="ExHentaiTaskOptions.TorrentFoundAt"/>, which is stamped when a
        /// torrent is *seen*, before a download that may still fail — reading it here would complete
        /// tasks that never got their file.
        /// </remarks>
        private static async Task<string?> IsTorrentAlreadyDownloaded(DownloadTask task, ExHentaiTaskOptions taskOptions,
            DownloadResultService? results, CancellationToken ct)
        {
            // A task that opted out of torrents downloads images; none of this says anything about it.
            // A configured handoff must pass through the producer, which durably records or
            // recovers the metadata before completion. A file or old stamp alone is insufficient.
            if (taskOptions.DownloadResultWorkflowId.HasValue ||
                !taskOptions.PreferTorrent || task.Type != (int) ExHentaiDownloadTaskType.SingleWork)
            {
                return null;
            }

            if (!taskOptions.TorrentDownloadedAt.HasValue || results == null) return null;
            string sourceKey;
            try { sourceKey = ExHentaiDownloadResultHelper.NormalizeSourceKey(task.Key); }
            catch (ArgumentException) { return null; }
            var result = await results.GetLatestBySourceAsync(task.Id, sourceKey, ct);
            if (result?.Kind != DownloadResultKind.TorrentMetadata || !await results.CanReuseAsync(result, ct)) return null;
            var userCopy = ExHentaiDownloadResultHelper.GetTorrentDownloadPath(result);
            return userCopy != null && await DownloadResultService.ReadReusableTorrentMetadataAsync(result, ct, userCopy) != null
                ? "torrent already downloaded" : null;
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
