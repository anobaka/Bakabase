namespace Bakabase.Modules.AI.Components.Cache;

/// <summary>Serializes identical cache misses across dependency-injection scopes.</summary>
public sealed class LlmCacheRequestCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal int PendingKeyCount
    {
        get { lock (_sync) return _entries.Count; }
    }

    internal async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken ct)
    {
        Entry entry;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }
            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(ct).ConfigureAwait(false);
            return new Lease(this, key, entry);
        }
        catch
        {
            RemoveUser(key, entry);
            throw;
        }
    }

    private void RemoveUser(string key, Entry entry)
    {
        lock (_sync)
        {
            if (--entry.Users != 0) return;
            _entries.Remove(key);
            entry.Gate.Dispose();
        }
    }

    private sealed class Entry
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal int Users;
    }

    private sealed class Lease(LlmCacheRequestCoordinator owner, string key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            entry.Gate.Release();
            owner.RemoveUser(key, entry);
        }
    }
}
