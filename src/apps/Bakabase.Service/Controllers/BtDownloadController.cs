using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Components.Downloader;
using Bakabase.Modules.Downloader.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Server-side BitTorrent transfers; output paths are explicitly declared for remote path guards.</summary>
[ApiController]
[Route("~/bt-downloads")]
public sealed class BtDownloadController(IBtDownloadManager downloads, IDownloadTaskRegistry registry,
    DownloadCenterService center) : ControllerBase
{
    private const int MaximumMetadataBytes = 4 * 1024 * 1024;

    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetBtDownloads")]
    public async Task<ListResponse<BtDownloadJob>> GetAll(CancellationToken ct)
        => new((await downloads.ListAsync(ct)).ToList());

    [HttpGet("{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetBtDownload")]
    public async Task<SingletonResponse<BtDownloadJob>> Get(string id, CancellationToken ct)
    {
        var job = await downloads.GetAsync(id, ct);
        return job == null
            ? SingletonResponseBuilder<BtDownloadJob>.BuildBadRequest("The BitTorrent download does not exist.")
            : new SingletonResponse<BtDownloadJob>(job);
    }

    [HttpPost]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [SwaggerOperation(OperationId = "CreateBtDownload")]
    public Task<SingletonResponse<BtDownloadJob>> Create([FromQuery] string outputDirectory,
        [FromBody] BtDownloadCreateInputModel model, CancellationToken ct)
        => Change(() => CreateBoundAsync(new BtDownloadRequest
        {
            Name = model.Name, MagnetUri = model.MagnetUri, TorrentUrl = model.TorrentUrl,
            OutputDirectory = outputDirectory
        }, ct));

    [HttpPost("upload")]
    [RemoteAccessible(PathParameters = [nameof(outputDirectory)])]
    [RequestSizeLimit(MaximumMetadataBytes + 65536)]
    [SwaggerOperation(OperationId = "UploadBtDownload")]
    public async Task<SingletonResponse<BtDownloadJob>> Upload(IFormFile file, [FromForm] string outputDirectory,
        [FromForm] string? name, CancellationToken ct)
    {
        if (file.Length is 0 or > MaximumMetadataBytes)
            return SingletonResponseBuilder<BtDownloadJob>.BuildBadRequest("Choose a torrent file no larger than 4 MiB.");
        await using var stream = file.OpenReadStream();
        using var metadata = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (metadata.Length + read > MaximumMetadataBytes)
                return SingletonResponseBuilder<BtDownloadJob>.BuildBadRequest("Choose a torrent file no larger than 4 MiB.");
            await metadata.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return await Change(() => CreateBoundAsync(new BtDownloadRequest
        {
            Name = name ?? Path.GetFileNameWithoutExtension(file.FileName), Metadata = metadata.ToArray(),
            OutputDirectory = outputDirectory
        }, ct));
    }

    [HttpPost("{id}/stop")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "StopBtDownload")]
    public Task<SingletonResponse<BtDownloadJob>> Stop(string id, CancellationToken ct)
        => Change(() => ExecuteAsync(id, "stop", ct));

    [HttpPost("{id}/resume")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "ResumeBtDownload")]
    public Task<SingletonResponse<BtDownloadJob>> Resume(string id, CancellationToken ct)
        => Change(() => ExecuteAsync(id, "start", ct));

    [HttpPost("{id}/retry")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RetryBtDownload")]
    public Task<SingletonResponse<BtDownloadJob>> Retry(string id, CancellationToken ct)
        => Change(() => ExecuteAsync(id, "retry", ct));

    [HttpDelete("{id}")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "DeleteBtDownload")]
    public async Task<BaseResponse> Delete(string id, CancellationToken ct)
    {
        try
        {
            await center.ExecuteAsync("bt:" + id, "delete", ct);
            return BaseResponseBuilder.Ok;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return BaseResponseBuilder.BuildBadRequest(ex.Message); }
    }

    private async Task<BtDownloadJob> CreateBoundAsync(BtDownloadRequest request, CancellationToken ct)
    {
        var job = await downloads.EnsureQueuedAsync(request, ct);
        await registry.EnsureBindingAsync(DownloadCenterBindings.For(job), ct);
        return job;
    }

    private async Task<BtDownloadJob> ExecuteAsync(string id, string action, CancellationToken ct)
    {
        await center.ExecuteAsync("bt:" + id, action, ct);
        return (await downloads.GetAsync(id, ct))!;
    }

    private static async Task<SingletonResponse<BtDownloadJob>> Change(Func<Task<BtDownloadJob>> action)
    {
        try { return new SingletonResponse<BtDownloadJob>(await action()); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return SingletonResponseBuilder<BtDownloadJob>.BuildBadRequest(ex.Message); }
    }
}
