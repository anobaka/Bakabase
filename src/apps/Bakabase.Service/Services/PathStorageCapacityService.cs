using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Service.Models.View;

namespace Bakabase.Service.Services;

/// <summary>
/// Read-only capacity queries with a response deadline. Native filesystem calls are not
/// cancellable on every OS: timed-out calls retain their bounded slot until they really exit.
/// Repeated requests share that call instead of accumulating blocked NAS threads.
/// </summary>
public sealed class PathStorageCapacityService
{
    private readonly IUserStoragePolicy _storage;
    private readonly Func<string, StorageVolumeCapacity> _measure;
    private readonly TimeSpan _deadline, _cacheLifetime;
    private readonly int _maximumQueries;
    private readonly object _gate = new();
    private readonly Dictionary<string, Query> _queries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private int _active;

    public PathStorageCapacityService(IUserStoragePolicy storage)
        : this(storage, StorageVolumes.Measure, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 4) { }

    internal PathStorageCapacityService(IUserStoragePolicy storage, Func<string, StorageVolumeCapacity> measure,
        TimeSpan deadline, TimeSpan cacheLifetime, int maximumQueries)
    {
        if (deadline <= TimeSpan.Zero || cacheLifetime < TimeSpan.Zero || maximumQueries < 1)
            throw new ArgumentOutOfRangeException(nameof(deadline));
        _storage = storage; _measure = measure; _deadline = deadline; _cacheLifetime = cacheLifetime; _maximumQueries = maximumQueries;
    }

    public async Task<StorageCapacityViewModel> GetAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string normalized;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length > 32768 || !System.IO.Path.IsPathFullyQualified(path))
                return new(path ?? "", "unknown", ErrorCode: "STORAGE_CAPACITY_PATH_INVALID");
            normalized = System.IO.Path.GetFullPath(path);
        }
        catch (ArgumentException) { return new(path, "unknown", ErrorCode: "STORAGE_CAPACITY_PATH_INVALID"); }
        Task<StorageCapacityViewModel> work;
        lock (_gate)
        {
            var now = DateTime.UtcNow;
            foreach (var key in _queries.Where(pair => pair.Value.CompletedAt is { } completed && now - completed >= _cacheLifetime)
                         .Select(pair => pair.Key).ToArray()) _queries.Remove(key);
            if (_queries.TryGetValue(normalized, out var existing)) work = existing.Work;
            else
            {
                if (_active >= _maximumQueries) return new(normalized, "unknown", ErrorCode: "STORAGE_CAPACITY_BUSY");
                // Bound short-lived result caching too, even if a caller submits many distinct paths.
                if (_queries.Count >= 128)
                    foreach (var key in _queries.Where(pair => pair.Value.CompletedAt != null)
                                 .OrderBy(pair => pair.Value.CompletedAt).Take(_queries.Count - 127).Select(pair => pair.Key).ToArray())
                        _queries.Remove(key);
                _active++;
                var query = new Query();
                work = query.Work = Task.Factory.StartNew(() => Read(normalized), CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                _queries[normalized] = query;
                _ = work.ContinueWith(_ =>
                {
                    lock (_gate) { _active--; query.CompletedAt = DateTime.UtcNow; }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        try { return await work.WaitAsync(_deadline, ct); }
        catch (TimeoutException) { return new(normalized, "unknown", ErrorCode: "STORAGE_CAPACITY_TIMED_OUT"); }
    }

    private StorageCapacityViewModel Read(string path)
    {
        try
        {
            // This guard may resolve mount/symlink metadata, so it belongs inside the same
            // bounded read as capacity measurement. A rejected path is never measured.
            _storage.EnsurePathAllowed(path);
            var capacity = _measure(path);
            if (capacity.AvailableBytes < 0 || capacity.TotalBytes <= 0 || capacity.AvailableBytes > capacity.TotalBytes)
                return new(path, "unknown", ErrorCode: "STORAGE_CAPACITY_UNKNOWN");
            return new(path, "available", capacity.VolumePath, capacity.AvailableBytes, capacity.TotalBytes);
        }
        catch (UserStoragePathException) { return new(path, "unavailable", ErrorCode: "STORAGE_CAPACITY_PATH_NOT_ALLOWED"); }
        catch (UnauthorizedAccessException) { return new(path, "unavailable", ErrorCode: "STORAGE_CAPACITY_ACCESS_DENIED"); }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        { return new(path, "unavailable", ErrorCode: "STORAGE_CAPACITY_UNAVAILABLE"); }
    }

    private sealed class Query
    {
        public Task<StorageCapacityViewModel> Work { get; set; } = null!;
        public DateTime? CompletedAt { get; set; }
    }
}
