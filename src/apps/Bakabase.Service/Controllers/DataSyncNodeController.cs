using System.Threading;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// The data sync feed a node holding a <c>datasync.read</c> grant reads: head, snapshot manifest and pages.
/// Only <c>datasync.read</c> grants may reach it once the endpoint scope check lands (§7.3); until then any grant
/// reaches it, and every action answers 501 until the feed lands.
/// </summary>
[ApiController]
[Route("federation/v1/export/datasync")]
[FederationEndpoint(FederationEndpointKind.Export, Scope = FederationScopes.DataSyncRead)]
public sealed class DataSyncNodeController : FederationControllerBase
{
    [HttpGet("head")]
    [SwaggerOperation(OperationId = "GetFederationDataSyncHead")]
    public IActionResult Head([FromQuery] string? mode, [FromQuery] string? since, [FromQuery] string? actor,
        [FromQuery] string? state, CancellationToken ct) => NotAvailableYet();

    [HttpGet("manifest")]
    [SwaggerOperation(OperationId = "CreateFederationDataSyncSnapshot")]
    public IActionResult Manifest([FromQuery] string? mode, [FromQuery] string? since, [FromQuery] string? actor,
        [FromQuery] string? state, CancellationToken ct) => NotAvailableYet();

    [HttpGet("changes")]
    [SwaggerOperation(OperationId = "ReadFederationDataSyncChanges")]
    public IActionResult Changes([FromQuery] string? snapshot, [FromQuery] string? kind, [FromQuery] long? since,
        [FromQuery] string? cursor, CancellationToken ct) => NotAvailableYet();

    private ContentResult NotAvailableYet() => FederationResult(new
    {
        code = "NotImplemented", message = "The data sync feed is not available on this node yet.", retryable = false
    }, 501);
}
