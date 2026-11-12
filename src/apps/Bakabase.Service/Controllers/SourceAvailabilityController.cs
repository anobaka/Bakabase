using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.SourceAvailability;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/source-availability")]
public sealed class SourceAvailabilityController(SourceAvailabilityBatchService service) : ControllerBase
{
    [HttpGet("acquisitions/{id:int}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetAcquisitionSourceAvailability")]
    public Task<SingletonResponse<List<SourceAvailabilityRow>>> Get(int id, CancellationToken ct) => Respond(() => service.GetAsync(id, ct));

    [HttpPost("acquisitions/preview")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "PreviewAcquisitionSourceAvailability")]
    public Task<SingletonResponse<SourceAvailabilityBatchView>> Preview([FromBody] SourceAvailabilityPreviewInput input, CancellationToken ct) => Respond(() => service.PreviewAsync(input, ct));

    [HttpGet("batches/{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetSourceAvailabilityBatch")]
    public Task<SingletonResponse<SourceAvailabilityBatchView?>> Batch(string id, CancellationToken ct) => Respond(() => service.GetBatchAsync(id, ct));

    [HttpPost("acquisitions/apply")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ApplyAcquisitionSourceAvailability")]
    public Task<SingletonResponse<SourceAvailabilityApplyResult>> Apply([FromBody] SourceAvailabilityApplyInput input, CancellationToken ct) => Respond(() => service.ApplyAsync(input, ct));

    private static async Task<SingletonResponse<T>> Respond<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {return SingletonResponseBuilder<T>.BuildBadRequest(error.Message);}
    }
}
