using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.Federation.Security;

namespace Bakabase.Service.Components.Federation;

public sealed record AvailableFederationAsset(AssetRef Ref, FederatedAsset Asset, PeerSessionSnapshot? Peer);
public sealed record FederationMediaTicket(string Id, AvailableFederationAsset Source, string? MappedPath);

/// <summary>All entries were obtained from an authenticated detail response, never an arbitrary user URL.</summary>
public sealed class FederationMediaSessions
{
    private readonly object _gate = new();
    private readonly Dictionary<AssetRef, AvailableFederationAsset> _assets = new();
    private readonly Dictionary<string, FederationMediaTicket> _tickets = new(StringComparer.Ordinal);
    private const int Limit = 8192;
    private const long ByteLimit = 64 * 1024 * 1024;
    private long _assetBytes;
    private long _ticketBytes;

    public void Remember(FederatedResourceDetail detail, PeerSessionSnapshot? peer, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            Prune();
            if (detail.Assets is not { Length: <= 288 } || detail.Ref is null)
                throw new FederationQueryException("InvalidPeerResponse", 502);
            foreach (var asset in detail.Assets)
            {
                if (asset is null || asset.FileName is not { Length: > 0 and <= 1024 } ||
                    asset.AssetId is not { Length: >= 16 and <= 128 } || asset.AssetId.Any(c => !char.IsAsciiLetterOrDigit(c)) ||
                    asset.SourceRootId != null && !NodeRequestSignature.IsIdentifier(asset.SourceRootId) ||
                    asset.RelativePath is { Length: > 4096 } ||
                    (asset.SourceRootId == null) != (asset.RelativePath == null))
                    throw new FederationQueryException("InvalidPeerResponse", 502);
                var mediaType = MediaContentTypes.Get(asset.FileName);
                if (mediaType == null || mediaType.Value.Kind != asset.Kind ||
                    asset.ExpiresAt <= DateTimeOffset.UtcNow || asset.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(11))
                    throw new FederationQueryException("InvalidPeerResponse", 502);
                var key = new AssetRef(detail.Ref, asset.AssetId);
                var value = new AvailableFederationAsset(key, asset with { ContentType = mediaType.Value.ContentType }, peer);
                var previousBytes = _assets.TryGetValue(key, out var previous) ? Estimate(previous) : 0;
                var nextBytes = _assetBytes - previousBytes + Estimate(value);
                if (_assets.Count >= Limit && previous == null || nextBytes + _ticketBytes > ByteLimit)
                    throw new FederationQueryException("Busy", 503, "The local media budget is full.", true);
                _assets[key] = value;
                _assetBytes = nextBytes;
            }
        }
    }

    public AvailableFederationAsset GetAsset(AssetRef reference)
    {
        lock (_gate)
        {
            Prune();
            return _assets.TryGetValue(reference, out var asset) ? asset :
                throw new FederationQueryException("AssetExpired", 410, "Reopen the resource to refresh its media session.");
        }
    }

    public FederationMediaTicket Issue(AvailableFederationAsset source, string? mappedPath, CancellationToken ct = default)
    {
        lock (_gate)
        {
            ct.ThrowIfCancellationRequested();
            Prune();
            var bytes = 256 + Estimate(source);
            if (_tickets.Count >= Limit || _assetBytes + _ticketBytes + bytes > ByteLimit)
                throw new FederationQueryException("Busy", 503, retryable: true);
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var ticket = new FederationMediaTicket(id, source, mappedPath);
            _tickets.Add(id, ticket);
            _ticketBytes += bytes;
            return ticket;
        }
    }

    public FederationMediaTicket GetTicket(string id)
    {
        lock (_gate)
        {
            Prune();
            return _tickets.TryGetValue(id, out var ticket) ? ticket :
                throw new FederationQueryException("AssetExpired", 410, "This media session expired.");
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _assets.Clear();
            _tickets.Clear();
            _assetBytes = _ticketBytes = 0;
        }
    }

    private void Prune()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _assets.Where(x => x.Value.Asset.ExpiresAt <= now).Select(x => x.Key).ToArray())
        {
            _assetBytes -= Estimate(_assets[key]);
            _assets.Remove(key);
        }
        foreach (var key in _tickets.Where(x => x.Value.Source.Asset.ExpiresAt <= now).Select(x => x.Key).ToArray())
        {
            _ticketBytes -= 256 + Estimate(_tickets[key].Source);
            _tickets.Remove(key);
        }
    }

    private static long Estimate(AvailableFederationAsset asset) => 1024L + 2L *
        (asset.Ref.ResourceRef.NodeId.Length + asset.Ref.ResourceRef.LibraryEpoch.Length + asset.Ref.AssetId.Length +
         asset.Asset.FileName.Length + (asset.Asset.RelativePath?.Length ?? 0) + (asset.Asset.SourceRootId?.Length ?? 0));
}
