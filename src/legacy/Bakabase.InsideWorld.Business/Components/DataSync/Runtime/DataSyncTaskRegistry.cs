using System;
using System.Collections.Concurrent;
using System.Threading;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// Attempt records for data sync's one-shot tasks (spec §8.10.1, v3.1 B2): <c>{taskId, attemptId, cancelRequested}</c>.
/// </summary>
/// <remarks>
/// <para>
/// A task id is reused (<c>DataSyncApply</c>, <c>DataSyncRestore</c>, <c>DataSyncUndo:{logId}</c>), and
/// <c>BTaskManager.Clean</c> cannot stop a daemon loop that already read the old handler from its task list. So
/// every enqueue registers a new attempt, and the body runs only while its own attempt is the current one and no
/// cancel was asked for it: a body the daemon starts from a stale list exits without writing.
/// </para>
/// <para>
/// Cancel sets the flag with <see cref="Interlocked.Exchange(ref int, int)"/> <b>before</b> anyone reads the task's
/// status, which closes the race with the daemon: if the canceller still saw <c>NotStarted</c>, the body has not
/// reached its first check yet, and that check sees the flag.
/// </para>
/// </remarks>
public sealed class DataSyncTaskRegistry : IDataSyncTaskRegistry
{
    private sealed class Entry(Guid attemptId)
    {
        public Guid AttemptId { get; } = attemptId;
        public int CancelRequested;
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public DataSyncTaskAttempt Register(string taskId)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskId);
        var entry = new Entry(Guid.NewGuid());
        _entries[taskId] = entry;
        return new DataSyncTaskAttempt(taskId, entry.AttemptId);
    }

    public bool RequestCancel(string taskId)
    {
        if (!_entries.TryGetValue(taskId, out var entry)) return false;
        Interlocked.Exchange(ref entry.CancelRequested, 1);
        return true;
    }

    public bool ShouldRun(string taskId, Guid attemptId) =>
        _entries.TryGetValue(taskId, out var entry) && entry.AttemptId == attemptId &&
        Volatile.Read(ref entry.CancelRequested) == 0;

    /// <summary>The current attempt of a task id, or null when none was registered.</summary>
    public DataSyncTaskAttempt? GetCurrent(string taskId) =>
        _entries.TryGetValue(taskId, out var entry) ? new DataSyncTaskAttempt(taskId, entry.AttemptId) : null;
}

/// <summary>
/// The attempt a data sync task body runs as, flowing with the body's async calls. The runner
/// (<see cref="IDataSyncApplyRunner"/>) takes only <c>BTaskArgs</c>, so it checks the attempt again after entering the
/// gate through <see cref="DataSyncTaskAttemptContextExtensions.ShouldRunCurrent"/> (§8.10.1: "after YieldAsync() and
/// again after entering the gate").
/// </summary>
public static class DataSyncTaskAttemptContext
{
    private static readonly AsyncLocal<DataSyncTaskAttempt?> CurrentAttempt = new();

    /// <summary>The attempt of the data sync task body this code runs in; null outside one.</summary>
    public static DataSyncTaskAttempt? Current => CurrentAttempt.Value;

    /// <summary>Sets <see cref="Current"/> until the returned scope is disposed.</summary>
    public static IDisposable Enter(DataSyncTaskAttempt attempt)
    {
        var previous = CurrentAttempt.Value;
        CurrentAttempt.Value = attempt;
        return new Scope(previous);
    }

    private sealed class Scope(DataSyncTaskAttempt? previous) : IDisposable
    {
        public void Dispose() => CurrentAttempt.Value = previous;
    }
}

public static class DataSyncTaskAttemptContextExtensions
{
    /// <summary>
    /// False when the data sync task body this code runs in must stop without writing: its attempt was replaced or
    /// cancelled. True outside a task body (a request, a test).
    /// </summary>
    public static bool ShouldRunCurrent(this IDataSyncTaskRegistry registry) =>
        DataSyncTaskAttemptContext.Current is not { } attempt || registry.ShouldRun(attempt.TaskId, attempt.AttemptId);
}
