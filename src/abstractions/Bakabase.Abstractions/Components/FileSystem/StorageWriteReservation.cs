using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>Upper bound for the writes to one destination during the entire writer invocation.</summary>
public sealed record StorageWriteBudget(string Destination, long MaximumAdditionalBytes);

/// <summary>
/// Coordinates known write budgets within one host. Other processes and writers without budgets
/// remain subject only to live monitoring; this is not a filesystem quota or an OS reservation.
/// </summary>
public interface IStorageWriteReservationGuard
{
    Task<T> RunReservedAsync<T>(IReadOnlyList<StorageWriteBudget> budgets, StorageWriteContext? context,
        Func<CancellationToken, Task<T>> writer, Func<Task>? waiting = null, CancellationToken ct = default);
}

public sealed partial class StorageWriteGuard
{
    // Covers ordinary directory/checkpoint overhead without changing the user's configured floor.
    public const long ReservationHeadroomBytes = 64L * 1024 * 1024;
    private readonly object _reservationGate = new();
    private readonly HashSet<Reservation> _reservations = [];
    private static readonly TimeSpan ReservationHeartbeatInterval = TimeSpan.FromSeconds(30);

    public async Task<T> RunReservedAsync<T>(IReadOnlyList<StorageWriteBudget> budgets, StorageWriteContext? context,
        Func<CancellationToken, Task<T>> writer, Func<Task>? waiting = null, CancellationToken ct = default)
    {
        context ??= StorageWriteScope.Current;
        if (budgets.Count == 0) throw new ArgumentException("At least one write destination is required.", nameof(budgets));
        var allocations = budgets.Select(b => b.MaximumAdditionalBytes < 0
            ? throw new ArgumentOutOfRangeException(nameof(budgets))
            : new StorageWriteBudget(Path.GetFullPath(b.Destination), b.MaximumAdditionalBytes)).ToArray();
        using var reservation = await ReserveAsync(allocations, context, waiting, ct);
        // Keep the full budget until the writer and its cancellation cleanup have returned. In
        // particular, progress and file length do not prove that sparse or resumed bytes are allocated.
        return await RunCoreAsync(allocations.Select(b => b.Destination).ToArray(), context, writer,
            reservation.PinnedVolumes, ct);
    }

    private async Task<Reservation> ReserveAsync(IReadOnlyList<StorageWriteBudget> allocations,
        StorageWriteContext? context, Func<Task>? waiting, CancellationToken ct)
    {
        var pins = new Dictionary<string, string>(Paths);
        var nextHeartbeat = DateTime.MinValue;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var minimum = await _policy.GetMinimumFreeBytesAsync(context, ct);
            if (minimum < 0) throw new ArgumentOutOfRangeException(nameof(minimum));
            lock (_reservationGate)
            {
                ct.ThrowIfCancellationRequested();
                var volumes = new Dictionary<string, VolumeBudget>(Paths);
                foreach (var allocation in allocations)
                {
                    var (volume, available) = MeasurePinned(allocation.Destination, pins);
                    if (!volumes.TryGetValue(volume, out var budget))
                        budget = new(available, ReservationHeadroomBytes);
                    try
                    {
                        volumes[volume] = new(Math.Min(budget.Available, available),
                            checked(budget.Bytes + allocation.MaximumAdditionalBytes));
                    }
                    catch (OverflowException) { throw new LocalStorageException(ReserveLowCode); }
                }

                var admitted = true;
                foreach (var (volume, budget) in volumes)
                {
                    // This payload cannot fit even without any competing reservation. Preserve the
                    // usual recoverable storage failure instead of waiting forever for another lease.
                    if (budget.Available < minimum || budget.Available - minimum < budget.Bytes)
                        throw new LocalStorageException(ReserveLowCode);
                    long reserved = 0, effectiveMinimum = minimum;
                    foreach (var active in _reservations)
                    {
                        if (!active.Volumes.TryGetValue(volume, out var held)) continue;
                        reserved = checked(reserved + held.Bytes);
                        effectiveMinimum = Math.Max(effectiveMinimum, active.Minimum);
                    }
                    if (budget.Available < effectiveMinimum ||
                        budget.Available - effectiveMinimum < budget.Bytes ||
                        budget.Available - effectiveMinimum - budget.Bytes < reserved)
                        admitted = false;
                }
                if (admitted)
                {
                    // Acquire all volumes together: a waiter never holds one volume while trying
                    // to obtain another, and aliases share the volume returned by the measurement.
                    var reservation = new Reservation(this, volumes, pins, minimum);
                    _reservations.Add(reservation);
                    return reservation;
                }
            }
            // No account/provider lock or capacity lock is held while waiting. Report immediately
            // and periodically so the download queue does not mistake admission for a stalled writer.
            if (waiting != null && DateTime.UtcNow >= nextHeartbeat)
            {
                await waiting();
                nextHeartbeat = DateTime.UtcNow + ReservationHeartbeatInterval;
            }
            await Task.Delay(_interval, ct);
        }
    }

    private (string Volume, long Available) MeasurePinned(string path, Dictionary<string, string>? pins)
    {
        try
        {
            var measured = _measure(path);
            if (pins != null)
            {
                lock (pins)
                {
                    if (pins.TryGetValue(path, out var original) && !Paths.Equals(original, measured.Volume))
                        throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE");
                    pins[path] = measured.Volume;
                }
            }
            return measured;
        }
        catch (LocalStorageException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE", error); }
    }

    private sealed record VolumeBudget(long Available, long Bytes);

    private sealed class Reservation(StorageWriteGuard owner, Dictionary<string, VolumeBudget> volumes,
        Dictionary<string, string> pinnedVolumes, long minimum) : IDisposable
    {
        public Dictionary<string, VolumeBudget> Volumes { get; } = volumes;
        public Dictionary<string, string> PinnedVolumes { get; } = pinnedVolumes;
        public long Minimum { get; } = minimum;
        public void Dispose()
        {
            lock (owner._reservationGate) owner._reservations.Remove(this);
        }
    }
}
