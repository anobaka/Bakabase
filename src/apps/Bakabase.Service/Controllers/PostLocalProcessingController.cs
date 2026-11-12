using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/download-intents/from-post/process-local")]
public sealed class PostLocalProcessingController(PostLocalProcessingService service) : ControllerBase
{
    [HttpPost]
    [RemoteAccessible(PathParameters = [nameof(directory)])]
    [SwaggerOperation(OperationId = "StartPostLocalProcessing")]
    public async Task<SingletonResponse<WorkflowRunViewModel>> Start([FromBody] PostLocalProcessingRequest input,
        [FromQuery] string directory, CancellationToken ct)
    {
        try {return new(await service.StartAsync(input, directory, ct));}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<WorkflowRunViewModel>.BuildBadRequest(error.Message);}
    }
}
