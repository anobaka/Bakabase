namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

/// <summary>
/// Why an item or a page was not downloaded on purpose. Every member is a definite, per-item content state
/// (never risk control, a busy service or an expired login — those abort the run instead).
/// Values are persisted nowhere but are exported to the web constants, so never renumber them.
/// </summary>
public enum BilibiliSkipReason
{
    /// <summary>The favorites entry is marked invalid (<c>attr &amp; 1</c>): removed or invalidated.</summary>
    InvalidItem = 1,
    UnsupportedOgvEpisode = 2,
    UnsupportedAudio = 3,
    UnsupportedCollection = 4,
    /// <summary><see cref="BilibiliSkip.Code"/> holds the favorites item type.</summary>
    UnsupportedItemType = 5,
    InteractiveVideo = 6,
    Deleted = 7,
    PrivateToUploader = 8,
    UnderReview = 9,
    RegionRestrictedOrHidden = 10,
    AccessDenied = 11,
    SupporterOnly = 12,
    SupporterOnlyPreview = 13,
    /// <summary>A bangumi/film episode refused with <c>-10403</c> (membership or purchase required).</summary>
    PgcMemberOrPaid = 14,
    PreviewOnly = 15,
    /// <summary>playurl <c>-404</c> without a PGC redirect, and CDN exhaustion reported by callers.</summary>
    Unavailable = 16,
    /// <summary>
    /// A page of an archive that redirects to a bangumi/film episode, refused with playurl <c>-404</c>/<c>-400</c>
    /// or answered with a preview: not supported yet (it may need a membership, or the cid may belong to no
    /// episode at all).
    /// </summary>
    PgcEpisodeNotSupported = 17,
    /// <summary>ffmpeg could not merge the streams even after every fallback.</summary>
    MergeFailed = 18,
    /// <summary>Every CDN URL for a stream is dead (4xx / an HTML error page), even after refreshing them.</summary>
    CdnUnavailable = 19,
}

/// <summary>A decision to skip.</summary>
/// <param name="Code">The Bilibili API code (or the favorites item type for
/// <see cref="BilibiliSkipReason.UnsupportedItemType"/>, or an HTTP / exit code for the transport reasons).</param>
/// <param name="Message">Bilibili's own message, when there is one. Short; never a body or a URL.</param>
public sealed record BilibiliSkip(BilibiliSkipReason Reason, int? Code = null, string? Message = null);

public static class BilibiliSkipReasons
{
    /// <summary>
    /// Reasons that hold only for the account asking. An expired or revoked login produces them as well (an
    /// anonymous answer), so such a skip is recorded only after the login has been confirmed.
    /// </summary>
    public static bool DependsOnLogin(BilibiliSkipReason reason) => reason is BilibiliSkipReason.SupporterOnly
        or BilibiliSkipReason.SupporterOnlyPreview or BilibiliSkipReason.PgcMemberOrPaid
        or BilibiliSkipReason.PreviewOnly or BilibiliSkipReason.PgcEpisodeNotSupported
        or BilibiliSkipReason.AccessDenied;
}
