using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

public enum BilibiliAudioKind
{
    /// <summary>Video-only answer.</summary>
    None = 0,
    Aac = 1,
    Flac = 2,
    DolbyEac3 = 3,
}

/// <param name="Id">Quality id (video) or stream id (audio).</param>
/// <param name="Urls">Ranked candidates (<see cref="BilibiliCdnUrls.Candidates"/>), never empty.</param>
/// <param name="IdentityKey">Identifies the same stream across URL refreshes and runs (never contains a URL).</param>
public sealed record BilibiliStreamCandidate(
    int Id,
    int CodecId,
    long Bandwidth,
    string? Codecs,
    IReadOnlyList<string> Urls,
    string IdentityKey);

/// <param name="AacFallbackAudio">The best AAC stream when <see cref="AudioKind"/> is FLAC or Dolby
/// (used when muxing those fails); null otherwise.</param>
public sealed record BilibiliDashSelection(
    BilibiliStreamCandidate Video,
    BilibiliStreamCandidate? Audio,
    BilibiliAudioKind AudioKind,
    BilibiliStreamCandidate? AacFallbackAudio);

/// <param name="LengthMs">Duration of the segment.</param>
/// <param name="Extension">".mp4" or ".flv".</param>
/// <param name="IdentityKey"><c>{cid}-d{Order}-{<paramref name="FileName"/>}</c>.</param>
/// <param name="FileName">The last path segment of the primary URL; it encodes the transcode.</param>
public sealed record BilibiliDurlSegment(
    int Order,
    long LengthMs,
    long Size,
    IReadOnlyList<string> Urls,
    string Extension,
    string IdentityKey,
    string FileName);

/// <summary>Picks the streams to download from a playurl answer.</summary>
public static class BilibiliStreamSelector
{
    public const int CodecAvc = 7;
    public const int CodecHevc = 12;
    public const int CodecAv1 = 13;
    public const int QualityDolbyVision = 126;

    /// <summary>AVC &gt; HEVC &gt; AV1: plays everywhere first.</summary>
    public static readonly IReadOnlyList<int> DefaultCodecPreference = [CodecAvc, CodecHevc, CodecAv1];

    /// <summary>
    /// The best video by the numeric <c>dash.video[].id</c> (never <c>quality</c> / <c>accept_quality</c> /
    /// <c>support_formats</c>), then by codec preference (unknown codecs last), then bandwidth; the best audio
    /// in the fixed order FLAC &gt; Dolby (E-AC-3) &gt; highest-bandwidth AAC. Null when no video entry is usable.
    /// Unknown ids and codecs never throw.
    /// </summary>
    public static BilibiliDashSelection? SelectDash(long cid, VideoSource.TDash dash,
        IReadOnlyList<int>? codecPreference = null)
    {
        var video = PickVideo(VideoCandidates(cid, dash), codecPreference ?? DefaultCodecPreference);
        if (video == null)
        {
            return null;
        }

        var aac = BestAac(cid, dash);

        if (dash.Flac?.Audio is { } flacFragment && ToAudio(cid, flacFragment) is { } flac)
        {
            return new(video, flac, BilibiliAudioKind.Flac, aac);
        }

        var dolby = (dash.Dolby?.Audio ?? [])
            .Select(a => ToAudio(cid, a))
            .OfType<BilibiliStreamCandidate>()
            .OrderByDescending(a => a.Bandwidth)
            .FirstOrDefault();
        if (dolby != null)
        {
            return new(video, dolby, BilibiliAudioKind.DolbyEac3, aac);
        }

        return aac != null
            ? new(video, aac, BilibiliAudioKind.Aac, null)
            : new(video, null, BilibiliAudioKind.None, null);
    }

    /// <summary>
    /// The video to try after <paramref name="failed"/> could not be merged: AVC at the same quality id when the
    /// failed one was another codec, otherwise the best lower id (ranked as by <see cref="SelectDash"/>). Never one
    /// in <paramref name="tried"/>; null when nothing is left.
    /// </summary>
    public static BilibiliStreamCandidate? SelectVideoFallback(long cid, VideoSource.TDash dash,
        BilibiliStreamCandidate failed, IReadOnlySet<(int Id, int CodecId)> tried,
        IReadOnlyList<int>? codecPreference = null)
    {
        var failedRank = QualityRank(failed.Id);
        return PickVideo(VideoCandidates(cid, dash).Where(v =>
                !tried.Contains((v.Id, v.CodecId)) &&
                (QualityRank(v.Id) < failedRank || (v.Id == failed.Id && v.CodecId == CodecAvc))),
            codecPreference ?? DefaultCodecPreference);
    }

    /// <summary>
    /// Durl segments in playback order. Callers only get here for a <c>Durl</c> outcome, where every segment
    /// has a URL.
    /// </summary>
    public static IReadOnlyList<BilibiliDurlSegment> SelectDurl(long cid, IEnumerable<VideoSource.TDurl>? durl) =>
        (durl ?? [])
        .Select(d => ToSegment(cid, d))
        .OfType<BilibiliDurlSegment>()
        .OrderBy(s => s.Order)
        .ToList();

    /// <summary>
    /// The same stream (same <see cref="BilibiliStreamCandidate.IdentityKey"/>) in a newer answer, e.g. after a
    /// URL refresh; null when it is no longer offered.
    /// </summary>
    public static BilibiliStreamCandidate? FindSame(long cid, VideoSource.TDash dash,
        BilibiliStreamCandidate previous, bool isAudio)
    {
        var candidates = isAudio ? AudioCandidates(cid, dash) : VideoCandidates(cid, dash);
        return candidates
            .Where(c => c.IdentityKey == previous.IdentityKey)
            .OrderByDescending(c => c.Bandwidth)
            .FirstOrDefault();
    }

    /// <summary>
    /// The same segment in a newer answer, matched by the full identity (order AND file name, so a refreshed
    /// answer that became a single preview segment, or another transcode, is not taken for it); null when it
    /// is no longer offered.
    /// </summary>
    public static BilibiliDurlSegment? FindSame(long cid, IEnumerable<VideoSource.TDurl>? durl,
        BilibiliDurlSegment previous) =>
        SelectDurl(cid, durl).FirstOrDefault(s => s.IdentityKey == previous.IdentityKey);

    /// <summary>
    /// Only <c>hev1</c> HEVC is retagged <c>hvc1</c> (for Apple players); <c>hvc1</c> and Dolby Vision
    /// (<c>dvh1</c>) keep their tag.
    /// </summary>
    public static bool NeedsHvc1Tag(BilibiliStreamCandidate video) =>
        video.CodecId == CodecHevc && (video.Codecs is { } codecs
            ? codecs.StartsWith("hev1", StringComparison.OrdinalIgnoreCase)
            : video.Id != QualityDolbyVision);

    /// <summary>
    /// Ranking of quality ids. Dolby Vision (126, profile 5 without an SDR fallback) shows wrong colours on
    /// players without DV, so it is taken only when nothing else is offered.
    /// </summary>
    private static int QualityRank(int id) => id == QualityDolbyVision ? 0 : id;

    private static BilibiliStreamCandidate? PickVideo(IEnumerable<BilibiliStreamCandidate> candidates,
        IReadOnlyList<int> preference)
    {
        var videos = candidates.ToList();
        if (videos.Count == 0)
        {
            return null;
        }

        var bestRank = videos.Max(v => QualityRank(v.Id));
        return videos
            .Where(v => QualityRank(v.Id) == bestRank)
            .OrderBy(v => CodecOrder(preference, v.CodecId))
            .ThenByDescending(v => v.Bandwidth)
            .First();
    }

    private static int CodecOrder(IReadOnlyList<int> preference, int codecId)
    {
        for (var i = 0; i < preference.Count; i++)
        {
            if (preference[i] == codecId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static IEnumerable<BilibiliStreamCandidate> VideoCandidates(long cid, VideoSource.TDash dash) =>
        (dash.Video ?? [])
        .Where(v => v.Id > 0)
        .Select(v => ToCandidate(v, $"{cid}-v{v.Id}-c{v.CodecId}"))
        .OfType<BilibiliStreamCandidate>();

    private static IEnumerable<BilibiliStreamCandidate> AudioCandidates(long cid, VideoSource.TDash dash)
    {
        var fragments = new List<VideoSource.TDash.TFragement>();
        if (dash.Flac?.Audio is { } flac)
        {
            fragments.Add(flac);
        }

        fragments.AddRange(dash.Dolby?.Audio ?? []);
        fragments.AddRange(dash.Audio ?? []);
        return fragments.Select(a => ToAudio(cid, a)).OfType<BilibiliStreamCandidate>();
    }

    private static BilibiliStreamCandidate? BestAac(long cid, VideoSource.TDash dash) =>
        // dash.audio's order is not stable between requests: select explicitly.
        (dash.Audio ?? [])
        .Select(a => ToAudio(cid, a))
        .OfType<BilibiliStreamCandidate>()
        .OrderByDescending(a => a.Bandwidth)
        .ThenByDescending(a => a.Id)
        .FirstOrDefault();

    private static BilibiliStreamCandidate? ToAudio(long cid, VideoSource.TDash.TFragement? fragment) =>
        fragment == null ? null : ToCandidate(fragment, $"{cid}-a{fragment.Id}");

    private static BilibiliStreamCandidate? ToCandidate(VideoSource.TDash.TFragement fragment, string identityKey)
    {
        var urls = BilibiliCdnUrls.Candidates(fragment.BaseUrl, fragment.BackupUrl);
        return urls.Count == 0
            ? null
            : new BilibiliStreamCandidate(fragment.Id, fragment.CodecId, fragment.Bandwidth, fragment.Codecs, urls,
                identityKey);
    }

    private static BilibiliDurlSegment? ToSegment(long cid, VideoSource.TDurl durl)
    {
        var urls = BilibiliCdnUrls.Candidates(durl.Url, durl.BackupUrl);
        if (urls.Count == 0)
        {
            return null;
        }

        var primary = !string.IsNullOrEmpty(durl.Url) && urls.Contains(durl.Url) ? durl.Url : urls[0];
        var extension = BilibiliCdnUrls.GetExtension(primary) == ".flv" ? ".flv" : ".mp4";
        var fileName = BilibiliCdnUrls.GetFileName(primary);
        return new BilibiliDurlSegment(durl.Order, durl.Length, durl.Size, urls, extension,
            $"{cid}-d{durl.Order}-{fileName}", fileName);
    }
}
