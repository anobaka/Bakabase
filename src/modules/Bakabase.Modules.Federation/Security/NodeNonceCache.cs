using System.Collections.Concurrent;

namespace Bakabase.Modules.Federation.Security;

/// <summary>A full bucket refuses new requests; it never evicts a live replay guard.</summary>
public sealed class NodeNonceCache(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private sealed class Bucket
    {
        public readonly object Gate = new();
        public readonly Dictionary<string, DateTimeOffset> Nonces = new(StringComparer.Ordinal);
    }

    public bool TryConsume(string grantId, string nonce)
    {
        var now = timeProvider.GetUtcNow();
        var bucket = _buckets.GetOrAdd(grantId, _ => new Bucket());
        lock (bucket.Gate)
        {
            foreach (var old in bucket.Nonces.Where(p => p.Value <= now).Select(p => p.Key).ToArray())
                bucket.Nonces.Remove(old);
            if (bucket.Nonces.ContainsKey(nonce) || bucket.Nonces.Count >= 16384) return false;
            bucket.Nonces.Add(nonce, now + NodeRequestSignature.MaxClockSkew * 2);
            return true;
        }
    }
}
