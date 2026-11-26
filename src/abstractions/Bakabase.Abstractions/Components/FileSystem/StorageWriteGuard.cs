using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>Runtime ownership and an optional stricter reserve, never a content or operation identity.</summary>
public sealed record StorageWriteContext(string OwnerKind, string OwnerId, long? MinimumFreeBytes = null);

public interface IStorageWritePolicyProvider
{
    Task<long> GetMinimumFreeBytesAsync(StorageWriteContext? context, CancellationToken ct = default);
}

public interface IStorageWriteGuard
{
    Task CheckAsync(string destination, StorageWriteContext? context, CancellationToken ct = default);
    /// <summary>Checks known additional payload bytes against the live reserve. This is admission,
    /// not a reservation against concurrent writers or an estimate of later extraction outputs.</summary>
    Task CheckAllocationAsync(string destination, long additionalBytes, StorageWriteContext? context, CancellationToken ct = default);
    Task<T> RunAsync<T>(IReadOnlyList<string> destinations, StorageWriteContext? context,
        Func<CancellationToken, Task<T>> writer, CancellationToken ct = default);
    Task CopyAsync(Stream source, Stream destination, string destinationPath, StorageWriteContext? context,
        CancellationToken ct = default);
    Task CopyFileAsync(Stream source, Stream destination, string destinationPath, StorageWriteContext? context,
        CancellationToken ct = default) => CopyAsync(source, destination, destinationPath, context, ct);
}

/// <summary>Flow an explicit caller through nested capabilities without putting it in frozen plans.</summary>
public static class StorageWriteScope
{
    private static readonly AsyncLocal<StorageWriteContext?> Value = new();
    public static StorageWriteContext? Current => Value.Value;
    public static IDisposable Enter(StorageWriteContext? context)
    {
        var previous = Value.Value;
        Value.Value = context ?? previous;
        return new Scope(previous);
    }
    private sealed class Scope(StorageWriteContext? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (!_disposed) {Value.Value = previous; _disposed = true;} }
    }
}

/// <summary>
/// Admission plus bounded monitoring of actual write volumes. Cancellation is awaited before
/// returning a reserve error, so a detached writer cannot continue after its caller suspends.
/// </summary>
public sealed partial class StorageWriteGuard : IStorageWriteGuard, IStorageWriteReservationGuard
{
    public const string ReserveLowCode = "LOCAL_STORAGE_RESERVE_LOW";
    private readonly IStorageWritePolicyProvider _policy;
    private readonly Func<string, (StorageVolumeIdentity Volume, long Available)> _measure;
    private readonly TimeSpan _interval;
    private readonly IFileIoObservationProvider? _fileIo;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public StorageWriteGuard(IStorageWritePolicyProvider policy, IFileIoObservationProvider? fileIo = null)
        : this(policy, NativeMeasure, TimeSpan.FromMilliseconds(500), fileIo) { }

    public StorageWriteGuard(IStorageWritePolicyProvider policy,
        Func<string, (string Volume, long Available)> measure, TimeSpan interval, IFileIoObservationProvider? fileIo = null)
        : this(policy, path =>
        {
            var (volume, available) = measure(path);
            return (new StorageVolumeIdentity(volume, volume), available);
        }, interval, fileIo) { }

    internal StorageWriteGuard(IStorageWritePolicyProvider policy,
        Func<string, (StorageVolumeIdentity Volume, long Available)> measure, TimeSpan interval, IFileIoObservationProvider? fileIo = null)
    {
        if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _policy = policy; _measure = measure; _interval = interval; _fileIo = fileIo;
    }

    public Task CheckAsync(string destination, StorageWriteContext? context, CancellationToken ct = default) =>
        CheckCoreAsync(destination, context ?? StorageWriteScope.Current, 0, null, ct);

    public Task CheckAllocationAsync(string destination, long additionalBytes, StorageWriteContext? context, CancellationToken ct = default)
    {
        if (additionalBytes < 0) throw new ArgumentOutOfRangeException(nameof(additionalBytes));
        return CheckCoreAsync(destination, context ?? StorageWriteScope.Current, additionalBytes, null, ct);
    }

    private async Task CheckCoreAsync(string destination, StorageWriteContext? context, long additionalBytes,
        Dictionary<string, StorageVolumeIdentity>? pinnedVolumes, CancellationToken ct, bool requireVolumeCheck = false)
    {
        ct.ThrowIfCancellationRequested();
        var minimum = await _policy.GetMinimumFreeBytesAsync(context, ct);
        if (minimum < 0) throw new ArgumentOutOfRangeException(nameof(minimum));
        if (minimum == 0 && !requireVolumeCheck) return; // Unscoped independent operations keep their existing admission policy.
        try
        {
            var path = Path.GetFullPath(destination);
            var (_, available) = MeasurePinned(path, pinnedVolumes);
            if (available < minimum || available - minimum < additionalBytes)
                throw new LocalStorageException(ReserveLowCode);
        }
        catch (LocalStorageException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { throw new LocalStorageException("LOCAL_STORAGE_UNAVAILABLE", error); }
    }

    public Task<T> RunAsync<T>(IReadOnlyList<string> destinations, StorageWriteContext? context,
        Func<CancellationToken, Task<T>> writer, CancellationToken ct = default) =>
        RunCoreAsync(destinations, context, writer, null, ct);

    private async Task<T> RunCoreAsync<T>(IReadOnlyList<string> destinations, StorageWriteContext? context,
        Func<CancellationToken, Task<T>> writer, Dictionary<string, StorageVolumeIdentity>? pinnedVolumes, CancellationToken ct)
    {
        context ??= StorageWriteScope.Current;
        using var scope = StorageWriteScope.Enter(context);
        using var ioScope = FileIoObservationScope.EnterIfUnowned(_fileIo, context);
        var paths = destinations.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).Distinct(Paths).ToArray();
        var volumes = pinnedVolumes ?? new Dictionary<string, StorageVolumeIdentity>(Paths);
        foreach (var path in paths) await CheckCoreAsync(path, context, 0, volumes, ct, pinnedVolumes != null);
        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Exception? failure = null;
        async Task Monitor()
        {
            using var timer = new PeriodicTimer(_interval);
            try
            {
                while (await timer.WaitForNextTickAsync(watch.Token))
                    foreach (var path in paths) await CheckCoreAsync(path, context, 0, volumes, watch.Token, pinnedVolumes != null);
            }
            catch (OperationCanceledException) when (watch.IsCancellationRequested) { }
            catch (Exception error)
            {
                Interlocked.CompareExchange(ref failure, error, null);
                // A transport's cancellation callback may itself fail. Cancellation has still
                // been requested; await the writer below and retain the original storage cause.
                try { await work.CancelAsync(); }
                catch (Exception) { }
            }
        }
        var monitoring = (context == null && pinnedVolumes == null) || paths.Length == 0 ? Task.CompletedTask : Monitor();
        try
        {
            var result = await writer(work.Token);
            await watch.CancelAsync();
            await monitoring;
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            // A writer may finish before the first timer tick; still observe its final free space.
            foreach (var path in paths) await CheckCoreAsync(path, context, 0, volumes, ct, pinnedVolumes != null);
            return result;
        }
        catch (Exception) when (failure != null && !ct.IsCancellationRequested)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
        finally
        {
            await watch.CancelAsync();
            await monitoring;
        }
    }

    public Task CopyAsync(Stream source, Stream destination, string destinationPath, StorageWriteContext? context,
        CancellationToken ct = default) => CopyCoreAsync(source, destination, destinationPath, context, false, ct);

    public Task CopyFileAsync(Stream source, Stream destination, string destinationPath, StorageWriteContext? context,
        CancellationToken ct = default) => CopyCoreAsync(source, destination, destinationPath, context, true, ct);

    private async Task CopyCoreAsync(Stream source, Stream destination, string destinationPath,
        StorageWriteContext? context, bool readFile, CancellationToken ct)
    {
        context ??= StorageWriteScope.Current;
        await RunAsync([destinationPath], context, async token =>
        {
            using var observed = FileIoObservationScope.Begin(readFile, write: true);
            var buffer = new byte[1024 * 1024];
            var volumes = new Dictionary<string, StorageVolumeIdentity>(Paths);
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                if (readFile) observed?.Add(read, 0);
                await CheckCoreAsync(destinationPath, context, read, volumes, token);
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
                observed?.Add(0, read);
            }
            return true;
        }, ct);
    }

    private static (StorageVolumeIdentity Volume, long Available) NativeMeasure(string path)
    {
        var capacity = StorageVolumes.Measure(path);
        return (StorageVolumes.Identify(capacity), capacity.AvailableBytes);
    }

    internal static (string Volume, long Available)? MeasureWindowsUnc(string path, Func<string, ulong> availableToCaller)
    {
        var capacity = StorageVolumes.MeasureWindowsUnc(path, query => (availableToCaller(query), 0));
        return capacity == null ? null : (capacity.VolumePath, capacity.AvailableBytes);
    }
}
