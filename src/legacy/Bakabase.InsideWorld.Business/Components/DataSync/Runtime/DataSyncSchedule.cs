using System;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>When a link is due again (§8.2, "When a link is due").</summary>
public static class DataSyncSchedule
{
    /// <summary>After the start, every link is due this soon; the first head round also verifies the actor (§5.6).</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    /// <summary>After a head with nothing new, and after a successful pull.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    /// <summary><c>AccessRevoked</c>, <c>PeerSharingOff</c>, <c>PeerRemoteAccessOff</c>, <c>PeerRestorePending</c>.</summary>
    public static readonly TimeSpan AccessRetry = TimeSpan.FromMinutes(60);

    /// <summary><c>PeerTooOld</c>, <c>ThisTooOld</c>.</summary>
    public static readonly TimeSpan VersionRetry = TimeSpan.FromHours(6);

    /// <summary>
    /// How long one pull may take, from its manifest to its last page (a restart with a new manifest included), before
    /// it is given up as <c>Unreachable</c> (<c>timeout</c>). The <c>DataSync</c> task fetches its due links one after
    /// another, so a source that keeps serving pages must not hold every other link waiting (§12: everything
    /// peer-supplied is budgeted). Checked between calls, each of which has its own deadline.
    /// </summary>
    public static readonly TimeSpan SnapshotDeadline = TimeSpan.FromMinutes(10);

    /// <summary>A last full reconciliation older than this makes the next pull one (§8.8).</summary>
    public static readonly TimeSpan FullReconciliationInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// An approver that has waited this long for its peer's first review may start anyway (§8.3,
    /// <c>DataSyncResumeAction.StartAnyway</c>).
    /// </summary>
    public static readonly TimeSpan StartAnywayAfter = TimeSpan.FromDays(7);

    private static readonly TimeSpan[] FailureBackoff =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)];

    /// <summary>
    /// The wait after a failure (<c>Unreachable</c>, <c>Busy</c>, a failed apply): the peer's <c>Retry-After</c> when
    /// it gave one, else 1 → 2 → 5 → 10 min by the number of consecutive failures, reset on success.
    /// </summary>
    public static TimeSpan Backoff(int consecutiveFailures, int? retryAfterSeconds = null)
    {
        if (retryAfterSeconds is > 0) return TimeSpan.FromSeconds(retryAfterSeconds.Value);
        var index = Math.Clamp(consecutiveFailures - 1, 0, FailureBackoff.Length - 1);
        return FailureBackoff[index];
    }
}
