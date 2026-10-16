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
using Bakabase.Modules.DataSync.Services;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record FederationPeerStatusResponse(NodeIdentity Identity,
    RemoteAccessMode RemoteAccessMode, bool RequirePairing, IReadOnlyList<string> ReachableAddresses);
public sealed record FederationDeviceNameRequest(string? Name, string? ExpectedName = null);
/// <param name="AsNewNode">A new node rather than a new library generation of this one.</param>
/// <param name="ReplaceInstallIdentity">
/// With <paramref name="AsNewNode"/>: a copied installation, which also takes a new install
/// identity and lets go of the devices paired under the old one. Recovering an unreadable
/// sharing state leaves it out, and keeps both.
/// </param>
public sealed record FederationIdentityResetRequest(bool AsNewNode = false, bool ReplaceInstallIdentity = false);
public sealed record FederationPeerChange(bool Changed = true);

[ApiController]
[Route("federation/local/peers")]
[FederationEndpoint(FederationEndpointKind.Local)]
public sealed class FederationPeerController(FederationPeerService peers,
    INodeIdentityProvider identity, NodeGrantService grants, IRemoteAccessService remoteAccess,
    TimeProvider timeProvider, FederationStateStore store) : FederationControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetFederationPeers")]
    [ProducesResponseType(typeof(FederationPeerStatusResponse), 200)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var node = await identity.GetAsync(ct);
        return FederationResult(new FederationPeerStatusResponse(node, remoteAccess.GetEffectiveMode(),
            remoteAccess.GetRequirePairing(), remoteAccess.GetReachableAddresses().Select(a => a.Url)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
    }

    [HttpPut("name")]
    [SwaggerOperation(OperationId = "SetFederationDeviceName")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Name([FromBody] FederationDeviceNameRequest request, CancellationToken ct)
    {
        await store.ChangeDisplayNameAsync(request.Name, request.ExpectedName, null, ct);
        return FederationResult(new FederationPeerChange());
    }

    [HttpPost("identity/reset")]
    [SwaggerOperation(OperationId = "ResetFederationIdentity")]
    [ProducesResponseType(typeof(NodeIdentity), 200)]
    public async Task<IActionResult> Reset([FromBody] FederationIdentityResetRequest request,
        [FromServices] IRemoteDeviceService remoteDevices,
        [FromServices] RemoteConnectionRegistry connections, CancellationToken ct)
    {
        // A new install identity comes only with a new node: asked for alone, it would be
        // quietly left out, and the install would keep answering as the one it was copied from.
        if (request.ReplaceInstallIdentity && !request.AsNewNode)
            throw new FederationAccessException("InvalidIdentityReset", 400,
                "A new install identity is only given together with a new node (asNewNode).");
        NodeIdentity node;
        if (request.AsNewNode && request.ReplaceInstallIdentity)
        {
            // A copied data directory carries its install's remote-access identity too, and
            // answering under it makes each copy take the other for itself. A new device takes a
            // new one — the node then inherits it, as the first node did — and lets go of the
            // devices paired under the old one: they expect that identity, and their keys were
            // issued to whichever install it was.
            //
            // It lets go of the servers it manages too, and first, while nothing else has changed:
            // a failure then leaves its identity, paired devices and node as they were, to be
            // tried again. Here only — each
            // key came with the copy and is the original's as well, so asking a server to revoke
            // it would cut the original off too (see server-switching.md, "Trust is explicit and
            // pairwise"). Both desktop and headless hosts own this same connection store.
            if (HttpContext?.RequestServices.GetService<IManagedServerService>() is { } managedServers)
            {
                try
                {
                    await managedServers.ForgetAllLocallyAsync(ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    HttpContext.RequestServices.GetService<ILogger<FederationPeerController>>()?.LogWarning(e,
                        "Could not forget the servers this device manages; it was not made a new device");
                    throw new FederationAccessException("ManagedServersNotForgotten", 500,
                        "The devices this one manages could not be removed here, so it was not made a new device. Try again.");
                }
            }
            await remoteAccess.RegenerateServerIdAsync();
            // Past this point the new identity is saved: the rest must follow it, whether or not
            // whoever asked is still waiting, or the install answers under the new id with the
            // old node and the old paired devices.
            foreach (var device in await remoteDevices.ForgetAllAsync(CancellationToken.None))
                connections.AbortDevice(device);
            node = await peers.ResetAsNewNodeAsync(inheritHostIdentity: true, CancellationToken.None);
        }
        // Recovering an unreadable sharing state: a new node, of its own id. The install keeps
        // its identity and its paired devices — nothing about them was lost.
        else if (request.AsNewNode) node = await peers.ResetAsNewNodeAsync(ct: ct);
        else node = await peers.RotateLibraryEpochAsync(ct);
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

}
