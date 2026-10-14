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
    FederationMediaSessions mediaSessions, FederationPlayerPolicy players,
    IBatchPlayProcessLauncher launcher, IResourceService resources, GrantLeaseRegistry leases,
    FederationDirectoryService directories, IFederationImageOpener imageOpener,
    Bakabase.Modules.Player.Services.DevicePlaybackOverridesStore? overrides = null,
    Bakabase.Abstractions.Components.ISystemPlayer? systemPlayer = null)
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
                detail = detail with { DirectoryAccess = await directories.GetAccessAsync(detail, peer, ct), Location = null };
                // Advisory only: resolving a detail already read the source with
                // current credentials. Do not make a HEAD request for every
                // thumbnail; PrepareAsync re-authorizes the selected image.
                IReadOnlyList<NodePathMapping> mappings = peer == null || !imageOpener.Available
                    ? [] : await peers.GetPathMappingsAsync(detail.Ref.NodeId, ct);
                var assets = new List<FederatedAsset>(detail.Assets.Length);
                foreach (var asset in detail.Assets)
                {
                    ct.ThrowIfCancellationRequested();
                    bool? canOpen = null;
                    if (asset.Kind == "image")
                    {
                        canOpen = false;
                        if (imageOpener.Available)
                        {
                            string? imagePath;
                            if (peer == null)
                            {
                                try
                                {
                                    imagePath = (await local.OpenAsync(FederationQueryAccess.LocalGrantId,
                                        asset.AssetId, ct)).Path;
                                }
                                catch (FederationQueryException) { imagePath = null; }
                            }
                            else imagePath = FederationPathMappingResolver.MapAsset(asset, mappings);
                            canOpen = FederationImageOpener.TryGetImagePath(asset.FileName, imagePath, out _);
                        }
                    }
                    // A peer never decides what can be opened on this device.
                    assets.Add(asset with {CanOpenLocally = canOpen});
                }
                detail = detail with {Assets = assets.ToArray()};
                mediaSessions.Remember(detail, peer, ct);
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
        using var authorized = CancellationTokenSource.CreateLinkedTokenSource(ct,
            source.Peer == null ? CancellationToken.None :
                leases.GetCancellationToken(GrantLeaseRegistry.OutboundKey(source.Peer.GrantId)));
        var localPath = await ValidateAndMapAsync(source, authorized.Token);
        authorized.Token.ThrowIfCancellationRequested();
        string? selectedPlayerId = null;
        if (request.Mode == "player" && overrides != null)
        {
            var reference = request.AssetRef.ResourceRef;
            var choice = await overrides.ResolveAsync($"node:{reference.NodeId}:{reference.LibraryEpoch}",
                reference.ResourceId, source.Asset.Kind, authorized.Token);
            if (choice?.Mode == "native") selectedPlayerId = choice.PlayerId;
        }
        if (request.Mode == "player" && source.Asset.Kind == "image")
        {
            if (selectedPlayerId is not (null or "system-default"))
                throw new FederationQueryException("PlayerUnavailable", 409,
                    "The selected player cannot open images. Choose the system image application or preview.");
            if (!imageOpener.Available)
                throw new FederationQueryException("PlayerUnavailable", 501,
                    "An image viewer cannot be opened on this device. Use the image preview instead.");
            if (localPath == null || !File.Exists(localPath))
                throw new FederationQueryException("MappedPathUnavailable", 409,
                    "This image is not available on this computer. Configure its path mapping or use the image preview.");
            if (!FederationImageOpener.TryGetImagePath(source.Asset.FileName, localPath, out var physicalImage))
                throw new FederationQueryException("UnsupportedPlaybackMode", 422,
                    "Only a matching local image can be opened. Use the image preview instead.");
            authorized.Token.ThrowIfCancellationRequested();
            imageOpener.Open(physicalImage!, authorized.Token);
        }
        else
        {
            if (request.Mode == "preview")
            {
                var ticket = mediaSessions.Issue(source, localPath, authorized.Token);
                var url = loopbackOrigin.TrimEnd('/') + "/federation/local/media/" + ticket.Id;
                return new PlaybackSessionResponse(url, source.Asset.ContentType, false, source.Asset.ExpiresAt);
            }
            var player = players.Resolve(source.Asset.FileName, localPath, selectedPlayerId);
            string? physicalMedia = null;
            if (localPath != null && !TryGetNativeMediaPath(source.Asset, localPath, out physicalMedia))
                throw new FederationQueryException("UnsupportedPlaybackMode", 422,
                    "The mapped file does not match this media type. Check the mapping or use the preview.");
            authorized.Token.ThrowIfCancellationRequested();
            if (player.IsSystemDefault)
            {
                if (systemPlayer == null || physicalMedia == null)
                    throw new FederationQueryException("PlayerUnavailable", 409);
                // Validate the physical target too: a peer's safe-looking .mp4 name
                // must not turn a mapped link to an executable into an OS-shell launch.
                try { await systemPlayer.Play(physicalMedia); }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    throw new FederationQueryException("PlayerUnavailable", 501,
                        "The default media application could not be opened. Choose another player or use the preview.",
                        innerException: e);
                }
            }
            else
            {
                string? url = null;
                if (physicalMedia == null)
                {
                    var ticket = mediaSessions.Issue(source, null, authorized.Token);
                    url = loopbackOrigin.TrimEnd('/') + "/federation/local/media/" + ticket.Id;
                }
                await launcher.LaunchAsync(player.ExecutablePath!,
                    FederationPlayerArguments.Build(player, physicalMedia, url ?? ""), authorized.Token);
            }
        }
        if (source.Peer == null)
            await resources.MarkPlayed(new Dictionary<int, string>
                { [source.Ref.ResourceRef.ResourceId] = "FileSystem:" + localPath });
        return new PlaybackSessionResponse(null, source.Asset.ContentType, true, source.Asset.ExpiresAt);
    }

    internal static bool TryGetNativeMediaPath(FederatedAsset asset, string? localPath, out string? physicalPath)
    {
        physicalPath = null;
        if (asset.Kind is not ("audio" or "video") || string.IsNullOrWhiteSpace(localPath) ||
            !Path.IsPathFullyQualified(localPath) || !File.Exists(localPath)) return false;
        try
        {
            var physical = MediaPathBoundary.ResolvePhysical(localPath);
            var extension = Path.GetExtension(asset.FileName);
            if (!PlayerFileCapabilities.CanStream(asset.FileName) ||
                !extension.Equals(Path.GetExtension(localPath), StringComparison.OrdinalIgnoreCase) ||
                !extension.Equals(Path.GetExtension(physical), StringComparison.OrdinalIgnoreCase) ||
                MediaContentTypes.Get(asset.FileName)?.Kind != asset.Kind ||
                MediaContentTypes.Get(physical)?.Kind != asset.Kind || !File.Exists(physical)) return false;
            physicalPath = physical;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
    }

    /// <summary>Renew leases this close to expiry before use, so a read never races the source's clock.</summary>
    private static readonly TimeSpan LeaseRenewalMargin = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Keeps a ticket usable past the source's short asset lease by resolving the resource again,
    /// which re-authorizes with the current grant, and adopting the new lease for the same file.
    /// </summary>
    public async Task RenewAsync(FederationMediaTicket ticket, bool force, CancellationToken ct)
    {
        var current = ticket.Source;
        if (!force && current.Asset.ExpiresAt - DateTimeOffset.UtcNow > LeaseRenewalMargin) return;
        var detail = (await ResolveAsync(new ResourceResolveRequest([current.Ref.ResourceRef]), ct)).Resources[0];
        var renewed = detail.Assets.Where(asset => asset.Kind == current.Asset.Kind &&
            asset.FileName == current.Asset.FileName && asset.SourceRootId == current.Asset.SourceRootId &&
            asset.RelativePath == current.Asset.RelativePath).ToArray();
        if (renewed.Length != 1)
            throw new FederationQueryException("AssetGone", 404, "The source no longer exposes this media file.");
        mediaSessions.Renew(ticket, mediaSessions.GetAsset(new AssetRef(detail.Ref, renewed[0].AssetId)));
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
        var mappings = await peers.GetPathMappingsAsync(source.Ref.ResourceRef.NodeId, deadline.Token);
        deadline.Token.ThrowIfCancellationRequested();
        return FederationPathMappingResolver.MapAsset(source.Asset, mappings);
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

    internal static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken ct)
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
