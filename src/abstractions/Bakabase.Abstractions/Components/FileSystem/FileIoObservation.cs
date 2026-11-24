using System;
using System.Collections.Generic;
using System.Threading;

namespace Bakabase.Abstractions.Components.FileSystem;

public sealed record FileIoObservationSample(string OwnerKind, string OwnerId, long ReadBytes, long WrittenBytes,
    bool ReadObserved, bool WriteObserved, bool ExternalProcessObserved = false,
    long NetworkReceivedBytes = 0, bool NetworkObserved = false, string? NetworkDownloadId = null);

public interface IFileIoObservation : IDisposable
{
    void Add(long readBytes, long writtenBytes);
}

public interface INetworkReceiveObservation : IDisposable
{
    void AddReceivedBytes(long bytes);
}

public interface IFileIoObservationProvider
{
    IFileIoObservation Begin(StorageWriteContext owner, bool observeRead, bool observeWrite, bool externalProcess = false);
    INetworkReceiveObservation BeginNetworkReceive(StorageWriteContext owner, string downloadId);
    /// <summary>Collects and resets the current interval. Only one sampler consumes a provider.</summary>
    IReadOnlyList<FileIoObservationSample> Drain();
}

/// <summary>Shares counters within each owner; completed owners leave after the final sample.</summary>
public sealed class FileIoObservationProvider : IFileIoObservationProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Kind, string Id, string? DownloadId), Counters> _owners = new();

    public IFileIoObservation Begin(StorageWriteContext owner, bool observeRead, bool observeWrite, bool externalProcess = false) =>
        Begin(owner, null, observeRead, observeWrite, externalProcess);

    public INetworkReceiveObservation BeginNetworkReceive(StorageWriteContext owner, string downloadId) =>
        Begin(owner, downloadId, false, false, false);

    private Counters.Observation Begin(StorageWriteContext owner, string? downloadId, bool read, bool write, bool external)
    {
        var key = (owner.OwnerKind, owner.OwnerId, downloadId);
        lock (_gate)
        {
            if (!_owners.TryGetValue(key, out var counters)) _owners.Add(key, counters = new());
            return counters.Open(read, write, external, downloadId != null);
        }
    }

    public IReadOnlyList<FileIoObservationSample> Drain()
    {
        lock (_gate)
        {
            var samples = new List<FileIoObservationSample>(_owners.Count);
            foreach (var (key, counters) in _owners)
            {
                var (sample, inactive) = counters.Drain(key);
                if (sample is not null) samples.Add(sample);
                if (inactive) _owners.Remove(key);
            }
            return samples;
        }
    }

    private sealed class Counters
    {
        private readonly object _gate = new();
        private long _read;
        private long _written;
        private long _received;
        private int _receivers;
        private bool _observedNetwork;
        private int _readers;
        private int _writers;
        private int _externalProcesses;
        private bool _observedRead;
        private bool _observedWrite;
        private bool _observedExternal;

        public Observation Open(bool read, bool write, bool external, bool network)
        {
            lock (_gate)
            {
                if (read) { _readers++; _observedRead = true; }
                if (write) { _writers++; _observedWrite = true; }
                if (external) { _externalProcesses++; _observedExternal = true; }
                if (network) { _receivers++; _observedNetwork = true; }
                return new Observation(this, read, write, external, network);
            }
        }

        public (FileIoObservationSample? Sample, bool Inactive) Drain((string Kind, string Id, string? DownloadId) owner)
        {
            lock (_gate)
            {
                var observedRead = _readers > 0 || _observedRead;
                var observedWrite = _writers > 0 || _observedWrite;
                var observedNetwork = _receivers > 0 || _observedNetwork;
                var sample = observedRead || observedWrite || observedNetwork
                    ? new FileIoObservationSample(owner.Kind, owner.Id, _read, _written, observedRead, observedWrite,
                        _externalProcesses > 0 || _observedExternal, _received, observedNetwork, owner.DownloadId)
                    : null;
                _read = 0;
                _written = 0;
                _received = 0;
                _observedRead = _observedWrite = _observedExternal = _observedNetwork = false;
                return (sample, _readers == 0 && _writers == 0 && _externalProcesses == 0 && _receivers == 0);
            }
        }

        public sealed class Observation(Counters counters, bool read, bool write, bool external, bool network)
            : IFileIoObservation, INetworkReceiveObservation
        {
            private bool _closed;

            public void Add(long readBytes, long writtenBytes)
            {
                lock (counters._gate)
                {
                    if (_closed) return;
                    if (read && readBytes > 0) counters._read += readBytes;
                    if (write && writtenBytes > 0) counters._written += writtenBytes;
                }
            }

            public void AddReceivedBytes(long bytes)
            {
                lock (counters._gate)
                    if (!_closed && network && bytes > 0) counters._received += bytes;
            }

            public void Dispose()
            {
                lock (counters._gate)
                {
                    if (_closed) return;
                    _closed = true;
                    if (read) { counters._readers--; counters._observedRead = true; }
                    if (write) { counters._writers--; counters._observedWrite = true; }
                    if (external) { counters._externalProcesses--; counters._observedExternal = true; }
                    if (network) { counters._receivers--; counters._observedNetwork = true; }
                }
            }
        }
    }
}

/// <summary>Flows ownership into instrumented file operations without changing persisted plans.</summary>
public static class FileIoObservationScope
{
    private static readonly AsyncLocal<Context?> Current = new();

    public static IDisposable? Enter(IFileIoObservationProvider? provider, StorageWriteContext? owner, string? networkDownloadId = null)
    {
        var previous = Current.Value;
        if (owner is null || provider is null && previous is null) return null;
        provider ??= previous!.Provider;
        if (previous is not null && ReferenceEquals(previous.Provider, provider) && previous.Owner == owner)
        {
            networkDownloadId ??= previous.NetworkDownloadId;
            if (networkDownloadId == previous.NetworkDownloadId) return null;
        }
        Current.Value = new(provider, owner, networkDownloadId);
        return new Scope(previous);
    }

    public static IFileIoObservation? Begin(bool read, bool write, bool externalProcess = false)
    {
        var context = Current.Value;
        return context is not null && (read || write)
            ? context.Provider.Begin(context.Owner, read, write, externalProcess)
            : null;
    }

    public static INetworkReceiveObservation? BeginNetworkReceive(string? downloadId)
    {
        var context = Current.Value;
        return context != null && !string.IsNullOrWhiteSpace(downloadId)
            ? context.Provider.BeginNetworkReceive(context.Owner, downloadId) : null;
    }

    internal static IDisposable? EnterIfUnowned(IFileIoObservationProvider? provider, StorageWriteContext? owner) =>
        Current.Value is null ? Enter(provider, owner) : null;

    public static bool Active => Current.Value is not null;
    public static string? NetworkDownloadId => Current.Value?.NetworkDownloadId;

    private sealed record Context(IFileIoObservationProvider Provider, StorageWriteContext Owner, string? NetworkDownloadId);

    private sealed class Scope(Context? previous) : IDisposable
    {
        private bool _restored;
        public void Dispose()
        {
            if (_restored) return;
            Current.Value = previous;
            _restored = true;
        }
    }
}
