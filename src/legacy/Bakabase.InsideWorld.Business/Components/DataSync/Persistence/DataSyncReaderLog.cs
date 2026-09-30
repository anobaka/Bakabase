using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Persistence;

/// <summary>
/// The latest read of each reader, in memory (§7.5.6). Heads arrive every few seconds; the row is written only on
/// a reader's first read, when its declared mode or state changes, or once <see cref="PersistInterval"/> has passed
/// since the last write. It powers "read 1 min ago" and the reader's state line, never a sync decision.
/// </summary>
public sealed class DataSyncReaderLog
{
    public static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <param name="PersistedAtUtc">When the row last got these values; null until this process wrote it.</param>
    public sealed record Entry(string Name, DateTime LastReadAtUtc, long LastSeqServed, string? Mode, string? State,
        DateTime? PersistedAtUtc, string? PersistedMode, string? PersistedState);

    public Entry? Get(string nodeId) => _entries.TryGetValue(nodeId, out var entry) ? entry : null;

    public IReadOnlyDictionary<string, Entry> Snapshot() => new Dictionary<string, Entry>(_entries, StringComparer.Ordinal);

    public void Set(string nodeId, Entry entry) => _entries[nodeId] = entry;

    public void Forget(string nodeId) => _entries.TryRemove(nodeId, out _);

    /// <summary>Whether a read changes what the row must say now (§7.5.6), given what this process last wrote.</summary>
    public static bool MustPersist(Entry? known, string? mode, string? state, DateTime nowUtc) =>
        known?.PersistedAtUtc is not { } persistedAt ||
        !string.Equals(known.PersistedMode, mode, StringComparison.Ordinal) ||
        !string.Equals(known.PersistedState, state, StringComparison.Ordinal) ||
        nowUtc - persistedAt >= PersistInterval;
}
