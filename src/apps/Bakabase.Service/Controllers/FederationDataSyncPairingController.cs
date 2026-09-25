using System.Threading;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>
/// Pairing for definitions (<c>datasync.read</c>). Its own routes rather than a scope field on the library's,
/// so an older node refuses them (<c>NodeRouteForbidden</c>) instead of filing a library request.
/// Not wired yet: every action answers 501 until the datasync pairing flow lands.
/// </summary>
[ApiController]
[Route("federation/v1/pair/datasync")]
[FederationEndpoint(FederationEndpointKind.Public)]
public sealed class FederationDataSyncPairingController : FederationControllerBase
{
    [HttpPost("request")]
    [SwaggerOperation(OperationId = "RequestFederationDataSyncPairing")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public IActionResult PairRequest([FromBody] NodeDataSyncPairRequest request, CancellationToken ct) =>
        NotAvailableYet();

    [HttpPost("code")]
    [SwaggerOperation(OperationId = "ExchangeFederationDataSyncInvitation")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public IActionResult PairCode([FromBody] NodeDataSyncPairCodeRequest request, CancellationToken ct) =>
        NotAvailableYet();

    [HttpPost("claim")]
    [SwaggerOperation(OperationId = "ClaimFederationDataSyncNodeGrant")]
    [ProducesResponseType(typeof(NodePairExchange), 200)]
    public IActionResult PairClaim([FromBody] NodePairClaimRequest request, CancellationToken ct) =>
        NotAvailableYet();

    private ContentResult NotAvailableYet() => FederationResult(new
    {
        code = "NotImplemented", message = "Definitions pairing is not available on this node yet.", retryable = false
    }, 501);
}
