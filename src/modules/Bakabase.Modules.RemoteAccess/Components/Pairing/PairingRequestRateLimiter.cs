using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.Modules.RemoteAccess.Components.Pairing;

/// <summary>
/// Bounds what an uncredentialed caller can make the server do by asking to pair.
/// </summary>
/// <remarks>
/// <para>
/// Filing a request needs no credentials at all — it cannot, since the point is to get
/// some. Each one writes to disk and raises a notification, so without a bound a caller
/// on the network could grow <c>devices.json</c>, bury a real request under a wall of
/// fake ones, and fill the notification centre.
/// </para>
/// <para>
/// The per-address budget is what actually refuses the flood. The notification throttle
/// is separate and global, because many addresses can each stay inside their own budget
/// and still add up to a wall of notifications. Which requests were actually announced is
/// remembered here too, so a device that files again while its announced request still
/// waits is not announced twice.
/// </para>
/// <para>
/// In memory only: a restart forgives everything, which is the right trade for something
/// whose worst case is bounded anyway — at most, one device is announced once more.
/// </para>
/// </remarks>
public sealed class PairingRequestRateLimiter(Func<DateTime>? now = null)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Requests one address may file per <see cref="Window"/>. Generous enough for
    /// someone retrying after a typo, far short of useful for a flood.
    /// </summary>
    public const int MaxPerAddress = 3;

    /// <summary>
    /// Quiet period between notifications, whoever they come from. A second device
    /// pairing within this window still appears in the settings list; it just does not
    /// raise its own notification.
    /// </summary>
    public static readonly TimeSpan NotificationInterval = TimeSpan.FromMinutes(2);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly List<(string Address, DateTime At)> _requests = [];
    private readonly Dictionary<string, DateTime> _announced = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private DateTime? _lastNotifiedAt;

    /// <summary>
    /// Whether this address may file another request, counting it if so.
    /// </summary>
    /// <param name="remoteAddress">
    /// Null when the peer address is unknown, which is lumped into one bucket rather
    /// than exempted — an unknown address is not a reason to stop counting.
    /// </param>
    public bool TryTake(string? remoteAddress)
    {
        var address = string.IsNullOrEmpty(remoteAddress) ? "unknown" : remoteAddress;
        var now = _now();

        lock (_gate)
        {
            _requests.RemoveAll(r => now - r.At >= Window);

            if (_requests.Count(r => string.Equals(r.Address, address, StringComparison.Ordinal)) >= MaxPerAddress)
            {
                return false;
            }

            _requests.Add((address, now));
            return true;
        }
    }

    /// <summary>Whether a notification may be raised now, counting it if so.</summary>
    public bool TryNotify()
    {
        var now = _now();

        lock (_gate)
        {
            if (_lastNotifiedAt.HasValue && now - _lastNotifiedAt.Value < NotificationInterval)
            {
                return false;
            }

            _lastNotifiedAt = now;
            return true;
        }
    }

    /// <summary>
    /// Records that a notification about this request actually went out, until the request
    /// itself lapses.
    /// </summary>
    /// <remarks>
    /// Called only once the notification exists: a device that files again is kept quiet
    /// only by an earlier request of its own that somebody was told about. One held back
    /// by <see cref="TryNotify"/>, or one whose notification could not be created, told
    /// nobody, so the next request from that device is still news.
    /// </remarks>
    public void MarkAnnounced(string requestId, DateTime expiresAt)
    {
        lock (_gate)
        {
            ForgetLapsedAnnouncements(_now());
            _announced[requestId] = expiresAt;
        }
    }

    /// <summary>Whether a notification about this request went out, and the request has not lapsed.</summary>
    public bool WasAnnounced(string requestId)
    {
        lock (_gate)
        {
            ForgetLapsedAnnouncements(_now());
            return _announced.ContainsKey(requestId);
        }
    }

    /// <summary>Bounds the record by the requests themselves, which are bounded already.</summary>
    private void ForgetLapsedAnnouncements(DateTime now)
    {
        foreach (var id in _announced.Where(a => a.Value <= now).Select(a => a.Key).ToList())
        {
            _announced.Remove(id);
        }
    }
}
