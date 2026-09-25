using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;

namespace Bakabase.Abstractions.Components.Network;

/// <summary>
/// Decides whether a failed network call is worth making again unchanged, and how long to wait
/// first.
/// </summary>
/// <remarks>
/// <para>
/// A whitelist on purpose: only failures of the network itself qualify — the connection could not
/// be made or broke off (including a TLS handshake that ended early, which is what an unstable
/// proxy or a flaky image server usually looks like), a host name did not resolve, the request timed
/// out, or the server said it was temporarily unable to answer (408, 429, 5xx). Anything else — a
/// 404, a ban page, a parse error, a full disk, the caller's own cancellation — fails the same way
/// however often it is repeated, and repeating some of them (a ban especially) makes matters worse.
/// </para>
/// <para>
/// A plain <see cref="IOException"/> is deliberately <em>not</em> enough: file-system failures are
/// IOExceptions too. It only counts inside an <see cref="HttpRequestException"/>, as an
/// <see cref="HttpIOException"/>, or when caused by a <see cref="SocketException"/>.
/// </para>
/// </remarks>
public static class TransientNetworkError
{
    /// <summary>
    /// Whether <paramref name="exception"/>, or anything it wraps (inner exceptions and every member
    /// of an <see cref="AggregateException"/>), is a transient network failure.
    /// </summary>
    /// <param name="exception">The failure to classify.</param>
    /// <param name="callerToken">
    /// The token the failed call was given. Once it is cancelled nothing is transient: the caller
    /// asked to stop, and a timeout racing that request must not turn into another attempt.
    /// </param>
    public static bool IsTransient(Exception exception, CancellationToken callerToken = default)
    {
        if (callerToken.IsCancellationRequested)
        {
            return false;
        }

        return EnumerateCauses(exception).Any(IsTransientCause);
    }

    /// <summary>
    /// Exponential backoff with ±20% jitter: <paramref name="initial"/> before the first retry,
    /// doubling per retry, never more than <paramref name="max"/>. The jitter keeps callers that
    /// failed together from all retrying at the same instant.
    /// </summary>
    /// <param name="retryIndex">Zero for the first retry.</param>
    public static TimeSpan GetBackoffDelay(int retryIndex, TimeSpan initial, TimeSpan max)
    {
        var exponent = Math.Clamp(retryIndex, 0, 30);
        var milliseconds = Math.Min(max.TotalMilliseconds, initial.TotalMilliseconds * Math.Pow(2, exponent));
        milliseconds *= 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, max.TotalMilliseconds));
    }

    /// <summary>
    /// Whether a response with this status is the server saying "not right now" rather than "no":
    /// 408, 429 and every 5xx.
    /// </summary>
    public static bool IsTransientStatusCode(HttpStatusCode status) =>
        status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int) status >= 500;

    private static bool IsTransientCause(Exception exception) => exception switch
    {
        // The handshake completed far enough for the client to reject it: an invalid or untrusted
        // certificate (a wrong system clock, an intercepting proxy or antivirus) or no common
        // protocol. That fails identically every time; only a handshake cut short by the transport
        // (an IOException, as in "unexpected EOF") is worth another attempt.
        HttpRequestException
        {
            StatusCode: null, HttpRequestError: HttpRequestError.SecureConnectionError,
            InnerException: AuthenticationException
        } => false,
        HttpRequestException hre => hre.StatusCode is { } status
            ? IsTransientStatusCode(status)
            : IsTransientRequestError(hre.HttpRequestError) ||
              // Thrown without a classification (older call paths, or constructed by hand), but it
              // still wraps a transport failure: HttpClient only ever wraps I/O from the connection.
              (hre.HttpRequestError == HttpRequestError.Unknown &&
               hre.InnerException is IOException or SocketException),
        // Raised while reading a body after the headers arrived, e.g. the server hung up mid-image.
        HttpIOException hio => IsTransientRequestError(hio.HttpRequestError),
        SocketException => true,
        // HttpClient.Timeout elapsed. The caller's own cancellation was ruled out up front.
        OperationCanceledException { InnerException: TimeoutException } => true,
        // A service answered "not right now" in-band (e.g. HTTP 200 with a risk-control code).
        ITransientServiceError => true,
        _ => false,
    };

    // ProxyTunnelError also covers a proxy refusing CONNECT outright (407), which repeating cannot
    // fix; it stays because the far more common case is a local proxy whose upstream briefly failed
    // (502/504 on CONNECT), and the exception does not say which.
    private static bool IsTransientRequestError(HttpRequestError error) => error is
        HttpRequestError.NameResolutionError or
        HttpRequestError.ConnectionError or
        HttpRequestError.SecureConnectionError or
        HttpRequestError.ResponseEnded or
        HttpRequestError.ProxyTunnelError;

    private static IEnumerable<Exception> EnumerateCauses(Exception root)
    {
        var pending = new Stack<Exception>();
        pending.Push(root);
        // Exception graphs are trees in practice; the cap only guards against a pathological cycle.
        for (var visited = 0; pending.Count > 0 && visited < 64; visited++)
        {
            var current = pending.Pop();
            yield return current;

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
    }
}
