using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Downloader.Models.Tasks;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Server-side task data and file transfers; path arguments are checked by the remote path guard.</summary>
[ApiController]
[Route("~/download-center/tasks")]
public sealed class DownloadCenterController(DownloadCenterService tasks) : ControllerBase
{
    private const int MaximumMetadataBytes = 4 * 1024 * 1024;

    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetDownloadCenterTasks")]
    public async Task<ListResponse<DownloadTask>> GetAll(CancellationToken ct) => new((await tasks.ListAsync(ct)).ToList());

    [HttpGet("{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetDownloadCenterTask")]
    public async Task<SingletonResponse<DownloadTask>> Get(string id, CancellationToken ct)
    {
        var task = await tasks.GetAsync(id, ct);
        return task == null ? SingletonResponseBuilder<DownloadTask>.Build(ResponseCode.NotFound, "The download task does not exist.") : new(task);
    }

    [HttpPost]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "CreateDownloadCenterTask")]
    public Task<SingletonResponse<DownloadTask>> Create([FromQuery] string outputDirectory,
        [FromBody] DownloadCenterCreateInputModel model, CancellationToken ct) =>
        Change(() => tasks.CreateAsync(model, outputDirectory, ct)!);

    [HttpPost("upload")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [RequestSizeLimit(MaximumMetadataBytes + 65536)]
    [SwaggerOperation(OperationId = "UploadDownloadCenterTorrent")]
    public async Task<SingletonResponse<DownloadTask>> Upload(IFormFile file, [FromForm] string outputDirectory,
        [FromForm] string? name, CancellationToken ct)
    {
        if (file.Length is 0 or > MaximumMetadataBytes)
            return SingletonResponseBuilder<DownloadTask>.BuildBadRequest("Choose a torrent file no larger than 4 MiB.");
        await using var stream = file.OpenReadStream();
        using var metadata = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (metadata.Length + read > MaximumMetadataBytes)
                return SingletonResponseBuilder<DownloadTask>.BuildBadRequest("Choose a torrent file no larger than 4 MiB.");
            await metadata.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return await Change(() => tasks.UploadTorrentAsync(metadata.ToArray(), outputDirectory,
            name ?? Path.GetFileNameWithoutExtension(file.FileName), ct)!);
    }

    [HttpPost("{id}/start")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "StartDownloadCenterTask")]
    public Task<SingletonResponse<DownloadTask>> Start(string id, CancellationToken ct,
        [FromQuery] DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.Ignore) =>
        Change(() => tasks.ExecuteAsync(id, "start", ct, actionOnConflict));

    [HttpPost("{id}/stop")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "StopDownloadCenterTask")]
    public Task<SingletonResponse<DownloadTask>> Stop(string id, CancellationToken ct) => Change(() => tasks.ExecuteAsync(id, "stop", ct));

    [HttpPost("{id}/retry")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RetryDownloadCenterTask")]
    public Task<SingletonResponse<DownloadTask>> Retry(string id, CancellationToken ct,
        [FromQuery] DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.Ignore) =>
        Change(() => tasks.ExecuteAsync(id, "retry", ct, actionOnConflict));

    [HttpDelete("{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "DeleteDownloadCenterTask")]
    public async Task<BaseResponse> Delete(string id, CancellationToken ct) => await Change(() => tasks.ExecuteAsync(id, "delete", ct));

    [HttpPost("batch")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "BatchDownloadCenterTasks")]
    public async Task<ListResponse<DownloadTaskActionResult>> Batch([FromBody] DownloadCenterBatchInputModel model, CancellationToken ct)
    {
        try { return new((await tasks.BatchAsync(model, ct)).ToList()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ListResponseBuilder<DownloadTaskActionResult>.BuildBadRequest(ex.Message); }
    }

    private static async Task<SingletonResponse<DownloadTask>> Change(Func<Task<DownloadTask?>> action)
    {
        try { return new(await action()); }
        catch (DownloadCenterCommandException ex)
        { return SingletonResponseBuilder<DownloadTask>.Build((ResponseCode)ex.Code, ex.Message); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return SingletonResponseBuilder<DownloadTask>.BuildBadRequest(ex.Message); }
    }
}
