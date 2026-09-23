using System.Security.Cryptography;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Modules.Federation.Media;

/// <summary>Bounded, process-local capabilities. Every redemption still needs live grant and resource checks.</summary>
public sealed class AssetLeaseStore(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _lock = new();
    private readonly Dictionary<string, AssetLease> _leases = new(StringComparer.Ordinal);
    private long _storedBytes;
    public const int MaximumLeases = 8192;
    public const int MaximumPathChars = 4096;
    public const long MaximumStoredBytes = 16L * 1024 * 1024;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public AssetLease Issue(ResourceRef resourceRef, string grantId, long grantVersion, string path,
        string contentType, string kind)
    {
        if (resourceRef == null || !NodeRequestSignature.IsIdentifier(resourceRef.NodeId) ||
            !NodeRequestSignature.IsIdentifier(resourceRef.LibraryEpoch) || resourceRef.ResourceId <= 0 ||
            !NodeRequestSignature.IsIdentifier(grantId) || grantVersion < 0)
            throw new FederationQueryException("InvalidAssetIdentity", 422, "The asset needs a complete resource and grant identity.");
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathChars || path.Contains('\0') ||
            contentType is not { Length: > 0 and <= 128 } || kind is not { Length: > 0 and <= 16 })
            throw new FederationQueryException("AssetMetadataTooLarge", 503, "The asset exceeds the media metadata budget.");
        var mediaType = MediaContentTypes.Get(path);
        if (mediaType == null || mediaType.Value.Kind != kind || mediaType.Value.ContentType != contentType)
            throw new FederationQueryException("UnsupportedAsset", 422, "The asset's media type is not supported.");
        lock (_lock)
        {
            Prune();
            // Reopening details should not exhaust the quota with identical capabilities.
            var current = _leases.Values.FirstOrDefault(x => x.ResourceRef == resourceRef &&
                x.GrantId == grantId && x.GrantVersion == grantVersion && x.Path == path &&
                x.ExpiresAt - _time.GetUtcNow() > TimeSpan.FromMinutes(1));
            if (current != null) return current;
            var size = RetainedBytes(resourceRef, grantId, path, contentType, kind);
            if (_leases.Count >= MaximumLeases || _storedBytes + size > MaximumStoredBytes) Evict(size);
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var lease = new AssetLease(id, resourceRef, grantId, grantVersion, path, contentType, kind,
                _time.GetUtcNow().Add(Lifetime));
            _leases.Add(id, lease);
            _storedBytes += size;
            return lease;
        }
    }

    public AssetLease Get(string id, string grantId)
    {
        lock (_lock)
        {
            Prune();
            if (!NodeRequestSignature.IsIdentifier(id) || !NodeRequestSignature.IsIdentifier(grantId) ||
                !_leases.TryGetValue(id, out var lease) || lease.GrantId != grantId)
                throw new FederationQueryException("AssetExpired", 410, "Reopen the resource to renew its media session.");
            return lease;
        }
    }

    public void Revoke(string grantId)
    {
        lock (_lock)
            foreach (var id in _leases.Where(x => x.Value.GrantId == grantId).Select(x => x.Key).ToArray())
                Remove(id);
    }

    /// <summary>
    /// A full table must not lock every reader out until leases expire, so the leases nearest to
    /// expiry make room. Their holders renew by resolving again, which re-checks the grant.
    /// Evicts down to 7/8 of both limits so a burst of issues does not sort on every call.
    /// </summary>
    private void Evict(long incomingBytes)
    {
        foreach (var lease in _leases.Values.OrderBy(x => x.ExpiresAt).ToArray())
        {
            if (_leases.Count < MaximumLeases / 8 * 7 && _storedBytes + incomingBytes <= MaximumStoredBytes / 8 * 7) return;
            Remove(lease.AssetId);
        }
    }

    private void Prune()
    {
        var now = _time.GetUtcNow();
        foreach (var id in _leases.Where(x => x.Value.ExpiresAt <= now).Select(x => x.Key).ToArray())
            Remove(id);
    }

    private void Remove(string id)
    {
        if (_leases.Remove(id, out var lease))
            _storedBytes -= RetainedBytes(lease.ResourceRef, lease.GrantId, lease.Path, lease.ContentType, lease.Kind);
    }

    // Charge retained UTF-16 strings plus dictionary, record and generated token overhead.
    private static long RetainedBytes(ResourceRef reference, string grantId, string path, string contentType,
        string kind) => 512L + 2L * (64 + reference.NodeId.Length + reference.LibraryEpoch.Length +
                                   grantId.Length + path.Length + contentType.Length + kind.Length);
}
