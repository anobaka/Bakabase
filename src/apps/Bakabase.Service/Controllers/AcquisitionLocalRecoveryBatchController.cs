using System;
using System.Collections.Generic;
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
[Route("~/acquisition/tasks/local-recovery-batches")]
public sealed class AcquisitionLocalRecoveryBatchController(AcquisitionLocalRecoveryBatchService service, IMediaPathGuard pathGuard) : ControllerBase
{
    private bool Restricted => HttpContext.GetRemoteAccessContext() is not ({IsUnrestricted: true} or {IsPaired: true});
    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionLocalRecoveryBatch")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatchPreview>> Preview([FromBody] AcquisitionLocalRecoveryBatchPreviewInput input) =>
        Result(() => service.PreviewAsync(input, HttpContext.RequestAborted, GuardDirectory, Restricted));
    [HttpPost("capacity"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionLocalRecoveryBatchCapacity")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatchCapacityPreview>> Capacity([FromBody] AcquisitionLocalRecoveryBatchCapacityInput input) =>
        Result(() => service.PreviewCapacityAsync(input, HttpContext.RequestAborted, GuardDirectory));
    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "CreateAcquisitionLocalRecoveryBatch")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatch>> Apply([FromBody] AcquisitionLocalRecoveryBatchApplyInput input) =>
        Result(() => service.ApplyAsync(input, HttpContext.RequestAborted, GuardDirectory, Restricted));
    [HttpGet, RemoteAccessible, SwaggerOperation(OperationId = "GetAcquisitionLocalRecoveryBatches")]
    public Task<SingletonResponse<IReadOnlyList<AcquisitionLocalRecoveryBatch>>> List() => Result(() => service.ListAsync(HttpContext.RequestAborted));
    [HttpGet("{batchId}"), RemoteAccessible, SwaggerOperation(OperationId = "GetAcquisitionLocalRecoveryBatch")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatch>> Get(string batchId) => Result(() => service.GetAsync(batchId, HttpContext.RequestAborted));
    [HttpPost("{batchId}/pause"), RemoteAccessible, SwaggerOperation(OperationId = "PauseAcquisitionLocalRecoveryBatch")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatch>> Pause(string batchId) => Result(() => service.CommandAsync(batchId, "pause", HttpContext.RequestAborted));
    [HttpPost("{batchId}/resume"), RemoteAccessible, SwaggerOperation(OperationId = "ResumeAcquisitionLocalRecoveryBatch")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatch>> Resume(string batchId) => Result(() => service.CommandAsync(batchId, "resume", HttpContext.RequestAborted, GuardDirectory));
    [HttpPost("{batchId}/cancel"), RemoteAccessible, SwaggerOperation(OperationId = "CancelAcquisitionLocalRecoveryBatch")]
    public Task<SingletonResponse<AcquisitionLocalRecoveryBatch>> Cancel(string batchId) => Result(() => service.CommandAsync(batchId, "cancel", HttpContext.RequestAborted));
    private static async Task<SingletonResponse<T>> Result<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (ArgumentException e) {return SingletonResponseBuilder<T>.BuildBadRequest(e.Message);}
        catch (UnauthorizedAccessException) {return SingletonResponseBuilder<T>.BuildBadRequest("LOCAL_BATCH_PATH_FORBIDDEN");}
        catch (InvalidOperationException) {return SingletonResponseBuilder<T>.BuildBadRequest("LOCAL_BATCH_OUTCOME_UNKNOWN");}
    }
    private async Task GuardDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var remote = HttpContext.GetRemoteAccessContext();
        if (remote is {IsUnrestricted: true} or {IsPaired: true}) return;
        if (!await pathGuard.IsServableAsync(path, HttpContext.RequestAborted))
            throw new UnauthorizedAccessException("LOCAL_BATCH_PATH_FORBIDDEN");
    }
}
