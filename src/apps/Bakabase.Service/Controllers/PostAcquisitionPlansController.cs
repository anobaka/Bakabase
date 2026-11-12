using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/post-parser/acquisition/plans")]
public sealed class PostAcquisitionPlansController(PostAcquisitionPlanAdapter adapter) : ControllerBase
{
    [HttpPost]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "CreatePostAcquisitionPlan")]
    public async Task<SingletonResponse<DownloadIntentSubmission>> Create([FromBody] PostDownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct)
    {
        try {return new(await adapter.CreateAsync(input, outputDirectory, ct));}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {return SingletonResponseBuilder<DownloadIntentSubmission>.BuildBadRequest(error.Message);}
    }
}
