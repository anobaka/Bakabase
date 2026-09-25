using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <param name="ReachableAddresses">Where other devices can reach this one while it shares; shown next to its code.</param>
public sealed record FederationPeerStatusResponse(NodeIdentity Identity, bool SharingEnabled,
    RemoteAccessMode RemoteAccessMode, bool RequirePairing, IReadOnlyList<FederationPeerView> Peers,
    IReadOnlyList<NodePairingRequestView> Requests, bool BrowsingEnabled = false,
    IReadOnlyList<string>? ReachableAddresses = null);
public sealed record FederationBrowsingRequest(bool Enabled);
public sealed record FederationSharingRequest(bool Enabled, bool EnablePairedRemoteAccess = false);
/// <param name="ShareBack">Also let the other device read this library (turns sharing on), so one approval pairs both ways.</param>
public sealed record FederationConnectRequest(string Address, string? Code = null, bool ShareBack = false);
public sealed record FederationDeviceNameRequest(string? Name);
public sealed record FederationClaimRequest(string RequestId);
public sealed record FederationPeerEnabledRequest(bool Enabled);
public sealed record FederationPathMappingsRequest(NodePathMapping[] Mappings, NodePathMapping[]? ExpectedMappings = null);
public sealed record FederationIdentityResetRequest(bool AsNewNode = false);
public sealed record FederationPeerChange(bool Changed = true);

[ApiController]
[Route("federation/local/peers")]
[FederationEndpoint(FederationEndpointKind.Local)]
public sealed class FederationPeerController(FederationPeerService peers, NodePairingClient pairing,
    INodeIdentityProvider identity, NodeGrantService grants, NodePairingRateLimiter rateLimiter,
    INodePeerDiscovery discovery, INodeTransport transport, PeerSessionFactory sessions,
    IRemoteAccessService remoteAccess, IBOptionsManager<RemoteAccessOptions> remoteOptions,
    TimeProvider timeProvider, FederationPairingFlow flow, FederationStateStore store) : FederationControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetFederationPeers")]
    [ProducesResponseType(typeof(FederationPeerStatusResponse), 200)]
    public async Task<IActionResult> Status([FromServices] FederationBrowsingControl browsing, CancellationToken ct)
    {
        var status = await peers.GetStatusAsync(ct);
        var mode = remoteAccess.GetEffectiveMode();
        return FederationResult(new FederationPeerStatusResponse(status.Identity, status.SharingEnabled,
            mode, remoteAccess.GetRequirePairing(), status.Peers.Select(peer =>
                peer with { ConnectionState = peer.Enabled ? sessions.GetConnectionState(peer.NodeId) : "Disabled" }).ToArray(),
            status.Requests, await browsing.IsEnabledAsync(ct),
            status.SharingEnabled && mode != RemoteAccessMode.Disabled
                // Every listening port reaches the same instance; show one address per host.
                ? remoteAccess.GetReachableAddresses().Select(a => a.Url)
                    .GroupBy(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url)
                    .Select(g => g.First()).ToArray()
                : []));
    }

    [HttpPut("browsing")]
    [SwaggerOperation(OperationId = "SetFederationBrowsing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Browsing([FromBody] FederationBrowsingRequest request,
        [FromServices] FederationBrowsingControl browsing, CancellationToken ct)
    {
        await browsing.SetEnabledAsync(request.Enabled, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpPut("sharing")]
    [SwaggerOperation(OperationId = "SetFederationSharing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Sharing([FromBody] FederationSharingRequest request, CancellationToken ct)
    {
        await SetSharingAsync(request.Enabled, request.EnablePairedRemoteAccess, ct);
        return FederationResult(new FederationPeerChange());
    }

    private async Task SetSharingAsync(bool enabled, bool enablePairedRemoteAccess, CancellationToken ct)
    {
        if (enabled && enablePairedRemoteAccess)
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
        await peers.SetSharingAsync(enabled, ct);
    }

    [HttpPut("name")]
    [SwaggerOperation(OperationId = "SetFederationDeviceName")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Name([FromBody] FederationDeviceNameRequest request, CancellationToken ct)
    {
        await store.SetDisplayNameAsync(request.Name, ct);
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
    public async Task<IActionResult> Connect([FromBody] FederationConnectRequest request, CancellationToken ct)
    {
        IReadOnlyList<string>? shareBack = null;
        if (request.ShareBack)
        {
            // Sharing back is an explicit choice on this form; make this device reachable for it,
            // without replacing a remote-access mode the user already opened further.
            if (!await store.IsSharingEnabledAsync(ct) || remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled)
                await SetSharingAsync(true, remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled, ct);
            shareBack = flow.GetShareBackAddresses(request.Address);
            if (shareBack.Count == 0)
                throw new FederationAccessException("NoReachableAddress", 409,
                    "This device has no network address the other device could use to read it back.");
        }
        var outcome = await pairing.ConnectAsync(request.Address, request.Code, ct, shareBack);
        if (outcome.Outcome == "granted") await flow.OnOutboundGrantedAsync(ct);
        return FederationResult(outcome);
    }

    [HttpPost("claim")]
    [SwaggerOperation(OperationId = "ClaimFederationPairing")]
    [ProducesResponseType(typeof(NodePairingOutcome), 200)]
    public async Task<IActionResult> Claim([FromBody] FederationClaimRequest request, CancellationToken ct)
    {
        var outcome = await pairing.ClaimAsync(request.RequestId, ct);
        if (outcome.Outcome == "granted") await flow.OnOutboundGrantedAsync(ct);
        return FederationResult(outcome);
    }

    [HttpPost("requests/{id}/approve")]
    [SwaggerOperation(OperationId = "ApproveFederationPairing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Approve(string id, CancellationToken ct)
    {
        RequireRemoteAccess();
        flow.ReadBack(await peers.ApproveAsync(id, ct));
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

    [HttpDelete("requests/{id}")]
    [SwaggerOperation(OperationId = "CancelFederationPairing")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct)
    {
        await peers.CancelOutgoingAsync(id, ct);
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

    [HttpDelete("{nodeId}")]
    [SwaggerOperation(OperationId = "RemoveFederationPeer")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Remove(string nodeId, CancellationToken ct)
    {
        await peers.RemovePeerAsync(nodeId, ct);
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
        await peers.SetPathMappingsAsync(nodeId, request.Mappings ?? [], ct, request.ExpectedMappings);
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
    public async Task<IActionResult> Reset([FromBody] FederationIdentityResetRequest request,
        [FromServices] FederationBrowsingControl browsing, CancellationToken ct)
    {
        var node = request.AsNewNode ? await peers.ResetAsNewNodeAsync(ct) : await peers.RotateLibraryEpochAsync(ct);
        // Old local tickets and reads must not survive changing this library's identity either.
        await browsing.SetEnabledAsync(false, CancellationToken.None);
        return FederationResult(node);
    }

    [HttpGet("~/federation/v1/info")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "GetFederationNodeInfo")]
    [ProducesResponseType(typeof(NodeInfo), 200)]
    public async Task<IActionResult> Info(CancellationToken ct)
    {
        var local = await identity.GetAsync(ct);
        // What this install says it is, where the host can tell: optional on the wire.
        var self = HttpContext.RequestServices.GetService<IServerSelfDescription>();
        var info = new NodeInfo(local.NodeId, local.LibraryEpoch, local.Name, 1, timeProvider.GetUtcNow())
            .DescribedBy(self);
        // The data sync capability members, where the host has data sync: optional on the wire too.
        if (HttpContext.RequestServices.GetService<INodeInfoContributor>() is { } contributor)
            info = await contributor.ContributeAsync(info, ct);
        return FederationResult(info);
    }

    [HttpPost("~/federation/v1/pair/code")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "ExchangeFederationInvitation")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairCode([FromBody] NodePairCodeRequest request, CancellationToken ct)
    {
        CheckRate();
        var exchange = await peers.ExchangeCodeAsync(request, RemoteAddress, ct);
        if (exchange.Outcome == "granted") flow.ReadBack(request.NodeId);
        return FederationResult(exchange);
    }

    [HttpPost("~/federation/v1/pair/request")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "RequestFederationPairing")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairRequest([FromBody] NodePairRequest request,
        [FromServices] INotificationService notifications, [FromServices] IBakabaseLocalizer localizer,
        CancellationToken ct)
    {
        CheckRate();
        var (exchange, created) = await peers.SubmitPairingRequestAsync(request, RemoteAddress, ct);
        if (created)
        {
            try
            {
                // Approval needs a person at this device while the request is still fresh.
                await notifications.CreateAsync(new NotificationCreationInputModel
                {
                    Source = "Federation",
                    Title = localizer["Federation_PairingRequest_Title", request.NodeName.Trim()],
                    Body = localizer["Federation_PairingRequest_Body", RemoteAddress ?? "?"],
                    PayloadJson = JsonSerializer.Serialize(new { route = "/federation/devices" }),
                    Severity = AppNotificationSeverity.Warning
                });
            }
            catch (Exception)
            {
                // A notification is a convenience; the request itself is stored.
            }
        }
        return FederationResult(exchange);
    }

    [HttpPost("~/federation/v1/pair/claim")]
    [FederationEndpoint(FederationEndpointKind.Public)]
    [SwaggerOperation(OperationId = "ClaimFederationNodeGrant")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairClaim([FromBody] NodePairClaimRequest request, CancellationToken ct) =>
        FederationResult(await peers.ClaimPairingAsync(request, ct));

    [HttpPost("~/federation/v1/export/handshake")]
    [FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.Any)]
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

    private string? RemoteAddress
    {
        get
        {
            var address = HttpContext.Connection.RemoteIpAddress;
            if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
            return address?.ToString();
        }
    }

    private void CheckRate()
    {
        if (!rateLimiter.TryTake(RemoteAddress ?? "unknown"))
            throw new FederationAccessException("PairingRateLimited", 429, "Too many pairing requests. Try again in a minute.");
    }
}
