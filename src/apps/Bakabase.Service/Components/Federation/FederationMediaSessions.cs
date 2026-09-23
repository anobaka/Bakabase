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

public sealed record AvailableFederationAsset(AssetRef Ref, FederatedAsset Asset, PeerSessionSnapshot? Peer)
{
    /// <summary>Whether <paramref name="other"/> is the same file of the same resource under a newer asset lease.</summary>
    public bool IsSameFileAs(AvailableFederationAsset other) =>
        Ref.ResourceRef == other.Ref.ResourceRef && Asset.Kind == other.Asset.Kind &&
        Asset.FileName == other.Asset.FileName && Asset.SourceRootId == other.Asset.SourceRootId &&
        Asset.RelativePath == other.Asset.RelativePath;
}

/// <summary>
/// A local playback ticket outlives the source's short asset lease: players pause, seek and
/// reconnect long after playback starts. <see cref="Source"/> is renewed through a fresh,
/// authenticated resolve; every read still revalidates the current grant with the source.
/// </summary>
public sealed class FederationMediaTicket(string id, AvailableFederationAsset source, string? mappedPath,
    DateTimeOffset created)
{
    public string Id { get; } = id;
    public string? MappedPath { get; } = mappedPath;
    public DateTimeOffset Created { get; } = created;
    public AvailableFederationAsset Source { get; internal set; } = source;
    public DateTimeOffset LastUsed { get; internal set; } = created;
}

/// <summary>All entries were obtained from an authenticated detail response, never an arbitrary user URL.</summary>
public sealed class FederationMediaSessions(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan TicketIdleLifetime = TimeSpan.FromHours(2);
    public static readonly TimeSpan TicketMaxLifetime = TimeSpan.FromHours(24);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
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
                var now = _time.GetUtcNow();
                if (mediaType == null || mediaType.Value.Kind != asset.Kind ||
                    asset.ExpiresAt <= now || asset.ExpiresAt > now.AddMinutes(11))
                    throw new FederationQueryException("InvalidPeerResponse", 502);
                var key = new AssetRef(detail.Ref, asset.AssetId);
                var value = new AvailableFederationAsset(key, asset with { ContentType = mediaType.Value.ContentType }, peer);
                if (_assets.Remove(key, out var previous)) _assetBytes -= Estimate(previous);
                // Tickets hold their own source, so the oldest remembered assets can make room;
                // reopening a resource remembers them again.
                if (_assets.Count >= Limit || _assetBytes + Estimate(value) + _ticketBytes > ByteLimit)
                    foreach (var oldest in _assets.OrderBy(x => x.Value.Asset.ExpiresAt).Select(x => x.Key).ToArray())
                    {
                        if (_assets.Count < Limit / 8 * 7 && _assetBytes + Estimate(value) + _ticketBytes <= ByteLimit / 8 * 7)
                            break;
                        _assetBytes -= Estimate(_assets[oldest]);
                        _assets.Remove(oldest);
                    }
                if (_assetBytes + Estimate(value) + _ticketBytes > ByteLimit)
                    throw new FederationQueryException("Busy", 503, "The local media budget is full.", true);
                _assets[key] = value;
                _assetBytes += Estimate(value);
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
            var ticket = new FederationMediaTicket(id, source, mappedPath, _time.GetUtcNow());
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
            if (!_tickets.TryGetValue(id, out var ticket))
                throw new FederationQueryException("AssetExpired", 410, "This media session expired.");
            ticket.LastUsed = _time.GetUtcNow();
            return ticket;
        }
    }

    /// <summary>Points a live ticket at a newer lease for the same file; anything else is refused.</summary>
    public void Renew(FederationMediaTicket ticket, AvailableFederationAsset renewed)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(ticket.Id, out var current) || !ReferenceEquals(current, ticket))
                throw new FederationQueryException("AssetExpired", 410, "This media session expired.");
            if (!ticket.Source.IsSameFileAs(renewed))
                throw new FederationQueryException("AssetGone", 404, "The source no longer exposes this media file.");
            _ticketBytes += Estimate(renewed) - Estimate(ticket.Source);
            ticket.Source = renewed;
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
        var now = _time.GetUtcNow();
        foreach (var key in _assets.Where(x => x.Value.Asset.ExpiresAt <= now).Select(x => x.Key).ToArray())
        {
            _assetBytes -= Estimate(_assets[key]);
            _assets.Remove(key);
        }
        foreach (var key in _tickets.Where(x => now - x.Value.LastUsed >= TicketIdleLifetime ||
                     now - x.Value.Created >= TicketMaxLifetime).Select(x => x.Key).ToArray())
        {
            _ticketBytes -= 256 + Estimate(_tickets[key].Source);
            _tickets.Remove(key);
        }
    }

    private static long Estimate(AvailableFederationAsset asset) => 1024L + 2L *
        (asset.Ref.ResourceRef.NodeId.Length + asset.Ref.ResourceRef.LibraryEpoch.Length + asset.Ref.AssetId.Length +
         asset.Asset.FileName.Length + (asset.Asset.RelativePath?.Length ?? 0) + (asset.Asset.SourceRootId?.Length ?? 0));
}
