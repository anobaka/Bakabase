using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Components.Federation;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("federation/v1/export")]
[FederationEndpoint(FederationEndpointKind.Export)]
public sealed class FederationExportController(LocalSearchSnapshotService snapshots,
    FederationResourceService resources, GrantLeaseRegistry leases) : FederationControllerBase
{
    private string GrantId => FederationHttpContext.GetNodePrincipal(HttpContext)?.GrantId ??
                              throw new FederationQueryException("NodeAuthenticationRequired", 401);

    [HttpPost("queries")]
    [SwaggerOperation(OperationId = "CreateFederationExportQuery")]
    [ProducesResponseType(typeof(NodeQueryBlock), 200)]
    public async Task<IActionResult> Create([FromBody] NodeExportQuery request, CancellationToken ct) =>
        FederationResult(await snapshots.CreateAsync(GrantId, request, ct));

    [HttpGet("queries/{id}/pages")]
    [SwaggerOperation(OperationId = "ReadFederationExportQuery")]
    [ProducesResponseType(typeof(NodeQueryBlock), 200)]
    public async Task<IActionResult> Read(string id, [FromQuery] string cursor, CancellationToken ct) =>
        FederationResult(await snapshots.ReadAsync(GrantId, id, cursor, ct));

    [HttpPost("queries/{id}/validate")]
    [SwaggerOperation(OperationId = "ValidateFederationExportQuery")]
    public async Task<IActionResult> Validate(string id, CancellationToken ct)
    {
        await snapshots.ValidateAsync(GrantId, id, ct);
        return NoContent();
    }

    [HttpDelete("queries/{id}")]
    [SwaggerOperation(OperationId = "ReleaseFederationExportQuery")]
    public async Task<IActionResult> Release(string id, CancellationToken ct)
    {
        await snapshots.ReleaseAsync(GrantId, id, ct);
        return NoContent();
    }

    [HttpPost("resources/resolve")]
    [SwaggerOperation(OperationId = "ResolveFederationExportResources")]
    [ProducesResponseType(typeof(ResourceResolveResponse), 200)]
    public async Task<IActionResult> Resolve([FromBody] ResourceResolveRequest request, CancellationToken ct) =>
        FederationResult(await resources.ResolveAsync(GrantId, request, ct));

    [HttpPost("resources/location")]
    [SwaggerOperation(OperationId = "LocateFederationExportResource")]
    [ProducesResponseType(typeof(ResourceLocationResponse), 200)]
    public async Task<IActionResult> Location([FromBody] ResourceLocationRequest request, CancellationToken ct) =>
        FederationResult((await resources.GetLocationAsync(GrantId, request.ResourceRef, ct)).Response);

    [HttpGet("mapping-roots")]
    [SwaggerOperation(OperationId = "GetFederationExportMappingRoots")]
    [ProducesResponseType(typeof(MappingRoot[]), 200)]
    public async Task<IActionResult> MappingRoots(CancellationToken ct) =>
        FederationResult(await resources.GetMappingRootsAsync(ct));

    [HttpGet("assets/{assetId}")]
    [SwaggerOperation(OperationId = "ReadFederationExportAsset")]
    public async Task<IActionResult> Asset(string assetId, CancellationToken ct)
    {
        var asset = await resources.OpenAsync(GrantId, assetId, ct);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, leases.GetCancellationToken(GrantId));
        HttpContext.RequestAborted = linked.Token;
        Response.RegisterForDispose(linked);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; media-src 'self'; img-src 'self'";
        return PhysicalFile(asset.Path, asset.ContentType, enableRangeProcessing: true);
    }

    [HttpHead("assets/{assetId}")]
    [SwaggerOperation(OperationId = "InspectFederationExportAsset")]
    public Task<IActionResult> InspectAsset(string assetId, CancellationToken ct) => Asset(assetId, ct);
}
