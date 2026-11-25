using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks/delete")]
public sealed class AcquisitionTaskDeletionController(AcquisitionTaskDeletionService service, IMediaPathGuard paths, IHostApplicationLifetime lifetime) : ControllerBase
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
        try { return new(await service.PreviewAsync(input, HttpContext.RequestAborted, path => Guard(path, HttpContext.RequestAborted))); }
        catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException)
        { return SingletonResponseBuilder<AcquisitionTaskDeletePreview>.BuildBadRequest(error.Message); }
    }

    [HttpPost]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "DeleteAcquisitionTasks")]
    public async Task<SingletonResponse<AcquisitionTaskDeleteResult>> Delete([FromBody] AcquisitionTaskDeleteInput input)
    {
        try
        {
            // A disconnected browser abandons the response. The accepted operation keeps
            // its scoped services and the caller's path guard until the action returns.
            var stopping = lifetime.ApplicationStopping;
            return new(await service.DeleteAsync(input, HttpContext.RequestAborted, path => Guard(path, stopping), stopping));
        }
        catch (Exception error) when (error is ArgumentException or UnauthorizedAccessException)
        { return SingletonResponseBuilder<AcquisitionTaskDeleteResult>.BuildBadRequest(error.Message); }
    }

    [HttpGet("{previewId}/progress")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionTaskDeletionProgress")]
    public SingletonResponse<AcquisitionTaskDeleteProgress> Progress(string previewId)
    {
        // Only progress and confirmed scope are returned. The receipt cannot authorize a replay.
        try { return new(service.GetProgress(previewId)); }
        catch (ArgumentException error)
        { return SingletonResponseBuilder<AcquisitionTaskDeleteProgress>.BuildBadRequest(error.Message); }
    }
    private async Task Guard(string path, CancellationToken ct)
    {
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await paths.IsServableAsync(path, ct))
            throw new UnauthorizedAccessException("CLEANUP_PATH_NOT_AUTHORIZED");
    }
}
