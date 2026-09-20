using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Player.Abstractions.Components;
using Bakabase.Modules.Player.Components;

namespace Bakabase.Service.Components.Federation;

public sealed class FederationMediaService(INodeIdentityProvider identity, FederationResourceService local,
    IPeerSessionFactory sessions, INodeTransport transport, FederationPeerService peers,
    FederationMediaSessions mediaSessions, LocalPlayerResolver players,
    IBatchPlayProcessLauncher launcher, IResourceService resources, GrantLeaseRegistry leases)
{
    public async Task<ResourceResolveResponse> ResolveAsync(ResourceResolveRequest request, CancellationToken ct)
    {
        if (request.Refs is not { Length: > 0 and <= 32 } || request.Refs.Any(x => x == null ||
                !NodeRequestSignature.IsIdentifier(x.NodeId) || !NodeRequestSignature.IsIdentifier(x.LibraryEpoch) || x.ResourceId <= 0))
            throw new FederationQueryException("InvalidResourceRefs", 422);
        var self = await identity.GetAsync(ct);
        var details = new Dictionary<ResourceRef, FederatedResourceDetail>();
        foreach (var group in request.Refs.Distinct().GroupBy(x => x.NodeId))
        {
            var batch = new ResourceResolveRequest(group.ToArray());
            ResourceResolveResponse response;
            PeerSessionSnapshot? peer = null;
            if (group.Key == self.NodeId)
                response = await local.ResolveAsync(FederationQueryAccess.LocalGrantId, batch, ct);
            else
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                peer = await sessions.GetAsync(group.Key, deadline.Token);
                using var authorized = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token,
                    leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(peer.GrantId)));
                using var http = await transport.SendAsync(peer, HttpMethod.Post,
                    "/federation/v1/export/resources/resolve", batch, authorized.Token);
                response = await ReadJsonAsync<ResourceResolveResponse>(http, authorized.Token);
            }
            if (response.Resources == null || response.Resources.Any(x => x == null || x.Ref == null ||
                    x.Assets is not { Length: <= 288 } || x.Assets.Any(a => a == null) ||
                    x.Properties is not { Length: <= 4096 } || x.Sources is not { Length: <= 4096 } ||
                    x.ExternalIdentities is not { Length: <= 4096 } || x.Collections is not { Length: <= 4096 }) ||
                response.Resources.Length != batch.Refs.Length ||
                response.Resources.Select(x => x.Ref).Distinct().Count() != batch.Refs.Length ||
                response.Resources.Any(x => !batch.Refs.Contains(x.Ref)))
                throw new FederationQueryException("InvalidPeerResponse", 502);
            foreach (var sourceDetail in response.Resources)
            {
                var detail = peer == null ? sourceDetail : sourceDetail with
                {
                    Assets = sourceDetail.Assets.Select(asset => asset with
                        { ExpiresAt = asset.ExpiresAt - peer.ClockOffset }).ToArray()
                };
                mediaSessions.Remember(detail, peer);
                details.Add(detail.Ref, detail);
            }
        }
        return new ResourceResolveResponse(request.Refs.Select(x => details[x]).ToArray());
    }

    public async Task<PlaybackSessionResponse> PrepareAsync(PlaybackSessionRequest request, string loopbackOrigin,
        CancellationToken ct)
    {
        if (request.AssetRef == null || request.AssetRef.ResourceRef == null || request.AssetRef.AssetId == null)
            throw new FederationQueryException("InvalidAssetRef", 422);
        if (request.Mode is not ("preview" or "player"))
            throw new FederationQueryException("UnsupportedPlaybackMode", 422);
        var source = mediaSessions.GetAsset(request.AssetRef);
        var localPath = await ValidateAndMapAsync(source, ct);
        var ticket = mediaSessions.Issue(source, localPath);
        var url = loopbackOrigin.TrimEnd('/') + "/federation/local/media/" + ticket.Id;
        if (request.Mode == "preview")
            return new PlaybackSessionResponse(url, source.Asset.ContentType, false, source.Asset.ExpiresAt);
        if (source.Asset.Kind == "image")
            throw new FederationQueryException("UnsupportedPlaybackMode", 422, "Use the image preview for this asset.");
        var player = players.ResolveInstalled(source.Asset.FileName) ??
                     throw new FederationQueryException("PlayerUnavailable", 501,
                         "Install a supported media player on this device to open this stream.");
        await launcher.LaunchAsync(player.ExecutablePath!,
            BatchPlayArguments.BuildFromTemplate(player.CommandTemplate, localPath ?? url), ct);
        if (source.Peer == null)
            await resources.MarkPlayed(new Dictionary<int, string>
                { [source.Ref.ResourceRef.ResourceId] = "FileSystem:" + localPath });
        return new PlaybackSessionResponse(null, source.Asset.ContentType, true, source.Asset.ExpiresAt);
    }

    public async Task<string?> ValidateAndMapAsync(AvailableFederationAsset source, CancellationToken ct)
    {
        if (source.Peer == null)
        {
            var lease = await local.OpenAsync(FederationQueryAccess.LocalGrantId, source.Ref.AssetId, ct);
            return lease.Path;
        }
        // Even a local mapped copy is used only after the source validates its current grant and asset.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct,
            leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(source.Peer.GrantId)));
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var head = await transport.SendAsync(source.Peer, HttpMethod.Head, AssetPath(source), cancellationToken: deadline.Token);
        EnsureSuccess(head);
        if (source.Asset.SourceRootId == null || source.Asset.RelativePath == null) return null;
        var mappings = await peers.GetPathMappingsAsync(source.Ref.ResourceRef.NodeId, ct);
        var mapping = mappings.FirstOrDefault(x => x.SourceRootId == source.Asset.SourceRootId);
        return mapping == null ? null : MediaPathBoundary.Map(mapping.LocalPath, source.Asset.RelativePath);
    }

    public static string AssetPath(AvailableFederationAsset source) =>
        "/federation/v1/export/assets/" + Uri.EscapeDataString(source.Ref.AssetId);

    public static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new FederationQueryException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "GrantRevoked",
                System.Net.HttpStatusCode.Gone => "AssetExpired",
                System.Net.HttpStatusCode.Conflict => "LibraryEpochChanged",
                _ => "PeerUnavailable"
            }, (int)response.StatusCode, "The source could not serve this media request.",
                retryable: (int)response.StatusCode >= 500);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        EnsureSuccess(response);
        const int limit = 8 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new FederationQueryException("PeerResponseTooLarge", 502);
        await using var body = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var bytes = new byte[16384];
        int count;
        while ((count = await body.ReadAsync(bytes, ct)) > 0)
        {
            if (buffer.Length + count > limit) throw new FederationQueryException("PeerResponseTooLarge", 502);
            buffer.Write(bytes, 0, count);
        }
        try
        {
            return JsonSerializer.Deserialize<T>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), FederationJson.Options)
                   ?? throw new FederationQueryException("InvalidPeerResponse", 502);
        }
        catch (JsonException e) { throw new FederationQueryException("InvalidPeerResponse", 502, innerException: e); }
    }
}
