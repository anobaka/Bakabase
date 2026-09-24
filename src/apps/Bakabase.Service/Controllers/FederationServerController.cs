using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record ManagedServerAddressRequest(string Address);
/// <param name="Code">A pairing code shown on the other server; without one a request is filed for it to approve.</param>
public sealed record ManagedServerPairRequest(string Address, string? Code = null);
/// <param name="Mappings">The whole table, replacing the previous one. Null or absent clears it, as an empty list does.</param>
public sealed record ManagedServerPathMappingsRequest(ManagedServerPathMapping[]? Mappings = null);
/// <param name="Path">A path on the server's own UI to land on; its root when absent.</param>
public sealed record ManagedServerOpenRequest(string? Path = null);

/// <summary>
/// Servers this device manages in full, shown in its own window through a local relay.
/// </summary>
/// <remarks>
/// Local-only, like every <c>/federation/local</c> route. The work is done by whatever
/// host composed the relays — the desktop app — so on a headless server the listing says
/// management is unavailable and everything else refuses.
/// </remarks>
[ApiController]
[Route("federation/local/servers")]
[FederationEndpoint(FederationEndpointKind.Local)]
[FederationServerController.InvalidRequest]
public sealed class FederationServerController : FederationControllerBase
{
    private IManagedServerService? Managed => HttpContext.RequestServices.GetService<IManagedServerService>();

    private IManagedServerService Required => Managed ?? throw new FederationAccessException(
        "ManagementUnavailable", 404, "This installation cannot manage other servers.");

    [HttpGet]
    [SwaggerOperation(OperationId = "GetManagedServers")]
    [ProducesResponseType(typeof(ManagedServersView), 200)]
    public async Task<IActionResult> List([FromQuery] bool probe, CancellationToken ct) =>
        FederationResult(Managed == null
            ? new ManagedServersView(false, [], [])
            : await Managed.GetAsync(probe, ct));

    /// <summary>
    /// Servers on this network that could be managed from here, found by their remote-access
    /// beacons — not by library sharing, which a server that can be managed usually has off.
    /// </summary>
    [HttpGet("discover")]
    [SwaggerOperation(OperationId = "DiscoverManagedServers")]
    [ProducesResponseType(typeof(ManagedServerDiscoveryView), 200)]
    public async Task<IActionResult> Discover(CancellationToken ct) =>
        FederationResult(await Required.DiscoverAsync(ct));

    [HttpPost("probe")]
    [SwaggerOperation(OperationId = "ProbeManagedServer")]
    [ProducesResponseType(typeof(ManagedServerProbeView), 200)]
    public async Task<IActionResult> Probe([FromBody] ManagedServerAddressRequest request, CancellationToken ct) =>
        FederationResult(await Required.ProbeAsync(request.Address, ct));

    [HttpPost("pair")]
    [SwaggerOperation(OperationId = "PairManagedServer")]
    [ProducesResponseType(typeof(ManagedServerPairingView), 200)]
    public async Task<IActionResult> Pair([FromBody] ManagedServerPairRequest request, CancellationToken ct) =>
        FederationResult(await Required.PairAsync(request.Address, request.Code, ct));

    [HttpDelete("requests/{requestId}")]
    [SwaggerOperation(OperationId = "CancelManagedServerRequest")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> CancelRequest(string requestId, CancellationToken ct) =>
        FederationResult(new FederationPeerChange(await Required.CancelRequestAsync(requestId, ct)));

    [HttpDelete("{serverId}")]
    [SwaggerOperation(OperationId = "ForgetManagedServer")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> Forget(string serverId, CancellationToken ct) =>
        FederationResult(new FederationPeerChange(await Required.ForgetAsync(serverId, ct)));

    [HttpPut("{serverId}/path-mappings")]
    [SwaggerOperation(OperationId = "SetManagedServerPathMappings")]
    [ProducesResponseType(typeof(FederationPeerChange), 200)]
    public async Task<IActionResult> PathMappings(string serverId,
        [FromBody] ManagedServerPathMappingsRequest request, CancellationToken ct) =>
        FederationResult(new FederationPeerChange(
            await Required.SetPathMappingsAsync(serverId, request.Mappings ?? [], ct)));

    [HttpPost("{serverId}/open")]
    [SwaggerOperation(OperationId = "OpenManagedServer")]
    [ProducesResponseType(typeof(ManagedServerOpenView), 200)]
    public async Task<IActionResult> Open(string serverId, [FromBody] ManagedServerOpenRequest? request,
        CancellationToken ct)
    {
        ManagedServerOpenView? opened;

        try
        {
            opened = await Required.OpenAsync(serverId, request?.Path, ct);
        }
        catch (IOException)
        {
            // No loopback port could be bound for the server's relay. Worth a retry — a port
            // taken by something else is often released again — and not an internal error.
            throw new FederationAccessException("RelayUnavailable", 503,
                "The relay for that server could not be started on this device.");
        }

        return FederationResult(opened ?? throw new FederationAccessException("ServerNotManaged", 404,
            "This device does not manage that server."));
    }

    [HttpPost("import-legacy-client")]
    [SwaggerOperation(OperationId = "ImportLegacyClientServers")]
    [ProducesResponseType(typeof(ManagedServerImportView), 200)]
    public async Task<IActionResult> ImportLegacyClient(CancellationToken ct) =>
        FederationResult(await Required.ImportFromLegacyClientAsync(ct));

    /// <summary>
    /// Answers a body that does not bind in the federation error shape, like every other
    /// refusal on these routes, instead of MVC's validation or media-type problem.
    /// </summary>
    /// <remarks>
    /// Ordered ahead of <c>[ApiController]</c>'s own filters for both (-3000 and -2000),
    /// which would otherwise answer first. The page reads <c>{code, message}</c> from every
    /// error here; a problem-details body reaches it as an error with no code.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class InvalidRequestAttribute : Attribute, IActionFilter, IOrderedFilter
    {
        public int Order => -5000;

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ModelState.IsValid)
            {
                return;
            }

            var errors = context.ModelState.Where(e => e.Value?.Errors.Count > 0).ToList();
            var unsupported = errors.Any(e =>
                e.Value!.Errors.Any(x => x.Exception is UnsupportedContentTypeException));
            var field = errors.Select(e => e.Key).FirstOrDefault(k => !string.IsNullOrEmpty(k));

            context.Result = new ContentResult
            {
                Content = System.Text.Json.JsonSerializer.Serialize(new
                {
                    code = unsupported ? "UnsupportedMediaType" : "InvalidRequest",
                    message = unsupported
                        ? "The request body must be JSON."
                        : field == null
                            ? "The request body could not be read."
                            : $"The request body could not be read ({field}).",
                    retryable = false
                }, FederationJson.Options),
                ContentType = "application/json; charset=utf-8",
                StatusCode = unsupported ? 415 : 400
            };
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
