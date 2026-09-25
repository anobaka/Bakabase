using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Bakabase.Service.Components.Federation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// Pairing for definitions (<c>datasync.read</c>, §7.2.1). Its own routes rather than a scope field on the library's,
/// so an older node refuses them (<c>NodeRouteForbidden</c>) instead of filing a library request. The gate lets them
/// through only while definitions sharing is on, and the service checks that switch again.
/// </summary>
[ApiController]
[Route("federation/v1/pair/datasync")]
[FederationEndpoint(FederationEndpointKind.Public)]
public sealed class FederationDataSyncPairingController(FederationPeerService peers,
    NodePairingRateLimiter rateLimiter, FederationPairingFlow flow, ILogger<FederationDataSyncPairingController> logger)
    : FederationControllerBase
{
    [HttpPost("request")]
    [SwaggerOperation(OperationId = "RequestFederationDataSyncPairing")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairRequest([FromBody] NodeDataSyncPairRequest request, CancellationToken ct)
    {
        CheckRate();
        var (exchange, created) = await peers.SubmitDataSyncRequestAsync(request, RemoteAddress, ct);
        if (created) await NotifyAsync(request.NodeName);
        return FederationResult(exchange);
    }

    /// <summary>
    /// A datasync code grants at once. When the code's creator agreed to two-way and the redeemer asked for it, this
    /// device reads the redeemer back (<c>ReadBack = "started"</c>), as a library code does (F68): in the background,
    /// after data sync has heard of the grant, so the creator's link exists before it hears how the read-back went.
    /// </summary>
    [HttpPost("code")]
    [SwaggerOperation(OperationId = "ExchangeFederationDataSyncInvitation")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairCode([FromBody] NodeDataSyncPairCodeRequest request, CancellationToken ct)
    {
        CheckRate();
        var (exchange, issued, intent) = await peers.ExchangeDataSyncCodeAsync(request, RemoteAddress, ct);
        if (issued && exchange.Outcome == "granted")
        {
            var readBack = exchange.ReadBack == NodeDataSyncReadBack.Started;
            flow.RaiseInboundGranted(request.NodeId,
                intent == NodeDataSyncIntents.TwoWay ? DataSyncRequestIntent.TwoWay : DataSyncRequestIntent.Follow,
                readBack);
            if (readBack) flow.ReadBackDataSync(request.NodeId);
        }
        return FederationResult(exchange);
    }

    [HttpPost("claim")]
    [SwaggerOperation(OperationId = "ClaimFederationDataSyncNodeGrant")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public async Task<IActionResult> PairClaim([FromBody] NodePairClaimRequest request, CancellationToken ct) =>
        FederationResult(await peers.ClaimDataSyncAsync(request, ct));

    /// <summary>
    /// Approval needs a person at this device while the request is fresh, so a desktop app says so; a headless server
    /// has nobody to tell (§9.4) and its requests wait in the CLI and in any window that manages it.
    /// </summary>
    private async Task NotifyAsync(string nodeName)
    {
        var services = HttpContext.RequestServices;
        if (services.GetService<IDataSyncHostKind>() is { IsHeadless: true }) return;
        try
        {
            var localizer = services.GetRequiredService<IBakabaseLocalizer>();
            await services.GetRequiredService<INotificationService>().CreateAsync(new NotificationCreationInputModel
            {
                Source = "DataSync",
                Title = localizer["DataSync_Request_Title", nodeName.Trim()],
                Body = localizer["DataSync_Request_Body", RemoteAddress ?? "?"],
                PayloadJson = JsonSerializer.Serialize(new { route = "/data-sync?tab=requests" }),
                Severity = AppNotificationSeverity.Warning
            });
        }
        catch (Exception e)
        {
            // A notification is a convenience; the request itself is stored.
            logger.LogDebug(e, "The notification for a definitions request could not be created");
        }
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
