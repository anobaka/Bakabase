using System.Net;
using Bakabase.Abstractions.Components.Network;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

public enum BilibiliApiCodeClass
{
    Ok = 0,
    /// <summary>Anti-bot refusal: back off (transient).</summary>
    RiskControl = 1,
    /// <summary>The service is overloaded or restarting: retry later (transient).</summary>
    ServiceBusy = 2,
    /// <summary>The cookie is not (or no longer) logged in: fatal.</summary>
    NotLoggedIn = 3,
    /// <summary>A per-item state (deleted, private, region, supporter-only…) the decision tables interpret.</summary>
    ContentState = 4,
    /// <summary>Not understood: fatal, the checkpoint must not advance.</summary>
    Unknown = 5,
}

/// <summary>
/// Classifies Bilibili's in-band codes. Only the codes listed here are ever interpreted as a content state;
/// anything else is <see cref="BilibiliApiCodeClass.Unknown"/> and stops the run rather than being skipped.
/// </summary>
public static class BilibiliApiCodes
{
    public const int Ok = 0;
    public const int NotLoggedIn = -101;
    public const int SystemUpgrading = -112;
    public const int BadRequest = -400;
    public const int Unauthenticated = -401;
    public const int AccessDenied = -403;
    public const int NotFound = -404;
    public const int RiskControl = -352;
    public const int RiskControlLegacy = -412;
    public const int ServerError = -500;
    public const int ServiceUnavailable = -503;
    public const int ServiceTimeout = -504;
    public const int TooFrequent = -509;
    public const int TooFrequentAlt = -799;
    public const int TooFrequentLegacy = -702;
    public const int ServerHiccup = -8888;
    public const int PgcRestricted = -10403;
    public const int ArchiveInvisible = 62002;
    public const int ArchiveUnderReview = 62004;
    public const int ArchiveUploaderOnly = 62012;
    public const int SupporterOnly = 87008;

    /// <param name="code">The top-level <c>code</c>.</param>
    /// <param name="vVoucher"><c>data.v_voucher</c>: a captcha challenge. Non-empty means risk control,
    /// whatever the code (it is also sent with code 0).</param>
    /// <remarks>
    /// -403 is a content state for <c>view</c> only and -400 for playurl on a PGC-redirect archive only; the
    /// decision tables (<see cref="BilibiliArchiveRules"/>, <see cref="BilibiliPlayUrlRules"/>) reject them
    /// everywhere else.
    /// </remarks>
    public static BilibiliApiCodeClass Classify(int code, string? vVoucher)
    {
        if (!string.IsNullOrEmpty(vVoucher))
        {
            return BilibiliApiCodeClass.RiskControl;
        }

        return code switch
        {
            Ok => BilibiliApiCodeClass.Ok,
            RiskControl or RiskControlLegacy or TooFrequent or TooFrequentAlt or Unauthenticated =>
                BilibiliApiCodeClass.RiskControl,
            ServerError or ServiceUnavailable or ServiceTimeout or ServerHiccup or SystemUpgrading
                or TooFrequentLegacy => BilibiliApiCodeClass.ServiceBusy,
            NotLoggedIn => BilibiliApiCodeClass.NotLoggedIn,
            ArchiveInvisible or ArchiveUnderReview or ArchiveUploaderOnly or SupporterOnly or NotFound
                or PgcRestricted or AccessDenied or BadRequest => BilibiliApiCodeClass.ContentState,
            _ => BilibiliApiCodeClass.Unknown,
        };
    }

    /// <summary>
    /// Classifies a non-2xx HTTP status from an <b>API</b> endpoint: 412 and 403 are risk control (the API
    /// answers content states in-band with HTTP 200), 408/429/5xx a busy service, anything else
    /// <see cref="BilibiliApiCodeClass.Unknown"/>. Not for CDN responses, where 403 means a dead URL.
    /// </summary>
    public static BilibiliApiCodeClass ClassifyApiHttpStatus(HttpStatusCode status)
    {
        if ((int) status is >= 200 and < 300)
        {
            return BilibiliApiCodeClass.Ok;
        }

        if (status is HttpStatusCode.PreconditionFailed or HttpStatusCode.Forbidden)
        {
            return BilibiliApiCodeClass.RiskControl;
        }

        return TransientNetworkError.IsTransientStatusCode(status)
            ? BilibiliApiCodeClass.ServiceBusy
            : BilibiliApiCodeClass.Unknown;
    }

    /// <summary>
    /// The exception for a code a decision table does not accept: risk control / busy become the transient
    /// <see cref="BilibiliTemporarilyUnavailableException"/>, everything else a fatal
    /// <see cref="BilibiliApiException"/>.
    /// </summary>
    public static Exception UnexpectedCode(string endpoint, int code, string? message, string? vVoucher = null) =>
        Classify(code, vVoucher) switch
        {
            BilibiliApiCodeClass.RiskControl => new BilibiliTemporarilyUnavailableException(
                BilibiliTemporaryFailureKind.RiskControl, endpoint, code),
            BilibiliApiCodeClass.ServiceBusy => new BilibiliTemporarilyUnavailableException(
                BilibiliTemporaryFailureKind.ServiceBusy, endpoint, code),
            _ => new BilibiliApiException(endpoint, code, message),
        };
}
