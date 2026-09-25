using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.DataSync.Merging;
using Bakabase.Modules.DataSync.Runtime;
using Bakabase.Modules.DataSync.Wire;

namespace Bakabase.InsideWorld.Business.Components.DataSync.Runtime;

/// <summary>
/// Pulls the <c>DataSync</c> task fetched, waiting for the <c>DataSyncApply</c> task (§2.9, §4.7). In memory and lost
/// on restart: one per link (a newer one replaces it), at most <see cref="DataSyncLimits.MaxStagedPullBytes"/> each and
/// <see cref="MaxTotalBytes"/> in total. Over the total, the oldest pulls are dropped; their links are refetched by
/// the next head poll, because their cursors never moved.
/// </summary>
public sealed class DataSyncStagedPullStore : IDataSyncStagedPullStore
{
    public const long DefaultMaxTotalBytes = 256L << 20;

    private readonly record struct Slot(DataSyncStagedPull Pull, long Bytes, long Order);

    private readonly object _lock = new();
    private readonly Dictionary<int, Slot> _slots = new();
    private readonly long _maxPullBytes;
    private long _order;

    public DataSyncStagedPullStore(DataSyncLimits limits) : this(limits, DefaultMaxTotalBytes)
    {
    }

    public DataSyncStagedPullStore(DataSyncLimits limits, long maxTotalBytes)
    {
        _maxPullBytes = limits.MaxStagedPullBytes;
        MaxTotalBytes = maxTotalBytes;
    }

    public long MaxTotalBytes { get; }

    public void Put(int linkId, DataSyncStagedPull pull) => Put(linkId, pull, EstimateBytes(pull));

    /// <summary>
    /// Stages <paramref name="pull"/> for <paramref name="linkId"/>, replacing the link's previous pull.
    /// <paramref name="bytes"/> is what the pull cost to read (the page bytes). False, and nothing staged, when the
    /// pull alone is larger than one link may stage.
    /// </summary>
    public bool Put(int linkId, DataSyncStagedPull pull, long bytes)
    {
        ArgumentNullException.ThrowIfNull(pull);
        if (bytes > _maxPullBytes) return false;
        lock (_lock)
        {
            _slots[linkId] = new Slot(pull, Math.Max(0, bytes), ++_order);
            var total = _slots.Values.Sum(s => s.Bytes);
            while (total > MaxTotalBytes && _slots.Count > 1)
            {
                var oldest = _slots.Where(s => s.Key != linkId).MinBy(s => s.Value.Order);
                _slots.Remove(oldest.Key);
                total -= oldest.Value.Bytes;
            }
        }

        return true;
    }

    public DataSyncStagedPull? Take(int linkId)
    {
        lock (_lock)
        {
            return _slots.Remove(linkId, out var slot) ? slot.Pull : null;
        }
    }

    public DataSyncStagedPull? Peek(int linkId)
    {
        lock (_lock)
        {
            return _slots.TryGetValue(linkId, out var slot) ? slot.Pull : null;
        }
    }

    /// <summary>Links with a staged pull, oldest first.</summary>
    public IReadOnlyList<int> LinksWaiting()
    {
        lock (_lock)
        {
            return _slots.OrderBy(s => s.Value.Order).Select(s => s.Key).ToList();
        }
    }

    /// <summary>The total size of the staged pulls.</summary>
    public long TotalBytes
    {
        get
        {
            lock (_lock)
            {
                return _slots.Values.Sum(s => s.Bytes);
            }
        }
    }

    /// <summary>
    /// A size for a pull staged without its page bytes: the records' content as JSON text, plus a fixed allowance per
    /// record for its envelope.
    /// </summary>
    public static long EstimateBytes(DataSyncStagedPull pull)
    {
        long bytes = 0;
        foreach (var kind in pull.Kinds)
        {
            foreach (var entity in kind.Entities)
            {
                bytes += 256 + (entity.Record.Content?.ToJsonString().Length ?? 0);
            }
        }

        return bytes;
    }
}

public static class DataSyncStagedPullStoreExtensions
{
    /// <summary>
    /// Stages a pull with the size it cost to read when the store is <see cref="DataSyncStagedPullStore"/>, else with
    /// the interface's own estimate. False when the pull is larger than one link may stage.
    /// </summary>
    public static bool PutSized(this IDataSyncStagedPullStore store, int linkId, DataSyncStagedPull pull, long bytes)
    {
        if (store is DataSyncStagedPullStore sized) return sized.Put(linkId, pull, bytes);
        store.Put(linkId, pull);
        return true;
    }
}
