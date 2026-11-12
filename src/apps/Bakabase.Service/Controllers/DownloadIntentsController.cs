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
[Route("~/download-intents")]
public sealed class DownloadIntentsController(DownloadIntentService intents, PostDownloadIntentAdapter posts) : ControllerBase
{
    [HttpPost("preflight")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "PreflightDownloadIntent")]
    public Task<SingletonResponse<DownloadIntentPreflight>> Preflight([FromBody] DownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct) => Change(() => intents.PreflightAsync(input, outputDirectory, ct));

    [HttpPost("submit")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "SubmitDownloadIntent")]
    public Task<SingletonResponse<DownloadIntentSubmission>> Submit([FromBody] DownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct) => Change(() => intents.SubmitAsync(input, outputDirectory, [], null, ct));

    [HttpPost("from-post/preflight")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "PreflightPostDownloadIntent")]
    public Task<SingletonResponse<DownloadIntentPreflight>> PreflightPost([FromBody] PostDownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct) => Change(() => posts.PreflightAsync(input, outputDirectory, ct));

    [HttpPost("from-post/submit")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "SubmitPostDownloadIntent")]
    public Task<SingletonResponse<DownloadIntentSubmission>> SubmitPost([FromBody] PostDownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct) => Change(() => posts.SubmitAsync(input, outputDirectory, ct));

    private static async Task<SingletonResponse<T>> Change<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<T>.BuildBadRequest(error.Message);}
    }
}
