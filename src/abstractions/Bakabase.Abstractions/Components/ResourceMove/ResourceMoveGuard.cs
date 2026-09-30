using Bakabase.Abstractions.Extensions;

namespace Bakabase.Abstractions.Components.ResourceMove;

/// <summary>
/// In-memory registry of the resources and path subtrees reserved by in-flight move batches.
/// Rebuilt from durable move records before startup scheduling. Waiting and recovery jobs
/// retain their reservations even after their executing task releases its scheduler slot.
/// Path overlap is judged on whole segments (<see cref="StringExtensions.IsPathEqualOrUnder"/>),
/// in both directions: reserving /a blocks a batch touching /a/b, and vice versa.
/// </summary>
public class ResourceMoveGuard
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Reservation> _reservations = new();
    private readonly HashSet<string> _retained = [];

    private record Reservation(HashSet<int> ResourceIds, List<string> Paths);

    /// <summary>
    /// Reserve the given resource ids and path subtrees for one batch. Fails when any path
    /// overlaps a path already reserved by another batch; <paramref name="conflictPath"/> then
    /// carries the already-reserved path that collided. A batch id holds at most one live
    /// reservation: a duplicate is rejected rather than overwritten, so a racing Retry can
    /// never swap out — and on its own failure release — the reservation of a batch that is
    /// still executing.
    /// </summary>
    public bool TryReserve(string batchId, IEnumerable<int> resourceIds, IEnumerable<string?> paths,
        out string? conflictPath)
    {
        var newPaths = paths.Select(p => p.StandardizePath()).OfType<string>().Distinct().ToList();
        lock (_lock)
        {
            if (_reservations.TryGetValue(batchId, out var own))
            {
                conflictPath = own.Paths.FirstOrDefault() ?? newPaths.FirstOrDefault() ?? string.Empty;
                return false;
            }

            foreach (var (_, reservation) in _reservations)
            {
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
            _retained.Remove(batchId);
        }
    }

    /// <summary>
    /// A recovered batch may initially reserve only its unfinished subset. Retrying additional
    /// records must atomically extend that reservation, rather than assuming the batch owns
    /// every path just because it owns some paths. A rejected extension leaves the old lock intact.
    /// </summary>
    public bool TryExtendReservation(string batchId, IEnumerable<int> resourceIds,
        IEnumerable<string?> paths, out string? conflictPath)
    {
        var requested = paths.Select(p => p.StandardizePath()).OfType<string>().Distinct().ToList();
        lock (_lock)
        {
            foreach (var (owner, reservation) in _reservations)
            {
                if (owner == batchId) continue;
                foreach (var path in requested)
                {
                    conflictPath = reservation.Paths.FirstOrDefault(existing =>
                        path.IsPathEqualOrUnder(existing) || existing.IsPathEqualOrUnder(path));
                    if (conflictPath != null) return false;
                }
            }
            var previous = _reservations.GetValueOrDefault(batchId);
            _reservations[batchId] = new Reservation(
                resourceIds.Concat(previous?.ResourceIds ?? []).ToHashSet(),
                requested.Concat(previous?.Paths ?? []).Distinct().ToList());
            conflictPath = null;
            return true;
        }
    }

    public bool HoldsReservation(string batchId)
    {
        lock (_lock) return _reservations.ContainsKey(batchId);
    }

    /// <summary>Waiting/recovery reservations outlive an executor. Legacy sync/mover tasks
    /// must not run until they understand these path exclusions.</summary>
    public bool HasRetainedReservations
    {
        get { lock (_lock) return _retained.Count != 0; }
    }

    public void Retain(string batchId)
    {
        lock (_lock) if (_reservations.ContainsKey(batchId)) _retained.Add(batchId);
    }

    public void Resume(string batchId)
    {
        lock (_lock) _retained.Remove(batchId);
    }

    public int[] GetReservedResourceIds(string batchId)
    {
        lock (_lock) return _reservations.TryGetValue(batchId, out var r) ? r.ResourceIds.ToArray() : [];
    }

    public string[] GetReservedPaths(string batchId)
    {
        lock (_lock) return _reservations.TryGetValue(batchId, out var r) ? r.Paths.ToArray() : [];
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
