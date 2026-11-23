using System.Collections.Concurrent;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>Serializes mutations of one acquisition task and its associated run.</summary>
public static class AcquisitionTaskGate
{
    private sealed class Entry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal bool CleaningIntermediates;
    }

    private static readonly ConcurrentDictionary<int, Entry> Entries = new();
    private static Entry Get(int taskId) => Entries.GetOrAdd(taskId, _ => new());
    public static SemaphoreSlim For(int taskId) => Get(taskId).Gate;

    public static IDisposable? TryEnterIntermediateCleanup(int taskId, CancellationToken ct)
    {
        var entry = Get(taskId);
        lock (entry)
        {
            if (!entry.Gate.Wait(0, ct)) return null;
            entry.CleaningIntermediates = true;
            return new CleanupLease(entry);
        }
    }

    public static bool IsCleaningIntermediates(int taskId)
    {
        if (!Entries.TryGetValue(taskId, out var entry)) return false;
        lock (entry) return entry.CleaningIntermediates;
    }

    private sealed class CleanupLease(Entry owner) : IDisposable
    {
        private Entry? held = owner;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref held, null) is not {} entry) return;
            lock (entry)
            {
                entry.CleaningIntermediates = false;
                entry.Gate.Release();
            }
        }
    }
}
