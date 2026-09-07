using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    /// <summary>
    /// Runs the registered <see cref="IDownloadTaskPrecheck"/>s and remembers what they concluded.
    ///
    /// A scheduling pass happens after every task transition, so with a large queue the same
    /// questions get asked hundreds of times a minute. Caching them is what turns the pre-check from
    /// a nice abstraction into an actual saving — but a stale answer is worse than no answer, so a
    /// snapshot is only reused while three things hold: it is younger than <see cref="Ttl"/>, the
    /// tasks it covers have not changed, and nobody has invalidated it explicitly (an options change,
    /// a task edit, a verdict being written).
    /// </summary>
    public class DownloadTaskPrecheckRunner(
        IEnumerable<IDownloadTaskPrecheck> prechecks,
        ILogger<DownloadTaskPrecheckRunner> logger)
    {
        /// <summary>
        /// How long a snapshot may be reused. Short enough that anything the pre-check cannot observe
        /// — a file appearing on disk, a torrent being posted — is picked up within a minute, long
        /// enough to absorb the burst of scheduling passes a large queue produces.
        /// </summary>
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

        private readonly Dictionary<ThirdPartyId, IDownloadTaskPrecheck> _prechecks =
            prechecks.ToDictionary(p => p.ThirdPartyId);

        private readonly ConcurrentDictionary<ThirdPartyId, Snapshot> _snapshots = new();

        /// <summary>
        /// Bumped whenever anything a pre-check might have read has changed. Compared rather than
        /// cleared so an invalidation racing an in-flight evaluation cannot be lost: an evaluation
        /// that started before the bump is simply not stored.
        /// </summary>
        private long _version;

        private sealed record Snapshot(
            long Version,
            DateTime EvaluatedAt,
            IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict> Verdicts,
            HashSet<int> CoveredTaskIds);

        /// <summary>
        /// Discards every cached snapshot. Call this whenever task options, source options or the
        /// task set itself change — a wrong skip is far more expensive than a recomputation.
        /// </summary>
        public void Invalidate() => Interlocked.Increment(ref _version);

        /// <summary>
        /// Verdicts for <paramref name="candidates"/>, grouped by source. Tasks with no registered
        /// pre-check, and any pre-check that throws, come back as
        /// <see cref="DownloadTaskPrecheckOutcome.Run"/> — the pre-check is an optimisation, never a
        /// gate, so its failure must cost speed and nothing else.
        /// </summary>
        public async Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateAsync(
            IReadOnlyList<DownloadTask> candidates, CancellationToken ct = default)
        {
            var result = new Dictionary<int, DownloadTaskPrecheckVerdict>();

            foreach (var group in candidates.GroupBy(c => c.ThirdPartyId))
            {
                if (!_prechecks.TryGetValue(group.Key, out var precheck))
                {
                    continue;
                }

                var tasks = group.ToArray();

                foreach (var (taskId, verdict) in await EvaluateSourceAsync(precheck, tasks, ct))
                {
                    result[taskId] = verdict;
                }
            }

            return result;
        }

        private async Task<IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict>> EvaluateSourceAsync(
            IDownloadTaskPrecheck precheck, IReadOnlyList<DownloadTask> tasks, CancellationToken ct)
        {
            var version = Interlocked.Read(ref _version);
            var ids = tasks.Select(t => t.Id).ToHashSet();

            if (_snapshots.TryGetValue(precheck.ThirdPartyId, out var cached) &&
                cached.Version == version &&
                DateTime.Now - cached.EvaluatedAt < Ttl &&
                // A snapshot only answers for the tasks it looked at. A pass over a wider set (a task
                // was added, or a filter was lifted) must re-evaluate rather than silently treat the
                // newcomers as ordinary.
                ids.IsSubsetOf(cached.CoveredTaskIds))
            {
                return cached.Verdicts;
            }

            IReadOnlyDictionary<int, DownloadTaskPrecheckVerdict> verdicts;
            try
            {
                verdicts = await precheck.EvaluateAsync(tasks, ct);
            }
            catch (Exception e)
            {
                logger.LogError(e, "The {ThirdPartyId} download pre-check failed; scheduling normally",
                    precheck.ThirdPartyId);
                return new Dictionary<int, DownloadTaskPrecheckVerdict>();
            }

            // Only cache when nothing invalidated us while we were evaluating.
            if (Interlocked.Read(ref _version) == version)
            {
                _snapshots[precheck.ThirdPartyId] = new Snapshot(version, DateTime.Now, verdicts, ids);
            }

            return verdicts;
        }
    }
}
