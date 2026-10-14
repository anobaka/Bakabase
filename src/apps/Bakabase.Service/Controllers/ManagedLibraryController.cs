using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Models.ResponseModels;
using Bakabase.Modules.Federation.Security;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public sealed record ManagedResourceDisplayOptions(UIOptions.UIResourceOptions UiResource,
    PropertyValueScope[] PropertyValueScopePriority);

public sealed record ManagedResourceDisplayOptionsPatch(PropertyValueScope[] PropertyValueScopePriority);

[ApiController]
[Route("management/library")]
public sealed class ManagedLibraryInfoController(ManagedLibraryService service) : ControllerBase
{
    [HttpGet("info")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedLibraryInfo")]
    public Task<ManagedLibraryInfo> Info(CancellationToken ct) => service.InfoAsync(ct);
}

[ApiController]
[Route("federation/local/management")]
[FederationEndpoint(FederationEndpointKind.Local)]
[FederationServerController.InvalidRequest]
public sealed class ManagedLibraryController(ManagedLibraryService service) : FederationControllerBase
{
    [HttpGet("resource-display-options")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetManagedResourceDisplayOptions")]
    public SingletonResponse<ManagedResourceDisplayOptions> ResourceDisplayOptions(
        [FromServices] IBOptionsManager<UIOptions> uiOptions,
        [FromServices] IBOptionsManager<ResourceOptions> resourceOptions) =>
        new(new ManagedResourceDisplayOptions(uiOptions.Value.Resource,
            resourceOptions.Value.PropertyValueScopePriority));

    [HttpPatch("resource-display-options")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PatchManagedResourceDisplayOptions")]
    public async Task<IActionResult> PatchResourceDisplayOptions([FromBody] ManagedResourceDisplayOptionsPatch model,
        [FromServices] IBOptionsManager<ResourceOptions> resourceOptions)
    {
        if (model.PropertyValueScopePriority == null || model.PropertyValueScopePriority.Length > 32 ||
            model.PropertyValueScopePriority.Any(scope => !Enum.IsDefined(scope)))
            return BadRequest(new {code = 400, message = "Choose valid property value sources."});
        await resourceOptions.SaveAsync(options => options.PropertyValueScopePriority = model.PropertyValueScopePriority.Distinct().ToArray());
        return Ok(new {code = 0});
    }

    [HttpGet("sources")]
    [SwaggerOperation(OperationId = "GetManagedLibrarySources")]
    [ProducesResponseType(typeof(ManagedLibrarySources), 200)]
    public async Task<IActionResult> Sources(CancellationToken ct) => FederationResult(await service.SourcesAsync(ct));

    [HttpPost("{serverId}/request")]
    [RequestSizeLimit(ManagedLibraryOperations.MaximumRequestBytes)]
    [SwaggerOperation(OperationId = "RequestManagedLibrary")]
    public async Task<IActionResult> Execute(string serverId, [FromBody] ManagedLibraryRequest request, CancellationToken ct)
    {
        var response = await service.ExecuteAsync(serverId, request, ct);
        return new ContentResult {StatusCode = response.StatusCode, ContentType = "application/json; charset=utf-8",
            Content = response.Body};
    }
    [HttpPost("{serverId}/upload")]
    [RequestSizeLimit(ManagedLibraryOperations.MaximumUploadEnvelopeBytes)]
    [SwaggerOperation(OperationId = "UploadManagedLibraryFile")]
    public async Task<IActionResult> Upload(string serverId, [FromBody] ManagedLibraryRequest request, CancellationToken ct)
    {
        if (request.Operation is not ("uploadFile" or "addResourceAcquisitionTorrent"))
            throw new FederationAccessException("InvalidManagedOperation", 400, "Choose a resource upload operation.");
        return await Execute(serverId, request, ct);
    }

}
