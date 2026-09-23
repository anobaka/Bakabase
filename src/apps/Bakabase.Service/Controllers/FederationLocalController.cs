using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("federation/local/queries")]
[FederationEndpoint(FederationEndpointKind.Local)]
public sealed class FederationLocalController(FederatedQueryCoordinator coordinator) : FederationControllerBase
{
    private const string Owner = "local-ui";

    [HttpPost]
    [SwaggerOperation(OperationId = "CreateFederatedLibraryQuery")]
    [ProducesResponseType(typeof(FederatedQueryPage), 200)]
    public async Task<IActionResult> Create([FromBody] LocalFederatedQuery request, CancellationToken ct) =>
        FederationResult(await coordinator.CreateAsync(Owner, request, ct));

    [HttpGet("{id}/pages")]
    [SwaggerOperation(OperationId = "ReadFederatedLibraryQuery")]
    [ProducesResponseType(typeof(FederatedQueryPage), 200)]
    public async Task<IActionResult> Read(string id, [FromQuery] string cursor, CancellationToken ct) =>
        FederationResult(await coordinator.ReadAsync(Owner, id, cursor, ct));

    [HttpDelete("{id}")]
    [SwaggerOperation(OperationId = "ReleaseFederatedLibraryQuery")]
    public async Task<IActionResult> Release(string id, CancellationToken ct)
    {
        await coordinator.ReleaseAsync(Owner, id, ct);
        return NoContent();
    }
}
