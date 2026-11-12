using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/acquisition/tasks/recovery/continuations")]
public sealed class AcquisitionContinuationController(AcquisitionContinuationService service) : ControllerBase
{
    private bool Restricted => HttpContext.GetRemoteAccessContext() is not ({IsUnrestricted: true} or {IsPaired: true});
    [HttpPost("preview"), RemoteAccessible, SwaggerOperation(OperationId = "PreviewAcquisitionContinuation")]
    public Task<SingletonResponse<AcquisitionContinuationPreview>> Preview([FromBody] AcquisitionContinuationPreviewInput input) =>
        Result(() => service.PreviewAsync(input, Restricted, HttpContext.RequestAborted));
    [HttpPost("apply"), RemoteAccessible, SwaggerOperation(OperationId = "CreateAcquisitionContinuation")]
    public Task<SingletonResponse<AcquisitionContinuation>> Apply([FromBody] AcquisitionContinuationApplyInput input) =>
        Result(() => service.ApplyAsync(input, Restricted, HttpContext.RequestAborted));
    [HttpGet, RemoteAccessible, SwaggerOperation(OperationId = "GetAcquisitionContinuations")]
    public Task<SingletonResponse<IReadOnlyList<AcquisitionContinuation>>> List() => Result(() => service.ListAsync(HttpContext.RequestAborted));
    [HttpGet("by-preview/{previewToken}"), RemoteAccessible, SwaggerOperation(OperationId = "GetAcquisitionContinuationByPreview")]
    public Task<SingletonResponse<AcquisitionContinuation>> ByPreview(string previewToken) => Result(() => service.GetByPreviewAsync(previewToken, HttpContext.RequestAborted));
    [HttpGet("{batchId}"), RemoteAccessible, SwaggerOperation(OperationId = "GetAcquisitionContinuation")]
    public Task<SingletonResponse<AcquisitionContinuation>> Get(string batchId) => Result(() => service.GetAsync(batchId, HttpContext.RequestAborted));
    [HttpPost("{batchId}/cancel"), RemoteAccessible, SwaggerOperation(OperationId = "CancelAcquisitionContinuation")]
    public Task<SingletonResponse<AcquisitionContinuation>> Cancel(string batchId) => Result(() => service.CancelAsync(batchId, HttpContext.RequestAborted));
    private static async Task<SingletonResponse<T>> Result<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (ArgumentException e) {return SingletonResponseBuilder<T>.BuildBadRequest(e.Message);}
        catch (UnauthorizedAccessException) {return SingletonResponseBuilder<T>.BuildBadRequest("CONTINUATION_PATH_FORBIDDEN");}
        catch (InvalidOperationException) {return SingletonResponseBuilder<T>.BuildBadRequest("RECOVERY_OUTCOME_UNKNOWN");}
    }
}
