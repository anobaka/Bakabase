using System;
using System.Collections.Concurrent;
using System.Threading;
using Bakabase.Modules.DataSync.Runtime;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Apply;

/// <summary>
/// Attempt records for the one-shot data sync tasks (§8.10.1, v3.1 B2 hardened): <c>{taskId, attemptId,
/// cancelRequested}</c>. The task's enqueuer registers an attempt and runs the body inside
/// <see cref="DataSyncTaskAttempts.Enter"/>, so the apply runner can tell, after <c>YieldAsync()</c> and again after
/// entering the gate, that the flag is clear and the attempt is still its own: a task the daemon started from a stale
/// list after <c>Clean</c> exits without writing.
/// </summary>
public sealed class DataSyncTaskRegistry : IDataSyncTaskRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public DataSyncTaskAttempt Register(string taskId)
    {
        ArgumentException.ThrowIfNullOrEmpty(taskId);
        var entry = new Entry(new DataSyncTaskAttempt(taskId, Guid.NewGuid()));
        _entries[taskId] = entry;
        return entry.Attempt;
    }

    /// <summary>
    /// Sets the flag with <see cref="Interlocked.Exchange(ref int, int)"/>, before the caller reads the task status
    /// (§8.10.1). False when no attempt is registered for the task.
    /// </summary>
    public bool RequestCancel(string taskId)
    {
        if (!_entries.TryGetValue(taskId, out var entry)) return false;
        Interlocked.Exchange(ref entry.CancelRequested, 1);
        return true;
    }

    public bool ShouldRun(string taskId, Guid attemptId) =>
        _entries.TryGetValue(taskId, out var entry) && entry.Attempt.AttemptId == attemptId &&
        Volatile.Read(ref entry.CancelRequested) == 0;

    /// <summary>The task's current attempt, or null when none is registered.</summary>
    public DataSyncTaskAttempt? Current(string taskId) =>
        _entries.TryGetValue(taskId, out var entry) ? entry.Attempt : null;

    /// <summary>Whether a cancel was requested for the task's current attempt.</summary>
    public bool IsCancelRequested(string taskId) =>
        _entries.TryGetValue(taskId, out var entry) && Volatile.Read(ref entry.CancelRequested) != 0;

    private sealed class Entry(DataSyncTaskAttempt attempt)
    {
        public DataSyncTaskAttempt Attempt { get; } = attempt;
        public int CancelRequested;
    }
}

/// <summary>
/// The attempt a task body runs as (§8.10.1). The body that enqueued the attempt enters it around the runner call:
/// <c>using (DataSyncTaskAttempts.Enter(attempt)) await runner.RunUndoAsync(id, args);</c>. Without one, the runner
/// checks only the cancel flag of the task's current attempt.
/// </summary>
public static class DataSyncTaskAttempts
{
    private static readonly AsyncLocal<DataSyncTaskAttempt?> Ambient = new();

    public static DataSyncTaskAttempt? Current => Ambient.Value;

    public static IDisposable Enter(DataSyncTaskAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var previous = Ambient.Value;
        Ambient.Value = attempt;
        return new Scope(previous);
    }

    /// <summary>
    /// Whether a body running as the ambient attempt (or, without one, the task's current attempt) may still write:
    /// no cancel was requested and the attempt is the registered one.
    /// </summary>
    public static bool MayRun(IDataSyncTaskRegistry registry, string taskId)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (Current is { } attempt)
            return attempt.TaskId != taskId || registry.ShouldRun(taskId, attempt.AttemptId);
        return registry is not DataSyncTaskRegistry concrete || !concrete.IsCancelRequested(taskId);
    }

    private sealed class Scope(DataSyncTaskAttempt? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
