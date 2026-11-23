using System;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks/delete")]
public sealed class AcquisitionTaskDeletionController(AcquisitionTaskDeletionService service, IMediaPathGuard paths) : ControllerBase
{
    [HttpGet("history"), RemoteAccessible, SwaggerOperation(OperationId = "GetDeletedAcquisitionTasks")]
    public async Task<SingletonResponse<AcquisitionDeletedTaskPage>> History(int pageIndex = 1, int pageSize = 25, string filter = "all")
    {
        try { return new(await service.GetDeletedTasksAsync(pageIndex, pageSize, filter, HttpContext.RequestAborted)); }
        catch (ArgumentException error) { return SingletonResponseBuilder<AcquisitionDeletedTaskPage>.BuildBadRequest(error.Message); }
    }

    [HttpPost("preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewDeleteAcquisitionTasks")]
    public async Task<SingletonResponse<AcquisitionTaskDeletePreview>> Preview([FromBody] AcquisitionTaskDeletePreviewInput input)
    {
        try { return new(await service.PreviewAsync(input, HttpContext.RequestAborted, Guard)); }
        catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException)
        { return SingletonResponseBuilder<AcquisitionTaskDeletePreview>.BuildBadRequest(error.Message); }
    }

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "DeleteAcquisitionTasks")]
    public async Task<SingletonResponse<AcquisitionTaskDeleteResult>> Delete([FromBody] AcquisitionTaskDeleteInput input)
    {
        try { return new(await service.DeleteAsync(input, HttpContext.RequestAborted, Guard)); }
        catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException)
        { return SingletonResponseBuilder<AcquisitionTaskDeleteResult>.BuildBadRequest(error.Message); }
    }

    [HttpGet("{previewId}/progress")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionTaskDeletionProgress")]
    public SingletonResponse<AcquisitionTaskDeleteProgress> Progress(string previewId)
    {
        // Only counters and task identifiers are returned, never file paths or cleanup permissions.
        try { return new(service.GetProgress(previewId)); }
        catch (ArgumentException error)
        { return SingletonResponseBuilder<AcquisitionTaskDeleteProgress>.BuildBadRequest(error.Message); }
    }
    private async Task Guard(string path)
    {
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await paths.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("CLEANUP_PATH_NOT_AUTHORIZED");
    }
}
