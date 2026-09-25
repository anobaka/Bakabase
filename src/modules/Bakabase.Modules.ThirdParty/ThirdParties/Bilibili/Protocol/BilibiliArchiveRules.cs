using System.Text.RegularExpressions;
using Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

public enum BilibiliViewOutcomeKind
{
    /// <summary>The view answer lists the pages: download them.</summary>
    Proceed = 1,
    /// <summary>The archive was merged into <see cref="BilibiliViewOutcome.ForwardAid"/>: ask view for that.</summary>
    FollowForward = 2,
    /// <summary>view said -404: ask pagelist whether it is deleted or only hidden
    /// (<see cref="BilibiliArchiveRules.DecideAfterNotFound"/>).</summary>
    CheckExistence = 3,
    /// <summary>view said -403 (login-only legacy archive): take the pages from pagelist
    /// (<see cref="BilibiliArchiveRules.DecidePageListFallback"/>).</summary>
    PageListFallback = 4,
    Skip = 5,
}

public sealed record BilibiliViewOutcome(BilibiliViewOutcomeKind Kind, BilibiliSkip? Skip = null, long? ForwardAid = null);

/// <summary>Decisions on <c>x/web-interface/view</c> and <c>x/player/pagelist</c> answers.</summary>
public static partial class BilibiliArchiveRules
{
    public const string ViewEndpoint = "view";
    public const string PageListEndpoint = "pagelist";
    public const int MaxForwardDepth = 1;

    /// <summary>
    /// Risk control, busy and -101 answers never get here in practice (the client throws for them); codes the
    /// table does not know throw too (<see cref="BilibiliApiCodes.UnexpectedCode"/>).
    /// </summary>
    public static BilibiliViewOutcome DecideView(DataWrapper<Post> view, long requestedAid, int forwardDepth)
    {
        switch (view.Code)
        {
            case BilibiliApiCodes.Ok:
            {
                var data = view.Data ?? throw new BilibiliProtocolException(ViewEndpoint, "code 0 without data");
                if (IsInteractive(data))
                {
                    return Skip(BilibiliSkipReason.InteractiveVideo);
                }

                if (data.Pages is { Count: > 0 })
                {
                    return new(BilibiliViewOutcomeKind.Proceed);
                }

                if (data.Forward is { } forward && forward > 0 && forward != requestedAid &&
                    forwardDepth < MaxForwardDepth)
                {
                    return new(BilibiliViewOutcomeKind.FollowForward, ForwardAid: forward);
                }

                return Skip(BilibiliSkipReason.Deleted);
            }
            case BilibiliApiCodes.ArchiveInvisible:
                return Skip(BilibiliSkipReason.Deleted, view);
            case BilibiliApiCodes.ArchiveUploaderOnly:
                return Skip(BilibiliSkipReason.PrivateToUploader, view);
            case BilibiliApiCodes.ArchiveUnderReview:
                return Skip(BilibiliSkipReason.UnderReview, view);
            case BilibiliApiCodes.SupporterOnly:
                return Skip(BilibiliSkipReason.SupporterOnly, view);
            case BilibiliApiCodes.PgcRestricted:
                return new(BilibiliViewOutcomeKind.Skip, BilibiliPlayUrlRules.DecidePgcRestricted(view.Message));
            case BilibiliApiCodes.NotFound:
                return new(BilibiliViewOutcomeKind.CheckExistence);
            case BilibiliApiCodes.AccessDenied:
                return new(BilibiliViewOutcomeKind.PageListFallback);
            default:
                throw BilibiliApiCodes.UnexpectedCode(ViewEndpoint, view.Code, view.Message);
        }
    }

    /// <summary>After view -404: listed pages mean the archive exists but is hidden here; otherwise it is gone.</summary>
    public static BilibiliSkip DecideAfterNotFound(DataWrapper<List<PostPage>> pageList)
    {
        if (pageList.Code == BilibiliApiCodes.Ok && pageList.Data is { Count: > 0 })
        {
            return new(BilibiliSkipReason.RegionRestrictedOrHidden, BilibiliApiCodes.NotFound);
        }

        if (pageList.Code == BilibiliApiCodes.Ok ||
            BilibiliApiCodes.Classify(pageList.Code, null) == BilibiliApiCodeClass.ContentState)
        {
            return new(BilibiliSkipReason.Deleted, BilibiliApiCodes.NotFound);
        }

        throw BilibiliApiCodes.UnexpectedCode(PageListEndpoint, pageList.Code, pageList.Message);
    }

    /// <summary>After view -403: pagelist still lists the pages of login-only legacy archives.</summary>
    public static (IReadOnlyList<PostPage>? Pages, BilibiliSkip? Skip) DecidePageListFallback(
        DataWrapper<List<PostPage>> pageList)
    {
        if (pageList.Code == BilibiliApiCodes.Ok && pageList.Data is { Count: > 0 } pages)
        {
            return (pages, null);
        }

        if (pageList.Code == BilibiliApiCodes.Ok ||
            BilibiliApiCodes.Classify(pageList.Code, null) == BilibiliApiCodeClass.ContentState)
        {
            return (null, new BilibiliSkip(BilibiliSkipReason.AccessDenied, BilibiliApiCodes.AccessDenied));
        }

        throw BilibiliApiCodes.UnexpectedCode(PageListEndpoint, pageList.Code, pageList.Message);
    }

    /// <summary>
    /// Supporter-only (充电专属) archives this account cannot play. Gated on <c>is_upower_play</c>, not
    /// <c>is_upower_exclusive</c>: time-limited free ones are exclusive and playable. <c>rights.pay</c> and
    /// friends are not gated (unreliable); the playurl preview check covers paid archives. Evaluate it only
    /// after the existing-file check, so a video downloaded while the user had access is not reported.
    /// </summary>
    public static BilibiliSkip? GateAccess(Post view) =>
        view is { IsUpowerExclusive: true, IsUpowerPlay: false }
            ? new BilibiliSkip(view.IsUpowerPreview
                ? BilibiliSkipReason.SupporterOnlyPreview
                : BilibiliSkipReason.SupporterOnly)
            : null;

    public static bool IsInteractive(Post view) => view.Rights?.IsSteinGate == 1;

    /// <summary>Whether the archive is really a bangumi/film episode (<c>…/bangumi/play/ep…</c>).</summary>
    public static bool IsPgcRedirect(string? redirectUrl) =>
        redirectUrl?.Contains("/bangumi/play/ep", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>The ep_id of a PGC redirect (phase-2 seam), or null.</summary>
    public static long? TryGetEpisodeId(string? redirectUrl)
    {
        if (string.IsNullOrEmpty(redirectUrl))
        {
            return null;
        }

        var match = EpisodeIdRegex().Match(redirectUrl);
        return match.Success && long.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    private static BilibiliViewOutcome Skip(BilibiliSkipReason reason, DataWrapper<Post>? view = null) =>
        new(BilibiliViewOutcomeKind.Skip, new BilibiliSkip(reason, view?.Code, view?.Message));

    [GeneratedRegex(@"/ep(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeIdRegex();
}
