namespace Bakabase.Modules.Federation.Peers;

public sealed class NodePairingRateLimiter(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset Since, int Count)> _addresses = new(StringComparer.Ordinal);

    public bool TryTake(string address)
    {
        lock (_gate)
        {
            var now = timeProvider.GetUtcNow();
            foreach (var old in _addresses.Where(p => now - p.Value.Since > TimeSpan.FromMinutes(1))
                         .Select(p => p.Key).ToArray()) _addresses.Remove(old);
            if (!_addresses.TryGetValue(address, out var bucket))
            {
                if (_addresses.Count >= 128) return false;
                bucket = (now, 0);
            }
            if (bucket.Count >= 10) return false;
            _addresses[address] = (bucket.Since, bucket.Count + 1);
            return true;
        }
    }
}
