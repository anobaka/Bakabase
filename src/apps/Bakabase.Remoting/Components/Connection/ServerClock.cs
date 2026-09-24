using System.Globalization;

namespace Bakabase.Remoting.Components.Connection;

/// <summary>
/// This client's estimate of the server's clock.
/// </summary>
/// <remarks>
/// <para>
/// Signatures carry a timestamp the server checks against a five-minute window, and the
/// two machines are not obliged to agree — a desktop with a wrong timezone offset, or a
/// container whose host clock drifted, is enough to make every request look expired.
/// Rather than send someone to re-pair over what is a clock problem, the handshake
/// reports the server's time and this holds the offset.
/// </para>
/// <para>
/// The offset is measured across a round trip, so half the elapsed time is attributed to
/// the response's travel. That is only an estimate, but the window it feeds is five
/// minutes wide; being a few hundred milliseconds off costs nothing.
/// </para>
/// </remarks>
public sealed class ServerClock(Func<DateTime>? localNow = null)
{
    private readonly Func<DateTime> _localNow = localNow ?? (() => DateTime.UtcNow);
    private long _offsetTicks;

    /// <summary>How far the server's clock sits ahead of this machine's.</summary>
    public TimeSpan Offset => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>
    /// This machine's own time, uncorrected. Exposed so everything that measures a round
    /// trip reads the same source this class does — a caller that reached for
    /// <c>DateTime.UtcNow</c> instead would be comparing two different clocks.
    /// </summary>
    public DateTime LocalNow => _localNow();

    /// <summary>The time to stamp on an outgoing signature.</summary>
    public DateTime Now => _localNow().Add(Offset);

    public long NowUnixSeconds => new DateTimeOffset(Now, TimeSpan.Zero).ToUnixTimeSeconds();

    /// <summary>
    /// Records what the server said its time was, and when the request that asked went
    /// out. Both are needed: the answer describes a moment already in the past by the
    /// time it arrives.
    /// </summary>
    public void Synchronize(DateTime serverTime, DateTime requestSentAt)
    {
        var receivedAt = _localNow();
        var roundTrip = receivedAt - requestSentAt;

        // A negative round trip means the caller passed a "sent" time from the future.
        // Nothing sensible follows from that, so ignore the sample rather than encode
        // nonsense into every subsequent signature.
        if (roundTrip < TimeSpan.Zero)
        {
            return;
        }

        var localAtServerTime = requestSentAt.Add(roundTrip / 2);
        Interlocked.Exchange(ref _offsetTicks, (serverTime.ToUniversalTime() - localAtServerTime).Ticks);
    }

    /// <summary>
    /// Reads the <c>serverTime</c> a handshake reported. Returns null for anything
    /// unparseable, which is what an older server that does not send it looks like.
    /// </summary>
    public static DateTime? ParseServerTime(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal |
                                                              DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
