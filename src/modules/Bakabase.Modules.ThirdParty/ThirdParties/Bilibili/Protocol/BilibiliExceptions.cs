using Bakabase.Abstractions.Components.Network;
using Bakabase.Abstractions.Exceptions;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Protocol;

// Every message below is built from an endpoint NAME (e.g. "playurl"), numeric codes and short, redacted
// texts only: these messages end up in task messages (persisted, shown, copied), logs and error reports.
// Never put a URL (signed CDN URLs carry mid/oi/upsig), a cookie or a response body in them.

public enum BilibiliTemporaryFailureKind
{
    RiskControl = 1,
    ServiceBusy = 2,
    CdnUnavailable = 3,
}

/// <summary>
/// Bilibili answered "not right now": risk control (-352/-412/v_voucher/HTTP 412…), an overloaded service
/// (-500/-503/-504, HTTP 5xx…), or no CDN host would serve a stream. Transient (the run is retried from the
/// checkpoint) and user-actionable (it is not a Bakabase defect).
/// </summary>
public sealed class BilibiliTemporarilyUnavailableException(
    BilibiliTemporaryFailureKind kind,
    string endpoint,
    int? code,
    Exception? inner = null,
    int? httpStatus = null)
    : Exception(BuildMessage(kind, endpoint, code, httpStatus), inner), ITransientServiceError,
        IUserActionableException
{
    public BilibiliTemporaryFailureKind Kind { get; } = kind;
    public string Endpoint { get; } = endpoint;

    /// <summary>The API code, when the refusal came in-band.</summary>
    public int? Code { get; } = code;

    /// <summary>The HTTP status, when the refusal was an HTTP status.</summary>
    public int? HttpStatus { get; } = httpStatus;

    private static string BuildMessage(BilibiliTemporaryFailureKind kind, string endpoint, int? code,
        int? httpStatus)
    {
        var detail = kind.ToString();
        if (code is { } c)
        {
            detail += $", code {c}";
        }

        if (httpStatus is { } s)
        {
            detail += $", HTTP {s}";
        }

        return $"Bilibili {endpoint} is temporarily unavailable ({detail}).";
    }
}

/// <summary>The cookie is missing, expired or belongs to nobody (-101 / myinfo without a profile). Fatal.</summary>
public sealed class BilibiliNotLoggedInException(string message) : Exception(message), IUserActionableException;

/// <summary>
/// Bilibili answered with a code this client does not know how to interpret, or a code that is fatal for
/// the endpoint. Fatal: a run must never skip-and-checkpoint its way past a protocol change.
/// </summary>
public sealed class BilibiliApiException(string endpoint, int code, string? apiMessage)
    : Exception(
        $"Bilibili {endpoint} returned code {code}{(string.IsNullOrWhiteSpace(apiMessage) ? "" : $": {BilibiliDiagnostics.RedactText(apiMessage)}")}")
{
    public string Endpoint { get; } = endpoint;
    public int Code { get; } = code;
}

/// <summary>
/// The response did not have the expected shape (not JSON, no code, a field of the wrong type, code 0 without
/// streams…). Fatal. Callers must not pass a JSON library exception as <paramref name="inner"/>: its message
/// quotes the offending value.
/// </summary>
public sealed class BilibiliProtocolException(string endpoint, string detail, Exception? inner = null)
    : Exception($"Unexpected response from Bilibili {endpoint}: {detail}", inner)
{
    public string Endpoint { get; } = endpoint;
}
