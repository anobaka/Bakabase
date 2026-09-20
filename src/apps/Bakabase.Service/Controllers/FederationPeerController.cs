using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record FederationPeerStatusResponse(NodeIdentity Identity, bool SharingEnabled,
    RemoteAccessMode RemoteAccessMode, bool RequirePairing, IReadOnlyList<FederationPeerView> Peers,
    IReadOnlyList<NodePairingRequestView> Requests);
public sealed record FederationSharingRequest(bool Enabled, bool EnablePairedRemoteAccess = false);
public sealed record FederationConnectRequest(string Address, string? Code = null);
public sealed record FederationClaimRequest(string RequestId);
public sealed record FederationPeerEnabledRequest(bool Enabled);
public sealed record FederationPathMappingsRequest(NodePathMapping[] Mappings);
public sealed record FederationIdentityResetRequest(bool AsNewNode = false);
public sealed record FederationPeerChange(bool Changed = true);

[ApiController]
[Route("federation/local/peers")]
[FederationEndpoint(FederationEndpointKind.Local)]
public sealed class FederationPeerController(FederationPeerService peers, NodePairingClient pairing,
    INodeIdentityProvider identity, NodeGrantService grants, NodePairingRateLimiter rateLimiter,
    INodePeerDiscovery discovery, INodeTransport transport, PeerSessionFactory sessions,
    IRemoteAccessService remoteAccess, IBOptionsManager<RemoteAccessOptions> remoteOptions,
    TimeProvider timeProvider) : FederationControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetFederationPeers")]
    [ProducesResponseType(typeof(FederationPeerStatusResponse), 200)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var status = await peers.GetStatusAsync(ct);
        return FederationResult(new FederationPeerStatusResponse(status.Identity, status.SharingEnabled,
            remoteAccess.GetEffectiveMode(), remoteAccess.GetRequirePairing(), status.Peers.Select(peer =>
                peer with { ConnectionState = peer.Enabled ? sessions.GetConnectionState(peer.NodeId) : "Disabled" }).ToArray(),
            status.Requests));
    }

    [HttpPut("sharing")]
    [SwaggerOperation(OperationId = "SetFederationSharing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Sharing([FromBody] FederationSharingRequest request, CancellationToken ct)
    {
        if (request.Enabled && request.EnablePairedRemoteAccess)
        {
            // Only the explicit sharing wizard action changes legacy remote-access settings.
            // Capture the existing settings before initializing either identity: the
            // legacy file watcher can briefly publish defaults during its own write.
            var configured = remoteOptions.Value;
            var allowLiveTranscode = configured.AllowLiveTranscode;
            var legacyServerId = configured.ServerId;
            if (string.IsNullOrWhiteSpace(legacyServerId))
                legacyServerId = await remoteAccess.GetOrCreateServerIdAsync();
            await identity.GetAsync(ct);
            await remoteOptions.SaveAsync(new RemoteAccessOptions
            {
                // A cloned federation node intentionally has a different NodeId;
                // never replace the existing legacy server identity with that value.
                ServerId = legacyServerId,
                AllowLiveTranscode = allowLiveTranscode,
                RequirePairing = true,
                Mode = RemoteAccessMode.Enabled
            });
        }
        await peers.SetSharingAsync(request.Enabled, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpPost("invite")]
    [SwaggerOperation(OperationId = "CreateFederationInvitation")]
    [ProducesResponseType(typeof(NodeInvitation), 200)]
    public async Task<IActionResult> Invite(CancellationToken ct)
    {
        RequireRemoteAccess();
        return FederationResult(await peers.IssueInvitationAsync(ct));
    }

    [HttpGet("discover")]
    [SwaggerOperation(OperationId = "DiscoverFederationPeers")]
    [ProducesResponseType(typeof(NodeDiscoveryCandidate[]), 200)]
    public async Task<IActionResult> Discover(CancellationToken ct) =>
        FederationResult(await discovery.DiscoverAsync(ct));

    [HttpPost("connect")]
    [SwaggerOperation(OperationId = "ConnectFederationPeer")]
    [ProducesResponseType(typeof(NodePairingOutcome), 200)]
    public async Task<IActionResult> Connect([FromBody] FederationConnectRequest request, CancellationToken ct) =>
        FederationResult(await pairing.ConnectAsync(request.Address, request.Code, ct));

    [HttpPost("claim")]
    [SwaggerOperation(OperationId = "ClaimFederationPairing")]
    [ProducesResponseType(typeof(NodePairingOutcome), 200)]
    public async Task<IActionResult> Claim([FromBody] FederationClaimRequest request, CancellationToken ct) =>
        FederationResult(await pairing.ClaimAsync(request.RequestId, ct));

    [HttpPost("requests/{id}/approve")]
    [SwaggerOperation(OperationId = "ApproveFederationPairing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        RequireRemoteAccess();
        await peers.ApproveAsync(id, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpPost("requests/{id}/reject")]
    [SwaggerOperation(OperationId = "RejectFederationPairing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Reject(string id, CancellationToken ct)
    {
        await peers.RejectAsync(id, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpDelete("grants/{grantId}")]
    [SwaggerOperation(OperationId = "RevokeFederationGrant")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Revoke(string grantId, CancellationToken ct)
    {
        await peers.RevokeAsync(grantId, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpDelete("{nodeId}/outbound")]
    [SwaggerOperation(OperationId = "ForgetFederationPeer")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Forget(string nodeId, CancellationToken ct)
    {
        await peers.ForgetOutboundAsync(nodeId, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpPut("{nodeId}/enabled")]
    [SwaggerOperation(OperationId = "SetFederationPeerEnabled")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Enable(string nodeId, [FromBody] FederationPeerEnabledRequest request,
        CancellationToken ct)
    {
        await peers.SetEnabledAsync(nodeId, request.Enabled, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpPut("{nodeId}/path-mappings")]
    [SwaggerOperation(OperationId = "SetFederationPathMappings")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Mappings(string nodeId, [FromBody] FederationPathMappingsRequest request,
        CancellationToken ct)
    {
        await peers.SetPathMappingsAsync(nodeId, request.Mappings ?? [], ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpGet("{nodeId}/mapping-roots")]
    [SwaggerOperation(OperationId = "GetFederationMappingRoots")]
    [ProducesResponseType(typeof(MappingRoot[]), 200)]
    public async Task<IActionResult> MappingRoots(string nodeId, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(8));
        using var response = await transport.SendAsync(nodeId, HttpMethod.Get,
            "/federation/v1/export/mapping-roots", cancellationToken: deadline.Token);
        return FederationResult(await FederationHttpClient.ReadEnvelopeAsync<MappingRoot[]>(response, deadline.Token));
    }

    [HttpPost("identity/reset")]
    [SwaggerOperation(OperationId = "ResetFederationIdentity")]
    [ProducesResponseType(typeof(NodeIdentity), 200)]
    public async Task<IActionResult> Reset([FromBody] FederationIdentityResetRequest request, CancellationToken ct) =>
        FederationResult(request.AsNewNode ? await peers.ResetAsNewNodeAsync(ct) : await peers.RotateLibraryEpochAsync(ct));

    [HttpGet("~/federation/v1/info")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "GetFederationNodeInfo")]
    [ProducesResponseType(typeof(NodeInfo), 200)]
    public async Task<IActionResult> Info(CancellationToken ct)
    {
        var local = await identity.GetAsync(ct);
        return FederationResult(new NodeInfo(local.NodeId, local.LibraryEpoch, local.Name, 1, timeProvider.GetUtcNow()));
    }

    [HttpPost("~/federation/v1/pair/code")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "ExchangeFederationInvitation")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairCode([FromBody] NodePairCodeRequest request, CancellationToken ct)
    {
        CheckRate();
        return FederationResult(await peers.ExchangeCodeAsync(request, ct));
    }

    [HttpPost("~/federation/v1/pair/request")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "RequestFederationPairing")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairRequest([FromBody] NodePairRequest request, CancellationToken ct)
    {
        CheckRate();
        return FederationResult(await peers.RequestPairingAsync(request, ct));
    }

    [HttpPost("~/federation/v1/pair/claim")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "ClaimFederationNodeGrant")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairClaim([FromBody] NodePairClaimRequest request, CancellationToken ct) =>
        FederationResult(await peers.ClaimPairingAsync(request, ct));

    [HttpPost("~/federation/v1/export/handshake")]
    [FederationEndpoint(FederationEndpointKind.Export)]
    [SwaggerOperation(OperationId = "VerifyFederationNode")]
    [ProducesResponseType(typeof(NodeHandshakeResponse), 200)]
    public async Task<IActionResult> Handshake([FromBody] NodeHandshakeRequest request, CancellationToken ct)
    {
        var principal = FederationHttpContext.GetNodePrincipal(HttpContext) ??
                        throw new FederationAccessException("NodeAuthenticationRequired", 401, "A node read grant is required.");
        return FederationResult(await grants.CreateHandshakeAsync(principal.GrantId, request.Challenge, ct));
    }

    private void RequireRemoteAccess()
    {
        if (remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled)
            throw new FederationAccessException("RemoteAccessDisabled", 403, "Enable paired remote access before sharing this library.");
    }

    private void CheckRate()
    {
        if (!rateLimiter.TryTake(HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
            throw new FederationAccessException("PairingRateLimited", 429, "Too many pairing requests. Try again in a minute.");
    }
}
