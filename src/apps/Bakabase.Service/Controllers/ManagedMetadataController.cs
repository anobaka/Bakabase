using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.Federation.MetadataReplicas;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

// Requires ordinary administrator access. Read-only federation grants cannot call these routes.
[ApiController]
[Route("management/library/metadata")]
public sealed class ManagedMetadataController(MetadataSourceFeed feed) : ControllerBase
{
    [HttpGet("head")]
    [SwaggerOperation(OperationId = "GetManagedMetadataHead")]
    public Task<MetadataSourceManifest> Head(CancellationToken ct) => feed.ManifestAsync(ct);

    [HttpPost("page")]
    [RequestSizeLimit(8192)]
    [SwaggerOperation(OperationId = "GetManagedMetadataPage")]
    public async Task<IActionResult> Page([FromBody] MetadataPageRequest request, CancellationToken ct)
    {
        try { return Ok(await feed.PageAsync(request, ct)); }
        catch (ReplicaBudgetExceededException e)
        { return StatusCode(413, new {code = "MetadataPageTooLarge", message = e.Message}); }
    }
}
