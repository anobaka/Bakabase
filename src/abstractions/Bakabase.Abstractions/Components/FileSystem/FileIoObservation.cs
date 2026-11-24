using System;
using System.Collections.Generic;
using System.Threading;

namespace Bakabase.Abstractions.Components.FileSystem;

public sealed record FileIoObservationSample(string OwnerKind, string OwnerId, long ReadBytes, long WrittenBytes,
    bool ReadObserved, bool WriteObserved, bool ExternalProcessObserved = false);

public interface IFileIoObservation : IDisposable
{
    void Add(long readBytes, long writtenBytes);
}

public interface IFileIoObservationProvider
{
    IFileIoObservation Begin(StorageWriteContext owner, bool observeRead, bool observeWrite, bool externalProcess = false);
    /// <summary>Collects and resets the current interval. Only one sampler consumes a provider.</summary>
    IReadOnlyList<FileIoObservationSample> Drain();
}

/// <summary>Shares counters within each owner; completed owners leave after the final sample.</summary>
public sealed class FileIoObservationProvider : IFileIoObservationProvider
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Kind, string Id), Counters> _owners = new();

    public IFileIoObservation Begin(StorageWriteContext owner, bool observeRead, bool observeWrite, bool externalProcess = false)
    {
        var key = (owner.OwnerKind, owner.OwnerId);
        lock (_gate)
        {
            if (!_owners.TryGetValue(key, out var counters)) _owners.Add(key, counters = new());
            return counters.Open(observeRead, observeWrite, externalProcess);
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
        private int _readers;
        private int _writers;
        private int _externalProcesses;
        private bool _observedRead;
        private bool _observedWrite;
        private bool _observedExternal;

        public IFileIoObservation Open(bool read, bool write, bool external)
        {
            lock (_gate)
            {
                if (read) { _readers++; _observedRead = true; }
                if (write) { _writers++; _observedWrite = true; }
                if (external) { _externalProcesses++; _observedExternal = true; }
                return new Observation(this, read, write, external);
            }
        }

        public (FileIoObservationSample? Sample, bool Inactive) Drain((string Kind, string Id) owner)
        {
            lock (_gate)
            {
                var observedRead = _readers > 0 || _observedRead;
                var observedWrite = _writers > 0 || _observedWrite;
                var sample = observedRead || observedWrite
                    ? new FileIoObservationSample(owner.Kind, owner.Id, _read, _written, observedRead, observedWrite,
                        _externalProcesses > 0 || _observedExternal)
                    : null;
                _read = 0;
                _written = 0;
                _observedRead = _observedWrite = _observedExternal = false;
                return (sample, _readers == 0 && _writers == 0 && _externalProcesses == 0);
            }
        }

        private sealed class Observation(Counters counters, bool read, bool write, bool external) : IFileIoObservation
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

            public void Dispose()
            {
                lock (counters._gate)
                {
                    if (_closed) return;
                    _closed = true;
                    if (read) { counters._readers--; counters._observedRead = true; }
                    if (write) { counters._writers--; counters._observedWrite = true; }
                    if (external) { counters._externalProcesses--; counters._observedExternal = true; }
                }
            }
        }
    }
}

/// <summary>Flows ownership into instrumented file operations without changing persisted plans.</summary>
public static class FileIoObservationScope
{
    private static readonly AsyncLocal<Context?> Current = new();

    public static IDisposable? Enter(IFileIoObservationProvider? provider, StorageWriteContext? owner)
    {
        var previous = Current.Value;
        if (owner is null || provider is null && previous is null) return null;
        provider ??= previous!.Provider;
        if (previous is not null && ReferenceEquals(previous.Provider, provider) && previous.Owner == owner) return null;
        Current.Value = new(provider, owner);
        return new Scope(previous);
    }

    public static IFileIoObservation? Begin(bool read, bool write, bool externalProcess = false)
    {
        var context = Current.Value;
        return context is not null && (read || write)
            ? context.Provider.Begin(context.Owner, read, write, externalProcess)
            : null;
    }

    internal static IDisposable? EnterIfUnowned(IFileIoObservationProvider? provider, StorageWriteContext? owner) =>
        Current.Value is null ? Enter(provider, owner) : null;

    public static bool Active => Current.Value is not null;

    private sealed record Context(IFileIoObservationProvider Provider, StorageWriteContext Owner);

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
