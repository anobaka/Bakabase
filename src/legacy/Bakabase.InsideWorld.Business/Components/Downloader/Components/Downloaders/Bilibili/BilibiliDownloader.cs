using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.FfMpeg;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.Bilibili
{
    /// <summary>
    /// Downloads a Bilibili favorites folder through the API: pages the folder, keeps the range checkpoint, names
    /// files exactly as before (so existing libraries are recognised), and records every item it skips on purpose
    /// as a notice. The per-page protocol (streams, CDN, merge, captions) lives in
    /// <see cref="BilibiliVideoDownloadService"/>.
    /// </summary>
    /// <remarks>
    /// <para>Failures: definite content states (deleted, supporter-only, not supported yet…) are skipped with a
    /// notice and the task completes. Risk control, busy services and flaky networks re-run the task from its
    /// checkpoint (risk control after 10 and 30 minutes, the rest after 30 s / 2 min / 5 min); partial downloads
    /// are kept and resumed. An expired login, a missing folder, a missing ffmpeg, a full disk and protocol
    /// changes fail the task without moving the checkpoint.</para>
    /// <para>This class never reads the account cookie: only the API client attaches it.</para>
    /// </remarks>
    public class BilibiliDownloader(
        IServiceProvider serviceProvider,
        BilibiliClient client,
        BilibiliVideoDownloadService videoService,
        FfMpegService ffMpegService,
        IDownloaderLocalizer localizer) : AbstractDownloader<BilibiliDownloadTaskType>(serviceProvider)
    {
        /// <summary>Work folders (partial streams) untouched for this long are deleted at the start of a task.</summary>
        public static readonly TimeSpan WorkDirectoryMaxAge = TimeSpan.FromDays(7);

        /// <summary>
        /// Items in a row that only came out "unavailable" (playurl -404 without a PGC redirect, no CDN host would
        /// serve them) before the run stops: that many is more likely a change on Bilibili's side than bad luck.
        /// </summary>
        public const int MaxUnavailableItemsInARow = 3;

        /// <summary>Pages beyond what the folder's <c>media_count</c> accounts for before paging is given up.</summary>
        public const int ExtraFavoritesPages = 5;

        /// <summary>Re-runs after risk control: Bilibili's blocks last minutes, not seconds.</summary>
        public static readonly IReadOnlyList<TimeSpan> RiskControlRetryDelays =
            [TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30)];

        /// <summary>Re-runs after any other transient failure (busy service, CDN, network, ffmpeg installing).</summary>
        public static readonly IReadOnlyList<TimeSpan> OtherTransientRetryDelays =
            [TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];

        public override ThirdPartyId ThirdPartyId => ThirdPartyId.Bilibili;
        public override BilibiliDownloadTaskType EnumTaskType => BilibiliDownloadTaskType.Favorites;

        protected override IReadOnlyList<TimeSpan> TransientFailureRetryDelays => OtherTransientRetryDelays;

        /// <summary>Favorites list pages already fetched in this start, so a re-run does not ask for them again.</summary>
        private readonly Dictionary<(long FavoritesId, int Page), FavoriteItemSearchResponseData> _listPages = new();

        private int _riskControlRetries;
        private int _otherTransientRetries;

        protected override void OnStarting()
        {
            lock (_listPages)
            {
                _listPages.Clear();
            }

            _riskControlRetries = 0;
            _otherTransientRetries = 0;
        }

        protected override string? GetNoticesFooter() => localizer.BilibiliSkipFooter();

        protected override TransientRetry? GetTransientRetry(Exception e, int attempt)
        {
            if (IsRiskControl(e))
            {
                var n = ++_riskControlRetries;
                return n <= RiskControlRetryDelays.Count
                    ? new TransientRetry(RiskControlRetryDelays[n - 1], n, RiskControlRetryDelays.Count)
                    : null;
            }

            var m = ++_otherTransientRetries;
            return m <= OtherTransientRetryDelays.Count
                ? new TransientRetry(OtherTransientRetryDelays[m - 1], m, OtherTransientRetryDelays.Count)
                : null;
        }

        protected override string DescribeTransientRetryWait(Exception e, TransientRetry retry, TimeSpan remaining) =>
            IsRiskControl(e)
                ? localizer.BilibiliRiskControlWaiting((int) Math.Ceiling(Math.Max(0, remaining.TotalMinutes)),
                    retry.Retry, retry.MaxRetries)
                : base.DescribeTransientRetryWait(e, retry, remaining);

        protected override async Task StartCore(DownloadTask task, CancellationToken ct)
        {
            try
            {
                await DownloadFavoritesAsync(task, ct);
            }
            catch (Exception e) when (FindCause<BilibiliTemporarilyUnavailableException>(e) is
                                          {Kind: BilibiliTemporaryFailureKind.RiskControl} risk &&
                                      e is not BilibiliDownloadInterruptedException)
            {
                throw new BilibiliDownloadInterruptedException(
                    localizer.BilibiliRiskControl(risk.Code ?? risk.HttpStatus), e);
            }
            catch (Exception e) when (FindCause<BilibiliNotLoggedInException>(e) != null &&
                                      e is not BilibiliDownloadInterruptedException)
            {
                throw new BilibiliDownloadInterruptedException(localizer.BilibiliNotLoggedIn(), e);
            }
            catch (Exception e) when (FindCause<DiskWriteException>(e) is {IsDiskFull: true} disk &&
                                      e is not BilibiliDownloadInterruptedException)
            {
                throw new BilibiliDownloadInterruptedException(localizer.BilibiliDiskFull(disk.Path), e);
            }
        }

        private async Task DownloadFavoritesAsync(DownloadTask task, CancellationToken ct)
        {
            // Nothing can be saved without ffmpeg; find out before spending any request.
            await EnsureFfMpegReadyAsync(ct);

            var tempRoot = string.IsNullOrWhiteSpace(task.DownloadPath)
                ? null
                : Path.Combine(task.DownloadPath, "temp");
            if (tempRoot != null)
            {
                PruneWorkDirectories(tempRoot, DateTime.UtcNow, WorkDirectoryMaxAge, Logger);
            }

            var favorites = await client.GetFavorites(ct);
            var target = long.TryParse(task.Key, out var favoritesId)
                ? favorites.FirstOrDefault(f => f.Id == favoritesId)
                : null;
            if (target == null)
            {
                throw new BilibiliFavoritesNotFoundException(localizer.BilibiliFavoritesNotFound(task.Key, task.Name));
            }

            await OnNameAcquiredInternal(target.Title);

            // Page work folders stay between runs (partial streams are resumed); PruneWorkDirectories removes
            // abandoned ones.
            var workRoot = Path.Combine(tempRoot ?? "temp", target.Id.ToString());

            var checkpoint = new RangeCheckpointContext(task.Checkpoint);
            var mediaCount = target.MediaCount;
            var page = Math.Max(task.StartPage ?? 1, 1);
            var done = 0;
            // Items since the last definite outcome that only came out "unavailable": the checkpoint is not moved
            // past them until a later item settles, so a Bilibili-side outage does not bury them.
            var pendingUnavailable = 0;

            await OnProgressInternal(0);

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var maxPage = (int) Math.Ceiling(mediaCount / (double) BiliBiliApiUrls.FavPageSize) + ExtraFavoritesPages;
                if (page > maxPage)
                {
                    Logger.LogWarning(
                        "Bilibili favorites {FavoritesId} still reports more items on page {Page} although it holds {MediaCount}; stopping without completing the checkpoint",
                        target.Id, page, mediaCount);
                    throw new BilibiliProtocolException(BilibiliClient.FavoriteItemsEndpoint,
                        $"has_more is still true on page {page} of a folder with {mediaCount} items");
                }

                var list = await GetListPageAsync(target.Id, page, ct);
                if (list.Info?.MediaCount is { } reported && reported > mediaCount)
                {
                    mediaCount = reported;
                }

                var total = Math.Max(mediaCount, 1);
                var finished = false;
                foreach (var item in list.Medias ?? [])
                {
                    ct.ThrowIfCancellationRequested();
                    var id = item.Id.ToString();
                    var action = checkpoint.Analyze(id);
                    NextCheckpoint = checkpoint.BuildCheckpoint(id);
                    var advance = true;
                    switch (action)
                    {
                        case RangeCheckpointContext.AnalyzeResult.AllTaskIsDone:
                            finished = true;
                            break;
                        case RangeCheckpointContext.AnalyzeResult.Skip:
                            Current = $"[{done + 1}/{total}][{item.Upper?.Name}]{item.Title}";
                            await OnCurrentChangedInternal();
                            // Moving the checkpoint here would also cover the unavailable items before it.
                            advance = pendingUnavailable == 0;
                            break;
                        case RangeCheckpointContext.AnalyzeResult.Download:
                            if (await ProcessItemAsync(task, item, done, total, workRoot, ct) ==
                                ItemOutcome.UnavailableOnly)
                            {
                                advance = false;
                                if (++pendingUnavailable >= MaxUnavailableItemsInARow)
                                {
                                    throw new BilibiliProtocolException(BilibiliClient.PlayUrlEndpoint,
                                        $"{pendingUnavailable} items in a row were unavailable; stopped without advancing the checkpoint");
                                }
                            }
                            else
                            {
                                pendingUnavailable = 0;
                            }

                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(action), action, null);
                    }

                    if (finished)
                    {
                        break;
                    }

                    done++;
                    if (advance)
                    {
                        await OnCheckpointChangedInternal(checkpoint.BuildCheckpoint(id));
                    }

                    await OnProgressInternal(Math.Clamp((decimal) done / total * 100, 0, 100));
                }

                // An empty page with has_more is normal (the list drops some dead items): keep paging.
                if (finished || !list.HasMore)
                {
                    if (pendingUnavailable > 0)
                    {
                        Logger.LogInformation(
                            "Bilibili favorites {FavoritesId}: the last {Count} item(s) were unavailable; the checkpoint is left before them so the next run checks them again",
                            target.Id, pendingUnavailable);
                    }
                    else
                    {
                        await OnCheckpointChangedInternal(checkpoint.BuildCheckpointOnComplete()!);
                    }

                    await OnProgressInternal(100);
                    break;
                }

                page++;
            }
        }

        private enum ItemOutcome
        {
            /// <summary>Downloaded, found on disk, or skipped for a definite reason.</summary>
            Settled = 1,

            /// <summary>Every page was skipped as unavailable (<see cref="IsUnavailable"/>).</summary>
            UnavailableOnly = 2,
        }

        /// <summary>
        /// Downloads every page of one favorites item, or records why not.
        /// </summary>
        /// <remarks>
        /// Checkpoint semantics: an item skipped for a definite reason advances the checkpoint like a downloaded
        /// one, and once a run completes the checkpoint is <c>{firstId}-</c>, so later runs only look at newly
        /// added items. Skipped items are therefore not revisited (e.g. after the user starts supporting a
        /// creator); clearing the checkpoint makes the next run look at everything again, at one API request per
        /// page that is already on disk. Items that only came out unavailable (<see cref="IsUnavailable"/>) are
        /// the exception: they do not move the checkpoint until a later item settles.
        /// </remarks>
        private async Task<ItemOutcome> ProcessItemAsync(DownloadTask task, FavoriteItem item, int index, int total,
            string workRoot, CancellationToken ct)
        {
            Current = $"[{index + 1}/{total}][{item.Upper?.Name}]{item.Title}";
            await OnCurrentChangedInternal();

            var classification = BilibiliFavoriteItemClassifier.Classify(item);
            if (classification.Skip != null)
            {
                Notice(item, null, classification.Skip);
                return IsUnavailable(classification.Skip) ? ItemOutcome.UnavailableOnly : ItemOutcome.Settled;
            }

            var resolution = await videoService.ResolveArchiveAsync(item, ct);
            if (resolution.Skip != null)
            {
                Notice(item, null, resolution.Skip);
                return IsUnavailable(resolution.Skip) ? ItemOutcome.UnavailableOnly : ItemOutcome.Settled;
            }

            var archive = resolution.Archive!;
            byte[]? cover = null;
            var coverLoaded = false;
            var unavailablePages = 0;
            var settledPages = 0;
            // What the folder-wide progress already says for this item; page reports only move it forward.
            var lastGlobalProgress = ComputeGlobalProgress(0, 1, archive.Pages.Count, index, total);
            foreach (var page in archive.Pages)
            {
                ct.ThrowIfCancellationRequested();
                var currentWritten = false;
                var job = new BilibiliPageJob(archive.Aid, page.Cid, archive.IsPgcRedirect, archive.AccessSkip,
                    Path.Combine(workRoot, page.Cid.ToString()),
                    ResolveTarget: async (qualityName, _) =>
                    {
                        // Byte-identical to the previous (external-tool) downloader: existing libraries are
                        // matched by this name.
                        var values = new Dictionary<BilibiliNamingFields, object?>
                        {
                            {BilibiliNamingFields.UploaderId, item.Upper?.Mid},
                            {BilibiliNamingFields.UploaderName, item.Upper?.Name},
                            {BilibiliNamingFields.AId, item.Id},
                            {BilibiliNamingFields.BvId, item.BvId},
                            {BilibiliNamingFields.PostTitle, item.Title},
                            {BilibiliNamingFields.CId, page.Cid},
                            {BilibiliNamingFields.PartNo, page.PartNo},
                            {BilibiliNamingFields.PartName, page.PartName},
                            {BilibiliNamingFields.QualityName, qualityName},
                            {BilibiliNamingFields.Extension, ".mp4"},
                        };

                        // The first call carries the real quality name; later calls only probe for a file
                        // downloaded under another one.
                        if (!currentWritten)
                        {
                            currentWritten = true;
                            Current =
                                $"[{index + 1}/{total}][{item.Upper?.Name}]{item.Title} - P{page.PartNo} - {qualityName}";
                            await OnCurrentChangedInternal();
                        }

                        var fullname = Path.Combine(task.DownloadPath, await BuildDownloadFilename(values));
                        return new BilibiliPageTarget(fullname, File.Exists(fullname));
                    },
                    OnProgress: async pageProgress =>
                    {
                        // Bytes moving are a sign of life even when the folder-wide percentage does not change
                        // (one page of a large folder can be far below 0.01%).
                        Touch();
                        var global = ComputeGlobalProgress(pageProgress, page.PartNo, archive.Pages.Count, index,
                            total);
                        if (global != lastGlobalProgress)
                        {
                            lastGlobalProgress = global;
                            await OnProgressInternal(global);
                        }
                    },
                    GetCover: async coverCt =>
                    {
                        if (!coverLoaded)
                        {
                            cover = await videoService.DownloadCoverAsync(archive.CoverUrl, coverCt);
                            coverLoaded = true;
                        }

                        return cover;
                    },
                    CoverUrl: archive.CoverUrl);

                var outcome = await videoService.DownloadPageAsync(job, ct);
                if (outcome is {Status: BilibiliPageStatus.Skipped, Skip: { } skip})
                {
                    Notice(item, archive.Pages.Count > 1 ? page : null, skip);
                    if (IsUnavailable(skip))
                    {
                        unavailablePages++;
                        continue;
                    }
                }

                settledPages++;
            }

            return unavailablePages > 0 && settledPages == 0 ? ItemOutcome.UnavailableOnly : ItemOutcome.Settled;
        }

        private void Notice(FavoriteItem item, BilibiliArchivePage? page, BilibiliSkip skip)
        {
            var message = string.IsNullOrWhiteSpace(skip.Message) ? null : BilibiliDiagnostics.RedactText(skip.Message);
            AddNotice(localizer.BilibiliSkipNotice(FormatSubject(item, page),
                localizer.DescribeBilibiliSkip(skip.Reason, skip.Code, message)));
            Logger.LogInformation("Skipped Bilibili item {Aid} (cid {Cid}): {Reason} ({Code})", item.Id, page?.Cid,
                skip.Reason, skip.Code);
        }

        /// <summary>
        /// Skips that may be temporary on Bilibili's side rather than a state of the video: they do not advance
        /// the checkpoint on their own, and several in a row stop the run.
        /// </summary>
        internal static bool IsUnavailable(BilibiliSkip skip) =>
            skip.Reason is BilibiliSkipReason.Unavailable or BilibiliSkipReason.CdnUnavailable;

        private async Task<FavoriteItemSearchResponseData> GetListPageAsync(long favoritesId, int page,
            CancellationToken ct)
        {
            lock (_listPages)
            {
                if (_listPages.TryGetValue((favoritesId, page), out var cached))
                {
                    return cached;
                }
            }

            var fresh = await client.GetPostsInFavorites(favoritesId, page, ct);
            lock (_listPages)
            {
                _listPages[(favoritesId, page)] = fresh;
            }

            return fresh;
        }

        private async Task EnsureFfMpegReadyAsync(CancellationToken ct)
        {
            try
            {
                await ffMpegService.EnsureReadyAsync(ct);
            }
            catch (DependencyNotInstalledException e) when (ffMpegService.Status == DependentComponentStatus.Installing)
            {
                // It will be there shortly: run again later instead of failing for good.
                throw new BilibiliDependencyNotReadyException(e.Message, e);
            }
        }

        /// <summary>The folder-wide percentage while page <paramref name="partNo"/> of item
        /// <paramref name="itemIndex"/> is at <paramref name="pageProgress"/> percent.</summary>
        internal static decimal ComputeGlobalProgress(decimal pageProgress, int partNo, int partCount, int itemIndex,
            int itemCount)
        {
            itemCount = Math.Max(itemCount, 1);
            partCount = Math.Max(partCount, 1);
            var itemUnit = 100m / itemCount;
            var partUnit = itemUnit / partCount;
            var global = itemUnit * itemIndex + partUnit * Math.Max(partNo - 1, 0) +
                         partUnit * Math.Clamp(pageProgress, 0, 100) / 100;
            return Math.Clamp(Math.Floor(global * 100) / 100, 0, 100);
        }

        /// <summary><c>[BV…] Title</c>, plus <c> P{n} {part name}</c> for one page of a multi-page video.</summary>
        internal static string FormatSubject(FavoriteItem item, BilibiliArchivePage? page)
        {
            var id = string.IsNullOrEmpty(item.BvId) ? $"av{item.Id}" : item.BvId;
            var subject = $"[{id}] {item.Title}".TrimEnd();
            return page == null ? subject : $"{subject} P{page.PartNo} {page.PartName}".TrimEnd();
        }

        /// <summary>
        /// Deletes work folders nobody has written to for <paramref name="maxAge"/>: under
        /// <paramref name="tempRoot"/> (<c>{DownloadPath}/temp</c>), each favorites folder (named by its numeric id)
        /// whose newest file is that old, otherwise each such entry inside it (a page's work folder, or a file
        /// left by the previous external-tool downloader). Anything else under <paramref name="tempRoot"/> is not ours and is
        /// left alone, as is the components folder.
        /// </summary>
        internal static void PruneWorkDirectories(string tempRoot, DateTime utcNow, TimeSpan maxAge,
            ILogger? logger = null)
        {
            if (!Directory.Exists(tempRoot))
            {
                return;
            }

            var cutoff = utcNow - maxAge;
            IEnumerable<string> favoritesFolders;
            try
            {
                favoritesFolders = Directory.EnumerateDirectories(tempRoot)
                    .Where(d => long.TryParse(Path.GetFileName(d), out _)).ToList();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(e, "Could not list the Bilibili work folders");
                return;
            }

            foreach (var folder in favoritesFolders)
            {
                try
                {
                    if (NewestWriteUtc(folder) < cutoff)
                    {
                        Directory.Delete(folder, true);
                        continue;
                    }

                    foreach (var entry in Directory.EnumerateFileSystemEntries(folder).ToList())
                    {
                        try
                        {
                            if (NewestWriteUtc(entry) >= cutoff)
                            {
                                continue;
                            }

                            if (Directory.Exists(entry))
                            {
                                Directory.Delete(entry, true);
                            }
                            else
                            {
                                File.Delete(entry);
                            }
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            logger?.LogWarning(e, "Could not delete the old Bilibili work item {Name}",
                                Path.GetFileName(entry));
                        }
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    logger?.LogWarning(e, "Could not prune the Bilibili work folder {Name}", Path.GetFileName(folder));
                }
            }
        }

        private static DateTime NewestWriteUtc(string path)
        {
            if (!Directory.Exists(path))
            {
                return File.GetLastWriteTimeUtc(path);
            }

            var newest = Directory.GetLastWriteTimeUtc(path);
            foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories))
            {
                var written = File.GetLastWriteTimeUtc(entry);
                if (written > newest)
                {
                    newest = written;
                }
            }

            return newest;
        }

        private static bool IsRiskControl(Exception e) =>
            FindCause<BilibiliTemporarilyUnavailableException>(e) is {Kind: BilibiliTemporaryFailureKind.RiskControl};

        private static T? FindCause<T>(Exception root) where T : Exception
        {
            var pending = new Stack<Exception>();
            pending.Push(root);
            for (var visited = 0; pending.Count > 0 && visited < 64; visited++)
            {
                var current = pending.Pop();
                if (current is T match)
                {
                    return match;
                }

                if (current is AggregateException aggregate)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        pending.Push(inner);
                    }
                }
                else if (current.InnerException != null)
                {
                    pending.Push(current.InnerException);
                }
            }

            return null;
        }
    }
}
