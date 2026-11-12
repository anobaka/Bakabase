using System;
using System.Threading.Tasks;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/{id:int}/storage-override")]
public sealed class AcquisitionStorageOverrideController(AcquisitionStorageOverrideService service, IMediaPathGuard pathGuard) : ControllerBase
{
    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionStorageOverride")]
    public async Task<SingletonResponse<AcquisitionStorageOverridePreview>> Preview(int id, [FromQuery] string scope = "storage")
    {
        try { return new(await service.PreviewAsync(id, HttpContext.RequestAborted, scope)); }
        catch (ArgumentException error) { return SingletonResponseBuilder<AcquisitionStorageOverridePreview>.BuildBadRequest(error.Message); }
    }

    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "ApplyAcquisitionStorageOverride")]
    public async Task<SingletonResponse<AcquisitionStorageOverrideResult>> Apply(int id, [FromBody] AcquisitionStorageOverrideApplyInput input)
    {
        try { return new(await service.ApplyAsync(id, input, HttpContext.RequestAborted, GuardDirectory)); }
        catch (ArgumentException error) { return SingletonResponseBuilder<AcquisitionStorageOverrideResult>.BuildBadRequest(error.Message); }
        catch (UnauthorizedAccessException error) { return SingletonResponseBuilder<AcquisitionStorageOverrideResult>.BuildBadRequest(error.Message); }
        catch (Exception error) when (error is InvalidOperationException or OperationCanceledException)
        { return SingletonResponseBuilder<AcquisitionStorageOverrideResult>.BuildBadRequest("STORAGE_OVERRIDE_OUTCOME_UNKNOWN"); }
    }
    private async Task GuardDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await pathGuard.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("CONTINUATION_PATH_FORBIDDEN");
    }

}
