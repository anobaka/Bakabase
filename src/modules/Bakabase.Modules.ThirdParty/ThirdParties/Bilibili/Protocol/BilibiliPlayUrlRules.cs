using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

public enum BilibiliPlayUrlOutcomeKind
{
    Dash = 1,
    Durl = 2,
    Skip = 3,
    /// <summary>Code 0 without anything playable: a protocol change, never a skip.</summary>
    NoStreams = 4,
}

public sealed record BilibiliPlayUrlOutcome(BilibiliPlayUrlOutcomeKind Kind, BilibiliSkip? Skip = null);

/// <summary>Decisions on <c>x/player/playurl</c> answers (both the 4048 request and the legacy naming request).</summary>
public static class BilibiliPlayUrlRules
{
    public const string PlayUrlEndpoint = "playurl";

    /// <summary>Absolute slack of the preview test, in ms.</summary>
    public const long PreviewToleranceMs = 50;

    /// <summary>
    /// Relative slack of the preview test: different transcodes of old durl-only archives differ from
    /// <c>timelength</c> by ~0.06%; the worst real preview is at 0.958.
    /// </summary>
    public const double PreviewRatio = 0.99;

    /// <summary>
    /// Decides a non-zero playurl code. Throws for codes that are not a content state of this page
    /// (risk control / busy → transient; -403, -400 without a PGC redirect, and unknown codes → fatal).
    /// </summary>
    /// <param name="endpoint">Endpoint name for the exceptions ("playurl", "playurl-naming").</param>
    public static BilibiliSkip DecideError(int code, string? message, bool isPgcRedirect,
        string endpoint = PlayUrlEndpoint)
    {
        switch (code)
        {
            case BilibiliApiCodes.SupporterOnly:
                return new(BilibiliSkipReason.SupporterOnly, code);
            case BilibiliApiCodes.NotFound when isPgcRedirect:
            case BilibiliApiCodes.BadRequest when isPgcRedirect:
                // Such archives list cids that belong to other aids or to no episode at all; -404/-400 does not
                // say that a membership would help.
                return new(BilibiliSkipReason.PgcEpisodeNotSupported, code);
            case BilibiliApiCodes.NotFound:
                // e.g. a cid now owned by another archive.
                return new(BilibiliSkipReason.Unavailable, code, message);
            case BilibiliApiCodes.PgcRestricted:
                return DecidePgcRestricted(message);
            // The archive changed state between view and playurl.
            case BilibiliApiCodes.ArchiveInvisible:
                return new(BilibiliSkipReason.Deleted, code, message);
            case BilibiliApiCodes.ArchiveUploaderOnly:
                return new(BilibiliSkipReason.PrivateToUploader, code, message);
            case BilibiliApiCodes.ArchiveUnderReview:
                return new(BilibiliSkipReason.UnderReview, code, message);
            default:
                throw BilibiliApiCodes.UnexpectedCode(endpoint, code, message);
        }
    }

    /// <summary>-10403: region lock when the message says so (地区), otherwise membership/purchase.</summary>
    public static BilibiliSkip DecidePgcRestricted(string? message) =>
        message?.Contains("地区", StringComparison.Ordinal) == true
            ? new(BilibiliSkipReason.RegionRestrictedOrHidden, BilibiliApiCodes.PgcRestricted, message)
            : new(BilibiliSkipReason.PgcMemberOrPaid, BilibiliApiCodes.PgcRestricted, message);

    public static BilibiliPlayUrlOutcome Decide(DataWrapper<VideoSource> rsp, bool isPgcRedirect)
    {
        if (rsp.Code != BilibiliApiCodes.Ok)
        {
            return new(BilibiliPlayUrlOutcomeKind.Skip, DecideError(rsp.Code, rsp.Message, isPgcRedirect));
        }

        var data = rsp.Data;
        if (data == null)
        {
            return new(BilibiliPlayUrlOutcomeKind.NoStreams);
        }

        if (HasUsableDashVideo(data))
        {
            return new(BilibiliPlayUrlOutcomeKind.Dash);
        }

        if (IsPreview(data))
        {
            return new(BilibiliPlayUrlOutcomeKind.Skip, new BilibiliSkip(isPgcRedirect
                ? BilibiliSkipReason.PgcEpisodeNotSupported
                : BilibiliSkipReason.PreviewOnly));
        }

        // Every segment needs a URL: a missing one would silently truncate the joined file.
        if (data.Durl is { Count: > 0 } durl &&
            durl.All(d => BilibiliCdnUrls.Candidates(d.Url, d.BackupUrl).Count > 0))
        {
            return new(BilibiliPlayUrlOutcomeKind.Durl);
        }

        return new(BilibiliPlayUrlOutcomeKind.NoStreams);
    }

    /// <summary>
    /// A trial clip instead of the whole video: no DASH although DASH was requested, and the durl segments add
    /// up to clearly less than <c>timelength</c> (Σlength &lt; timelength × <see cref="PreviewRatio"/> −
    /// <see cref="PreviewToleranceMs"/>). Never compare with view's duration and never look for "试看".
    /// </summary>
    public static bool IsPreview(VideoSource data)
    {
        if (HasUsableDashVideo(data) || data.Durl is not { Count: > 0 } durl || data.Timelength <= 0)
        {
            return false;
        }

        var sum = durl.Sum(d => d.Length);
        return sum < data.Timelength * PreviewRatio - PreviewToleranceMs;
    }

    private static bool HasUsableDashVideo(VideoSource data) =>
        data.Dash?.Video?.Any(v => v.Id > 0 && BilibiliCdnUrls.Candidates(v.BaseUrl, v.BackupUrl).Count > 0) ==
        true;
}
