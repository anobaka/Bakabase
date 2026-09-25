using System.Runtime.ExceptionServices;
using System.Text;
using Bakabase.Abstractions.Components.Media;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Constants;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Download;

/// <param name="PartNo">1-based index in the archive's pages (the <c>PartNo</c> naming field).</param>
public sealed record BilibiliArchivePage(int PartNo, long Cid, string? PartName, int DurationSeconds);

/// <param name="AccessSkip">Why this account cannot play the archive (supporter-only), decided from view. It is
/// applied per page only AFTER the existing-file check, so a video downloaded while the user had access is
/// never reported as skipped.</param>
public sealed record BilibiliArchive(
    long Aid,
    string? BvId,
    string? CoverUrl,
    bool IsPgcRedirect,
    BilibiliSkip? AccessSkip,
    IReadOnlyList<BilibiliArchivePage> Pages);

/// <summary>Exactly one of <see cref="Archive"/> and <see cref="Skip"/> is set.</summary>
public sealed record BilibiliArchiveResolution(BilibiliArchive? Archive, BilibiliSkip? Skip);

/// <param name="VideoPath">Full path of the key file (the <c>.mp4</c>).</param>
/// <param name="AlreadyExists">Whether that file exists, i.e. the page counts as downloaded.</param>
public sealed record BilibiliPageTarget(string VideoPath, bool AlreadyExists);

/// <summary>One page (cid) to download.</summary>
/// <param name="WorkDirectory">A folder for this page only (partial streams, the merged file). Kept when the page
/// is interrupted (the next run resumes), deleted when it finishes in any way.</param>
/// <param name="ResolveTarget">Builds the key file's path from the legacy QualityName (may be null). Usually
/// called once; when the naming request is refused it is called once per known quality name (and with null) to
/// find a file downloaded earlier, so it must be cheap and free of side effects that matter.</param>
/// <param name="OnProgress">0–100 for this page; monotonic, throttled, never called concurrently. Also called with
/// an unchanged value as a sign of life during long transfers and merges.</param>
/// <param name="GetCover">The archive's cover bytes (fetched lazily once per archive by the caller, e.g. with
/// <see cref="BilibiliVideoDownloadService.DownloadCoverAsync"/>); null = none.</param>
/// <param name="DownloadCaptions">Whether to save danmaku (<c>.xml</c>) and subtitles (<c>.srt</c>).</param>
/// <param name="CoverUrl">The cover's URL, for the cover file's extension (and as the source when
/// <paramref name="GetCover"/> is null).</param>
public sealed record BilibiliPageJob(
    long Aid,
    long Cid,
    bool IsPgcRedirect,
    BilibiliSkip? AccessSkip,
    string WorkDirectory,
    Func<string?, CancellationToken, Task<BilibiliPageTarget>> ResolveTarget,
    Func<decimal, Task>? OnProgress = null,
    Func<CancellationToken, Task<byte[]?>>? GetCover = null,
    bool DownloadCaptions = true,
    string? CoverUrl = null);

public enum BilibiliPageStatus
{
    Downloaded = 1,
    AlreadyExists = 2,
    Skipped = 3,
}

public sealed record BilibiliPageOutcome(
    BilibiliPageStatus Status,
    string? VideoPath = null,
    string? QualityName = null,
    BilibiliSkip? Skip = null);

/// <summary>
/// Downloads one archive page through the API: the protocol decisions come from <c>Protocol/</c>, the bytes from
/// <see cref="BilibiliCdnDownloader"/> (no cookie), the merge from <see cref="IMediaMerger"/>.
/// </summary>
/// <remarks>
/// <para>Page flow: legacy naming request (file names stay byte-identical) → existing key file → a complete merge
/// left by an earlier run → access gate → login check → playurl (fnval=4048) → streams → merge → cover and
/// captions → key file LAST (a crash never leaves a key file without its captions) → work folder deleted.</para>
/// <para>Outcomes: definite content states are <see cref="BilibiliPageStatus.Skipped"/>; so are streams that no
/// CDN host will serve (<see cref="BilibiliSkipReason.CdnUnavailable"/>) and streams ffmpeg cannot merge after
/// every fallback (<see cref="BilibiliSkipReason.MergeFailed"/>). Risk control, busy services and flaky
/// networks throw transient errors; an expired login, a full disk, a missing ffmpeg and protocol changes throw
/// fatal ones.</para>
/// <para>Nothing here logs or throws a URL other than through <see cref="BilibiliCdnUrls.Redact"/>, a cookie or
/// a response body.</para>
/// </remarks>
public sealed class BilibiliVideoDownloadService
{
    /// <summary>A stream changing under a download re-plans the page at most this often; then it is transient.</summary>
    public const int MaxReplans = 1;

    /// <summary>Video re-selections after a mux failure (see <see cref="BilibiliStreamSelector.SelectVideoFallback"/>).</summary>
    public const int MaxVideoFallbacks = 2;

    /// <summary>Beyond the quality check after every playurl answer, myinfo is asked at least this often.</summary>
    public static readonly TimeSpan LoginCheckInterval = TimeSpan.FromMinutes(10);

    /// <summary>Refresh requests starting within this window of each other share one playurl answer.</summary>
    public static readonly TimeSpan RefreshShareWindow = TimeSpan.FromSeconds(30);

    /// <summary>Complete once it exists: the merger writes a partial file and moves it into place, the single-mp4
    /// durl path moves a finished CDN file.</summary>
    public const string MergedFileName = "merged.mp4";

    /// <summary>
    /// Every QualityName the legacy naming request is known to produce (<c>support_formats[].new_description</c>,
    /// which <see cref="VideoQuality.Description"/> binds to), for finding an existing file when that request is
    /// refused. Null is the name of a page whose answer had no formats.
    /// </summary>
    public static readonly IReadOnlyList<string?> KnownLegacyQualityNames =
    [
        "8K 超高清", "杜比视界", "HDR 真彩", "4K 超高清", "1080P 60帧", "1080P 高码率", "1080P 高清", "720P 准高清",
        "480P 标清", "360P 流畅",
        // Also seen in answers (PGC-like wording).
        "4K 超清", "720P 高清", "480P 清晰",
        null,
    ];

    private const int QualityLowLogin = 32;
    private const int QualityNeedsLogin = 64;

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly UTF8Encoding Utf8WithBom = new(true);

    private readonly BilibiliClient _client;
    private readonly BilibiliCdnDownloader _cdn;
    private readonly IMediaMerger _merger;
    private readonly TimeProvider _time;
    private readonly ILogger<BilibiliVideoDownloadService> _logger;
    private readonly object _loginLock = new();
    private long? _lastLoginCheck;

    public BilibiliVideoDownloadService(BilibiliClient client, BilibiliCdnDownloader cdn, IMediaMerger merger,
        TimeProvider time, ILogger<BilibiliVideoDownloadService> logger)
    {
        _client = client;
        _cdn = cdn;
        _merger = merger;
        _time = time;
        _logger = logger;
    }

    #region Archive

    /// <summary>
    /// view (following one <c>forward</c>) or, after -403, pagelist. Skips are definite content states; risk
    /// control, busy, -101 and unknown codes throw, and so does an expired login behind a skip that depends on it.
    /// </summary>
    public async Task<BilibiliArchiveResolution> ResolveArchiveAsync(FavoriteItem item, CancellationToken ct)
    {
        var resolution = await ResolveArchiveCoreAsync(item, ct);
        if (resolution.Skip is { } skip)
        {
            await ConfirmLoginBehindSkipAsync(skip, ct);
        }

        return resolution;
    }

    private async Task<BilibiliArchiveResolution> ResolveArchiveCoreAsync(FavoriteItem item, CancellationToken ct)
    {
        var aid = item.Id;
        var depth = 0;
        while (true)
        {
            var view = await _client.GetView(aid, ct);
            var outcome = BilibiliArchiveRules.DecideView(view, aid, depth);
            switch (outcome.Kind)
            {
                case BilibiliViewOutcomeKind.Proceed:
                {
                    var data = view.Data!;
                    return new BilibiliArchiveResolution(new BilibiliArchive(aid, data.BvId ?? item.BvId,
                        data.Pic ?? item.Cover, BilibiliArchiveRules.IsPgcRedirect(data.RedirectUrl),
                        BilibiliArchiveRules.GateAccess(data), ToPages(data.Pages!)), null);
                }
                case BilibiliViewOutcomeKind.FollowForward:
                    _logger.LogInformation("Bilibili archive {Aid} was merged into {Forward}; following it.", aid,
                        outcome.ForwardAid);
                    aid = outcome.ForwardAid!.Value;
                    depth++;
                    continue;
                case BilibiliViewOutcomeKind.CheckExistence:
                    return new BilibiliArchiveResolution(null,
                        BilibiliArchiveRules.DecideAfterNotFound(await _client.GetPageList(aid, ct)));
                case BilibiliViewOutcomeKind.PageListFallback:
                {
                    var (pages, skip) =
                        BilibiliArchiveRules.DecidePageListFallback(await _client.GetPageList(aid, ct));
                    return skip != null
                        ? new BilibiliArchiveResolution(null, skip)
                        : new BilibiliArchiveResolution(
                            new BilibiliArchive(aid, item.BvId, item.Cover, false, null, ToPages(pages!)), null);
                }
                case BilibiliViewOutcomeKind.Skip:
                    return new BilibiliArchiveResolution(null, outcome.Skip);
                default:
                    throw new BilibiliProtocolException(BilibiliClient.ViewEndpoint,
                        $"unhandled view outcome {outcome.Kind}");
            }
        }
    }

    private static IReadOnlyList<BilibiliArchivePage> ToPages(IReadOnlyList<PostPage> pages) =>
        pages.Select((p, i) => new BilibiliArchivePage(i + 1, p.Cid, p.Part, p.Duration)).ToList();

    #endregion

    #region Page

    public async Task<BilibiliPageOutcome> DownloadPageAsync(BilibiliPageJob job, CancellationToken ct)
    {
        var progress = new BilibiliPageProgress(job.OnProgress, _time, _logger);
        BilibiliPageOutcome outcome;
        try
        {
            outcome = await DownloadPageCoreAsync(job, progress, ct);
        }
        catch
        {
            await progress.CloseAsync();
            throw;
        }

        TryDeleteDirectory(job.WorkDirectory);
        await progress.CloseAsync(100);
        if (outcome.Skip is { } skip)
        {
            // Debug: the caller records (and logs) every skip as a notice.
            _logger.LogDebug("Bilibili page {Aid}/{Cid} skipped: {Reason} ({Code}).", job.Aid, job.Cid,
                skip.Reason, skip.Code);
        }

        return outcome;
    }

    private async Task<BilibiliPageOutcome> DownloadPageCoreAsync(BilibiliPageJob job, BilibiliPageProgress progress,
        CancellationToken ct)
    {
        // 1. Naming: the unchanged legacy request, so existing libraries keep their names.
        var naming = await _client.GetLegacyNamingSource(job.Aid, job.Cid, ct);
        if (naming.Code != BilibiliApiCodes.Ok)
        {
            // Throws for anything that is not a content state of this page.
            var skip = BilibiliPlayUrlRules.DecideError(naming.Code, naming.Message, job.IsPgcRedirect,
                BilibiliClient.NamingEndpoint);
            return await FindExistingAsync(job, ct) ?? await SkippedAsync(skip, ct);
        }

        var qualityName = BilibiliQualityNaming.LegacyQualityName(naming.Data?.SupportFormats);
        var target = await job.ResolveTarget(qualityName, ct);
        if (target.AlreadyExists)
        {
            return new BilibiliPageOutcome(BilibiliPageStatus.AlreadyExists, target.VideoPath, qualityName);
        }

        var work = new WorkFiles(job.WorkDirectory);
        if (work.HasCompleteMerge())
        {
            _logger.LogInformation("Bilibili page {Aid}/{Cid}: reusing the merged file of an earlier run.", job.Aid,
                job.Cid);
        }
        else
        {
            // 2. Access gate — only now, so a file downloaded while the user had access is not reported.
            if (job.AccessSkip != null)
            {
                return await SkippedAsync(job.AccessSkip, ct);
            }

            await EnsureLoginRecentlyCheckedAsync(ct);

            var replans = 0;
            while (true)
            {
                try
                {
                    var skip = await PlanDownloadAndMergeAsync(job, work, progress, ct);
                    if (skip != null)
                    {
                        return await SkippedAsync(skip, ct);
                    }

                    break;
                }
                catch (BilibiliStreamChangedException e)
                {
                    if (++replans > MaxReplans)
                    {
                        throw new BilibiliTemporarilyUnavailableException(
                            BilibiliTemporaryFailureKind.CdnUnavailable, BilibiliClient.PlayUrlEndpoint, null, e);
                    }

                    _logger.LogInformation(
                        "Bilibili page {Aid}/{Cid}: stream {Identity} is no longer offered; planning again.",
                        job.Aid, job.Cid, e.IdentityKey);
                }
                catch (BilibiliCdnDeadException e)
                {
                    _logger.LogWarning("Bilibili page {Aid}/{Cid}: no CDN host serves stream {Identity} (HTTP {Status}).",
                        job.Aid, job.Cid, e.IdentityKey, e.HttpStatus);
                    return Skipped(new BilibiliSkip(BilibiliSkipReason.CdnUnavailable, e.HttpStatus));
                }
            }
        }

        await progress.ReportAsync(BilibiliPageProgress.MergeEnd, true);
        await FinalizeAsync(job, work, target.VideoPath, ct);
        return new BilibiliPageOutcome(BilibiliPageStatus.Downloaded, target.VideoPath, qualityName);
    }

    /// <summary>
    /// The naming request was refused, so the name cannot be computed. A file downloaded earlier under any known
    /// quality name still counts as downloaded, so a refused naming request never reports an existing file.
    /// </summary>
    private async Task<BilibiliPageOutcome?> FindExistingAsync(BilibiliPageJob job, CancellationToken ct)
    {
        foreach (var name in KnownLegacyQualityNames)
        {
            var target = await job.ResolveTarget(name, ct);
            if (target.AlreadyExists)
            {
                return new BilibiliPageOutcome(BilibiliPageStatus.AlreadyExists, target.VideoPath, name);
            }
        }

        return null;
    }

    private static BilibiliPageOutcome Skipped(BilibiliSkip skip) => new(BilibiliPageStatus.Skipped, Skip: skip);

    /// <summary>A skip, once the login behind it is confirmed (see <see cref="ConfirmLoginBehindSkipAsync"/>).</summary>
    private async Task<BilibiliPageOutcome> SkippedAsync(BilibiliSkip skip, CancellationToken ct)
    {
        await ConfirmLoginBehindSkipAsync(skip, ct);
        return Skipped(skip);
    }

    /// <summary>
    /// One plan: playurl, streams, merge into <see cref="MergedFileName"/>. Returns a skip, or null once the
    /// merged file is complete.
    /// </summary>
    private async Task<BilibiliSkip?> PlanDownloadAndMergeAsync(BilibiliPageJob job, WorkFiles work,
        BilibiliPageProgress progress, CancellationToken ct)
    {
        var rsp = await _client.GetPlayUrl(job.Aid, job.Cid, BiliBiliApiUrls.DefaultPlayQn, ct);
        var outcome = BilibiliPlayUrlRules.Decide(rsp, job.IsPgcRedirect);
        switch (outcome.Kind)
        {
            case BilibiliPlayUrlOutcomeKind.Skip:
                return outcome.Skip;
            case BilibiliPlayUrlOutcomeKind.NoStreams:
                _logger.LogWarning("Bilibili playurl for {Aid}/{Cid} answered code 0 without playable streams: {Data}",
                    job.Aid, job.Cid, BilibiliDiagnostics.RedactJson(JsonConvert.SerializeObject(rsp.Data)));
                throw new BilibiliProtocolException(BilibiliClient.PlayUrlEndpoint, "code 0 without playable streams");
        }

        var data = rsp.Data!;
        await EnsureLoggedInForQualityAsync(data, ct);

        work.EnsureDirectory();
        // Refreshes live as long as this plan: one still running when the plan ends (a transfer failed while
        // another waited for it) is cancelled rather than left to finish on its own.
        using var planCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var refresh = new SharedPlayUrlRefresh(
            () => _client.GetPlayUrl(job.Aid, job.Cid, BiliBiliApiUrls.DefaultPlayQn, planCts.Token), _time);
        try
        {
            return outcome.Kind == BilibiliPlayUrlOutcomeKind.Dash
                ? await DownloadDashAsync(job, data, work, refresh, progress, ct)
                : await DownloadDurlAsync(job, data, work, refresh, progress, ct);
        }
        finally
        {
            await planCts.CancelAsync();
        }
    }

    #endregion

    #region DASH

    private async Task<BilibiliSkip?> DownloadDashAsync(BilibiliPageJob job, VideoSource data, WorkFiles work,
        SharedPlayUrlRefresh refresh, BilibiliPageProgress progress, CancellationToken ct)
    {
        var dash = data.Dash!;
        var selection = BilibiliStreamSelector.SelectDash(job.Cid, dash) ??
                        throw new BilibiliProtocolException(BilibiliClient.PlayUrlEndpoint,
                            "dash without usable video");
        var durationMs = data.Timelength > 0 ? data.Timelength : dash.Duration * 1000;

        var video = selection.Video;
        var audio = selection.Audio;
        var audioKind = selection.AudioKind;
        _logger.LogInformation(
            "Bilibili page {Aid}/{Cid}: video {VideoId} (codec {CodecId}), audio {AudioId} ({AudioKind}).", job.Aid,
            job.Cid, video.Id, video.CodecId, audio?.Id, audioKind);

        var videoSlot = progress.AddStream(EstimateBytes(video.Bandwidth, durationMs));
        var audioSlot = audio == null ? null : progress.AddStream(EstimateBytes(audio.Bandwidth, durationMs));
        await RunTogetherAsync(ct,
            c => TransferDashAsync(job, video, false, work, refresh, videoSlot, c),
            audio == null ? null : c => TransferDashAsync(job, audio, true, work, refresh, audioSlot!, c));

        var failure = await TryMuxAsync(work, video, audio, IsExperimental(audioKind), durationMs, progress, ct);
        if (failure == null)
        {
            return null;
        }

        var exitCode = failure.ExitCode;

        // FLAC / E-AC-3 in MP4 needs a recent ffmpeg: retry with the AAC stream.
        if (IsExperimental(audioKind) && selection.AacFallbackAudio is { } aac)
        {
            _logger.LogWarning(
                "Bilibili page {Aid}/{Cid}: merging with {AudioKind} audio failed (exit {ExitCode}); retrying with AAC.",
                job.Aid, job.Cid, audioKind, exitCode);
            audio = aac;
            audioKind = BilibiliAudioKind.Aac;
            var aacSlot = progress.AddStream(EstimateBytes(aac.Bandwidth, durationMs));
            await TransferDashAsync(job, aac, true, work, refresh, aacSlot, ct);
            failure = await TryMuxAsync(work, video, audio, false, durationMs, progress, ct);
            if (failure == null)
            {
                return null;
            }

            exitCode = failure.ExitCode;
        }

        // The video stream may be what ffmpeg cannot handle (Dolby Vision, 8K HEVC, AV1 on an old build).
        var tried = new HashSet<(int Id, int CodecId)> {(video.Id, video.CodecId)};
        var failed = video;
        for (var i = 0; i < MaxVideoFallbacks; i++)
        {
            var next = BilibiliStreamSelector.SelectVideoFallback(job.Cid, dash, failed, tried);
            if (next == null)
            {
                break;
            }

            tried.Add((next.Id, next.CodecId));
            _logger.LogWarning(
                "Bilibili page {Aid}/{Cid}: merging video {VideoId} (codec {CodecId}) failed (exit {ExitCode}); trying video {NextId} (codec {NextCodecId}).",
                job.Aid, job.Cid, failed.Id, failed.CodecId, exitCode, next.Id, next.CodecId);
            var slot = progress.AddStream(EstimateBytes(next.Bandwidth, durationMs));
            await TransferDashAsync(job, next, false, work, refresh, slot, ct);
            failure = await TryMuxAsync(work, next, audio, IsExperimental(audioKind), durationMs, progress, ct);
            if (failure == null)
            {
                return null;
            }

            exitCode = failure.ExitCode;
            failed = next;
        }

        _logger.LogWarning("Bilibili page {Aid}/{Cid}: the streams could not be merged (last exit code {ExitCode}).",
            job.Aid, job.Cid, exitCode);
        return new BilibiliSkip(BilibiliSkipReason.MergeFailed, exitCode);
    }

    private static bool IsExperimental(BilibiliAudioKind kind) =>
        kind is BilibiliAudioKind.Flac or BilibiliAudioKind.DolbyEac3;

    private async Task TransferDashAsync(BilibiliPageJob job, BilibiliStreamCandidate candidate, bool isAudio,
        WorkFiles work, SharedPlayUrlRefresh refresh, BilibiliPageProgress.Slot slot, CancellationToken ct)
    {
        var path = work.DashStreamPath(candidate, isAudio);
        var ticket = new SharedPlayUrlRefresh.Ticket();
        await _cdn.DownloadAsync(new BilibiliCdnTransfer(path, candidate.IdentityKey, candidate.Urls, async rct =>
        {
            var r = await refresh.GetAsync(ticket, rct);
            if (BilibiliPlayUrlRules.Decide(r, job.IsPgcRedirect).Kind != BilibiliPlayUrlOutcomeKind.Dash ||
                r.Data?.Dash is not { } fresh)
            {
                return null;
            }

            return BilibiliStreamSelector.FindSame(job.Cid, fresh, candidate, isAudio)?.Urls;
        }), slot.Report, ct);
        slot.Complete(FileLength(path));
    }

    private async Task<MediaMergeException?> TryMuxAsync(WorkFiles work, BilibiliStreamCandidate video,
        BilibiliStreamCandidate? audio, bool experimental, long durationMs, BilibiliPageProgress progress,
        CancellationToken ct)
    {
        var request = new MediaMuxRequest(work.DashStreamPath(video, false),
            audio == null ? null : work.DashStreamPath(audio, true), work.MergedPath)
        {
            TagHevcAsHvc1 = BilibiliStreamSelector.NeedsHvc1Tag(video),
            AllowExperimentalCodecs = experimental && audio != null,
        };
        try
        {
            await _merger.MuxAsync(request, MergeProgress(progress, durationMs), ct);
        }
        catch (MediaMergeException e)
        {
            return e;
        }

        return null;
    }

    #endregion

    #region durl

    private async Task<BilibiliSkip?> DownloadDurlAsync(BilibiliPageJob job, VideoSource data, WorkFiles work,
        SharedPlayUrlRefresh refresh, BilibiliPageProgress progress, CancellationToken ct)
    {
        var segments = BilibiliStreamSelector.SelectDurl(job.Cid, data.Durl);
        if (segments.Count == 0)
        {
            throw new BilibiliProtocolException(BilibiliClient.PlayUrlEndpoint, "durl without usable segments");
        }

        var durationMs = data.Timelength > 0 ? data.Timelength : segments.Sum(s => s.LengthMs);
        var slots = segments.Select(s => progress.AddStream(s.Size)).ToList();
        var paths = new List<string>();
        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var path = work.SegmentPath(segment);
            var ticket = new SharedPlayUrlRefresh.Ticket();
            await _cdn.DownloadAsync(new BilibiliCdnTransfer(path, segment.IdentityKey, segment.Urls, async rct =>
            {
                var r = await refresh.GetAsync(ticket, rct);
                // Re-decide: access can end mid-download, and a preview answer must never stand in for the
                // segment (it would be saved as the whole video). Anything but a full durl answer re-plans.
                return BilibiliPlayUrlRules.Decide(r, job.IsPgcRedirect).Kind == BilibiliPlayUrlOutcomeKind.Durl
                    ? BilibiliStreamSelector.FindSame(job.Cid, r.Data?.Durl, segment)?.Urls
                    : null;
            }), slots[i].Report, ct);
            slots[i].Complete(FileLength(path));
            paths.Add(path);
        }

        var mergeProgress = MergeProgress(progress, durationMs);
        try
        {
            if (paths.Count == 1 && segments[0].Extension == ".mp4")
            {
                DiskWriteException.Guard(work.MergedPath, () => File.Move(paths[0], work.MergedPath, true));
            }
            else if (paths.Count == 1)
            {
                await _merger.RemuxAsync(paths[0], work.MergedPath, mergeProgress, ct);
            }
            else
            {
                await _merger.ConcatAsync(paths, work.MergedPath, mergeProgress, ct);
            }
        }
        catch (MediaMergeException e)
        {
            _logger.LogWarning("Bilibili page {Aid}/{Cid}: the segments could not be joined (exit code {ExitCode}).",
                job.Aid, job.Cid, e.ExitCode);
            return new BilibiliSkip(BilibiliSkipReason.MergeFailed, e.ExitCode);
        }

        return null;
    }

    #endregion

    #region Login

    /// <summary>myinfo at most every <see cref="LoginCheckInterval"/>: a cookie can expire during a long run.</summary>
    private async Task EnsureLoginRecentlyCheckedAsync(CancellationToken ct)
    {
        lock (_loginLock)
        {
            if (_lastLoginCheck is { } last && _time.GetElapsedTime(last) < LoginCheckInterval)
            {
                return;
            }
        }

        await CheckLoginAsync(ct);
    }

    /// <summary>
    /// Logged-out playurl answers are code 0 with low qualities only. Such an answer while higher qualities are
    /// offered means the login may have expired: ask myinfo before a single byte is downloaded, so a low-quality
    /// file is never saved under a name that claims more.
    /// </summary>
    private async Task EnsureLoggedInForQualityAsync(VideoSource data, CancellationToken ct)
    {
        if (data.Dash?.Video is not { Count: > 0 } videos)
        {
            return;
        }

        var best = videos.Where(v => v.Id > 0).Select(v => v.Id).DefaultIfEmpty(0).Max();
        var offered = Math.Max(data.AcceptQuality?.DefaultIfEmpty(0).Max() ?? 0,
            data.SupportFormats?.Select(f => f.Quality).DefaultIfEmpty(0).Max() ?? 0);
        if (best <= QualityLowLogin && offered >= QualityNeedsLogin)
        {
            _logger.LogInformation(
                "Bilibili playurl offered only quality {Best} while listing {Offered}; checking the login.", best,
                offered);
            await CheckLoginAsync(ct);
        }
    }

    /// <summary>
    /// An expired login answers like an account without access (supporter-only, preview, member-only). A skip
    /// advances the checkpoint for good, so such a skip asks myinfo first, whenever it was last asked: an expired
    /// login throws <see cref="BilibiliNotLoggedInException"/> and the checkpoint stays.
    /// </summary>
    private async Task ConfirmLoginBehindSkipAsync(BilibiliSkip skip, CancellationToken ct)
    {
        if (BilibiliSkipReasons.DependsOnLogin(skip.Reason))
        {
            await CheckLoginAsync(ct);
        }
    }

    private async Task CheckLoginAsync(CancellationToken ct)
    {
        await _client.EnsureLoggedIn(ct);
        lock (_loginLock)
        {
            _lastLoginCheck = _time.GetTimestamp();
        }
    }

    #endregion

    #region Finalize, cover, captions

    private async Task FinalizeAsync(BilibiliPageJob job, WorkFiles work, string videoPath, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(videoPath);
        if (!string.IsNullOrEmpty(directory))
        {
            DiskWriteException.Guard(directory, () => Directory.CreateDirectory(directory));
        }

        await SaveSidecarsAsync(job, videoPath, ct);
        ct.ThrowIfCancellationRequested();

        // The key file LAST: once it exists the page counts as downloaded.
        if (File.Exists(videoPath))
        {
            TryDeleteFile(work.MergedPath);
        }
        else
        {
            DiskWriteException.Guard(videoPath, () => File.Move(work.MergedPath, videoPath));
        }

        _logger.LogInformation("Bilibili page {Aid}/{Cid} downloaded.", job.Aid, job.Cid);
    }

    /// <summary>Cover, danmaku and subtitles next to the video; each only when missing; failures only logged.</summary>
    private async Task SaveSidecarsAsync(BilibiliPageJob job, string videoPath, CancellationToken ct)
    {
        var stem = Path.Combine(Path.GetDirectoryName(videoPath) ?? "", Path.GetFileNameWithoutExtension(videoPath));

        await BestEffortAsync("cover", job, async () =>
        {
            var path = stem + CoverExtension(job.CoverUrl);
            if (File.Exists(path))
            {
                return;
            }

            var bytes = job.GetCover != null
                ? await job.GetCover(ct)
                : await DownloadCoverAsync(job.CoverUrl, ct);
            if (bytes is { Length: > 0 })
            {
                await WriteFileAsync(path, bytes, ct);
            }
        }, ct);

        if (!job.DownloadCaptions)
        {
            return;
        }

        await BestEffortAsync("danmaku", job, async () =>
        {
            var path = stem + ".xml";
            if (File.Exists(path))
            {
                return;
            }

            var body = await _cdn.GetSmallAsync(BiliBiliApiUrls.DanmakuXml(job.Cid), ct);
            if (body == null)
            {
                return;
            }

            var xml = BilibiliTextDecoder.DecodeDanmakuXml(body.Bytes, body.ContentEncodings);
            await WriteFileAsync(path, Utf8NoBom.GetBytes(xml), ct);
        }, ct);

        IReadOnlyList<BilibiliSubtitlePlan> plans = [];
        await BestEffortAsync("subtitle list", job, async () =>
        {
            var dm = await _client.GetDmView(job.Aid, job.Cid, ct);
            if (dm.Code == BilibiliApiCodes.Ok)
            {
                plans = BilibiliCaptions.PlanSubtitles(dm.Data?.Subtitle?.Subtitles);
            }
        }, ct);

        foreach (var plan in plans)
        {
            await BestEffortAsync("subtitle", job, async () =>
            {
                if (!plan.IsAi && File.Exists(stem + plan.FileSuffix + ".srt"))
                {
                    return;
                }

                var body = await _cdn.GetSmallAsync(plan.Url, ct);
                if (body == null)
                {
                    return;
                }

                var json = Encoding.UTF8.GetString(BilibiliTextDecoder.Decode(body.Bytes, body.ContentEncodings));
                SubtitleBody subtitle;
                try
                {
                    subtitle = JsonConvert.DeserializeObject<SubtitleBody>(json) ??
                               throw new InvalidDataException("empty subtitle");
                }
                catch (JsonException)
                {
                    // Not the exception itself: its message quotes the body.
                    throw new InvalidDataException("the subtitle is not the expected JSON");
                }

                var path = stem + BilibiliCaptions.ResolveFileSuffix(plan, subtitle) + ".srt";
                if (!File.Exists(path))
                {
                    await WriteFileAsync(path, Utf8WithBom.GetPreamble()
                        .Concat(Utf8WithBom.GetBytes(BilibiliCaptions.ToSrt(subtitle))).ToArray(), ct);
                }
            }, ct);
        }
    }

    /// <summary>The cover through the CDN client (no cookie to <c>i*.hdslb.com</c>); null on any failure.</summary>
    public async Task<byte[]?> DownloadCoverAsync(string? coverUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coverUrl))
        {
            return null;
        }

        try
        {
            return (await _cdn.GetSmallAsync(BilibiliCaptions.NormalizeUrl(coverUrl), ct))?.Bytes;
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Downloading the Bilibili cover {Url} failed: {Error}: {Message}",
                BilibiliCdnUrls.Redact(coverUrl), e.GetType().Name, BilibiliDiagnostics.RedactText(e.Message));
            return null;
        }
    }

    /// <summary>The cover URL's extension (as the old downloader named covers), ".jpg" when there is none.</summary>
    public static string CoverExtension(string? coverUrl)
    {
        if (!string.IsNullOrWhiteSpace(coverUrl) &&
            Uri.TryCreate(BilibiliCaptions.NormalizeUrl(coverUrl), UriKind.Absolute, out var uri))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (extension.Length is > 1 and <= 6 && extension.Skip(1).All(char.IsLetterOrDigit))
            {
                return extension;
            }
        }

        return ".jpg";
    }

    private async Task BestEffortAsync(string what, BilibiliPageJob job, Func<Task> action, CancellationToken ct)
    {
        try
        {
            await action();
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Bilibili page {Aid}/{Cid}: saving the {What} failed: {Error}: {Message}", job.Aid,
                job.Cid, what, e.GetType().Name, BilibiliDiagnostics.RedactText(e.Message));
        }
    }

    /// <summary>Through a temp file next to the video, which never stays behind in the library.</summary>
    private async Task WriteFileAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var temp = path + ".partial";
        try
        {
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, true);
        }
        catch
        {
            TryDeleteFile(temp);
            throw;
        }
    }

    #endregion

    #region Helpers

    private static MediaMergeProgress MergeProgress(BilibiliPageProgress progress, long durationMs) =>
        new(progress.ReportMergeAsync, durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : null);

    /// <summary>Bytes of a stream from its bandwidth (bit/s) and duration, until the CDN reports the size.</summary>
    private static long EstimateBytes(long bandwidth, long durationMs) =>
        bandwidth > 0 && durationMs > 0 ? Math.Max(1, bandwidth / 8 * durationMs / 1000) : 1;

    private static long FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 1;
        }
    }

    /// <summary>
    /// Runs the transfers at once; the first failure cancels the others and is the one rethrown (not the
    /// cancellations it caused).
    /// </summary>
    private static async Task RunTogetherAsync(CancellationToken ct, params Func<CancellationToken, Task>?[] works)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = works.OfType<Func<CancellationToken, Task>>().Select(work => Task.Run(async () =>
        {
            try
            {
                await work(linked.Token);
            }
            catch
            {
                await linked.CancelAsync();
                throw;
            }
        }, CancellationToken.None)).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
            ct.ThrowIfCancellationRequested();
            var exceptions = tasks.Where(t => t.IsFaulted || t.IsCanceled)
                .SelectMany(t => (IEnumerable<Exception>?) t.Exception?.InnerExceptions ?? [])
                .ToList();
            var cause = exceptions.FirstOrDefault(e => e is not OperationCanceledException) ?? exceptions.FirstOrDefault();
            if (cause != null)
            {
                ExceptionDispatchInfo.Throw(cause);
            }

            throw;
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not delete the work folder {Path}: {Error}", path, e.GetType().Name);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Could not delete {Path}: {Error}", path, e.GetType().Name);
        }
    }

    /// <summary>
    /// The files of one page's work folder. Stream files are named after the stream's identity, so a partial or
    /// finished file is only reused when exactly the same stream is offered again.
    /// </summary>
    private sealed class WorkFiles(string directory)
    {
        private const int MaxNameLength = 120;

        public string MergedPath { get; } = Path.Combine(directory, MergedFileName);

        public string DashStreamPath(BilibiliStreamCandidate candidate, bool isAudio) =>
            Path.Combine(directory,
                isAudio ? $"a-{candidate.Id}.m4s" : $"v-{candidate.Id}-c{candidate.CodecId}.m4s");

        /// <summary><c>seg-{Order}-{file name}</c> (the file name names the transcode).</summary>
        public string SegmentPath(BilibiliDurlSegment segment)
        {
            var safe = new string(segment.FileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)
                .ToArray()).Trim('.', ' ');
            if (safe.Length > MaxNameLength)
            {
                safe = safe[^MaxNameLength..];
            }

            if (!safe.EndsWith(segment.Extension, StringComparison.OrdinalIgnoreCase))
            {
                safe += segment.Extension;
            }

            return Path.Combine(directory, $"seg-{segment.Order}-{safe}");
        }

        /// <summary>See <see cref="MergedFileName"/>: it only ever appears complete.</summary>
        public bool HasCompleteMerge() => File.Exists(MergedPath);

        public void EnsureDirectory() => DiskWriteException.Guard(directory, () => Directory.CreateDirectory(directory));
    }

    /// <summary>
    /// One playurl refresh shared by the transfers of a plan (video and audio URLs usually expire together): a
    /// transfer asking for a refresh joins one that is running or finished less than
    /// <see cref="RefreshShareWindow"/> ago, unless it has already used that one — so one API request per round,
    /// not one per stream, while a transfer's own second refresh always asks again. A failed refresh is
    /// never reused.
    /// </summary>
    private sealed class SharedPlayUrlRefresh(Func<Task<DataWrapper<VideoSource>>> fetch, TimeProvider time)
    {
        private readonly object _lock = new();
        private Task<DataWrapper<VideoSource>>? _current;
        private int _generation;
        private long _startedAt;

        /// <summary>A transfer's view of the rounds: the last one it used.</summary>
        public sealed class Ticket
        {
            internal int Seen;
        }

        public Task<DataWrapper<VideoSource>> GetAsync(Ticket ticket, CancellationToken ct)
        {
            lock (_lock)
            {
                var reusable = _current != null && ticket.Seen < _generation && !_current.IsFaulted &&
                               !_current.IsCanceled &&
                               (!_current.IsCompleted || time.GetElapsedTime(_startedAt) < RefreshShareWindow);
                if (!reusable)
                {
                    _generation++;
                    _startedAt = time.GetTimestamp();
                    _current = fetch();
                    // Its waiters may all be gone (cancelled) by the time it fails: observe the failure here, so
                    // it never surfaces as an unobserved task exception.
                    _current.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }

                ticket.Seen = _generation;
                return _current!.WaitAsync(ct);
            }
        }
    }

    #endregion
}
