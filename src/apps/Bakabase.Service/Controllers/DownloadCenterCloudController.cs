using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components.DownloadIntents;
using Bakabase.Modules.Downloader.Cloud;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.RemoteAccess;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Download-center intake reuses source preflight without exposing acquisition or post-processing goals.</summary>
[ApiController]
[Route("~/download-center/cloud")]
public sealed class DownloadCenterCloudController(DownloadIntentService intents) : ControllerBase
{
    [HttpPost("preflight")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "PreflightDownloadCenterCloud")]
    public Task<SingletonResponse<DownloadIntentPreflight>> Preflight([FromBody] DownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct) =>
        Change(() => intents.PreflightDownloadsOnlyAsync(input, outputDirectory, ct));

    [HttpPost("tasks")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "CreateDownloadCenterCloudTasks")]
    public Task<SingletonResponse<DownloadIntentSubmission>> Create([FromBody] DownloadIntentRequest input,
        [FromQuery] string? outputDirectory, CancellationToken ct) =>
        Change(() => intents.SubmitDownloadsOnlyAsync(input, outputDirectory, ct));

    [HttpGet("{taskId:int}/owned-copy")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetCloudDownloadOwnedCopy")]
    public Task<SingletonResponse<CloudDownloadOwnedCopyView>> OwnedCopy(int taskId,
        [FromServices] CloudDownloadOwnedCopyService copies, CancellationToken ct) => Change(() => copies.GetAsync(taskId, ct));

    [HttpPost("{taskId:int}/owned-copy/retry")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RetryCloudDownloadOwnedCopy")]
    public Task<SingletonResponse<CloudDownloadOwnedCopyView>> RetryOwnedCopy(int taskId,
        [FromServices] CloudDownloadOwnedCopyService copies, CancellationToken ct) => Change(() => copies.RetryAsync(taskId, ct));

    private static async Task<SingletonResponse<T>> Change<T>(Func<Task<T>> action)
    {
        try {return new(await action());}
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {return SingletonResponseBuilder<T>.BuildBadRequest(error.Message);}
    }
}
