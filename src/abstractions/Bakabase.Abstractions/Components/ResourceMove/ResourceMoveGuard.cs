using Bakabase.Abstractions.Extensions;

namespace Bakabase.Abstractions.Components.ResourceMove;

/// <summary>
/// In-memory registry of the resources and path subtrees reserved by in-flight move batches.
/// Deliberately not persisted: when the process dies the executing task dies with it, the
/// records table (Interrupted rows) becomes the durable trace, and an empty registry after a
/// restart is therefore correct — no stale-lock cleanup is ever needed.
/// Path overlap is judged on whole segments (<see cref="StringExtensions.IsPathEqualOrUnder"/>),
/// in both directions: reserving /a blocks a batch touching /a/b, and vice versa.
/// </summary>
public class ResourceMoveGuard
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Reservation> _reservations = new();

    private record Reservation(HashSet<int> ResourceIds, List<string> Paths);

    /// <summary>
    /// Reserve the given resource ids and path subtrees for one batch. Fails when any path
    /// overlaps a path already reserved by another batch; <paramref name="conflictPath"/> then
    /// carries the already-reserved path that collided.
    /// </summary>
    public bool TryReserve(string batchId, IEnumerable<int> resourceIds, IEnumerable<string?> paths,
        out string? conflictPath)
    {
        var newPaths = paths.Select(p => p.StandardizePath()).OfType<string>().Distinct().ToList();
        lock (_lock)
        {
            foreach (var (existingBatchId, reservation) in _reservations)
            {
                if (existingBatchId == batchId)
                {
                    continue;
                }

                foreach (var newPath in newPaths)
                {
                    var overlapped = reservation.Paths.FirstOrDefault(existing =>
                        newPath.IsPathEqualOrUnder(existing) || existing.IsPathEqualOrUnder(newPath));
                    if (overlapped != null)
                    {
                        conflictPath = overlapped;
                        return false;
                    }
                }
            }

            _reservations[batchId] = new Reservation(resourceIds.ToHashSet(), newPaths);
        }

        conflictPath = null;
        return true;
    }

    public void Release(string batchId)
    {
        lock (_lock)
        {
            _reservations.Remove(batchId);
        }
    }

    public bool IsResourceLocked(int resourceId)
    {
        lock (_lock)
        {
            return _reservations.Values.Any(r => r.ResourceIds.Contains(resourceId));
        }
    }

    /// <summary>The first of <paramref name="resourceIds"/> reserved by any batch, or null.</summary>
    public int? FirstLockedResourceId(IEnumerable<int> resourceIds)
    {
        lock (_lock)
        {
            foreach (var id in resourceIds)
            {
                if (_reservations.Values.Any(r => r.ResourceIds.Contains(id)))
                {
                    return id;
                }
            }
        }

        return null;
    }

    public HashSet<int> GetLockedResourceIds()
    {
        lock (_lock)
        {
            return _reservations.Values.SelectMany(r => r.ResourceIds).ToHashSet();
        }
    }
}
