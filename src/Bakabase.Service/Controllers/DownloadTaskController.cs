using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Swashbuckle.AspNetCore.Annotations;
using DownloadTask = Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.DownloadTask;

namespace Bakabase.Service.Controllers;

[Route("~/download-task")]
public class DownloadTaskController : Controller
{
    private readonly IBOptions<ExHentaiOptions> _exhentaiOptions;
    private readonly IGuiAdapter _guiAdapter;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly DownloadTaskService _service;
    private readonly IDownloaderFactory _downloaderFactory;

    public DownloadTaskController(DownloadTaskService service, IStringLocalizer<SharedResource> localizer,
        IGuiAdapter guiAdapter, IBOptions<ExHentaiOptions> exhentaiOptions,
        IDownloaderFactory downloaderFactory)
    {
        _service = service;
        _localizer = localizer;
        _guiAdapter = guiAdapter;
        _exhentaiOptions = exhentaiOptions;
        _downloaderFactory = downloaderFactory;
    }

    [SwaggerOperation(OperationId = "GetAllDownloaderDefinitions")]
    [HttpGet("downloaders/definitions")]
    public async Task<ListResponse<DownloaderDefinition>>
        GetAllDownloaderDefinitions()
    {
        return new ListResponse<DownloaderDefinition>(_downloaderFactory.GetDefinitions());
    }

    [SwaggerOperation(OperationId = "GetAllDownloadTasks")]
    [HttpGet]
    public async Task<ListResponse<DownloadTask>> GetAll()
    {
        return new ListResponse<DownloadTask>(await _service.GetAllDto());
    }

    [SwaggerOperation(OperationId = "GetDownloadTask")]
    [HttpGet("{id}")]
    public async Task<SingletonResponse<DownloadTask>> Get(int id)
    {
        return new SingletonResponse<DownloadTask>(await _service.GetDto(id));
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddDownloadTask")]
    public async Task<ListResponse<DownloadTaskDbModel>> Add([FromBody] DownloadTaskAddInputModel model)
    {
        var taskResult = model.AddTasks(_localizer);
        if (taskResult.Code != 0) return taskResult;


        var tasks = taskResult.Data;
        if (tasks.Any())
        {
            if (!model.IsDuplicateAllowed)
            {
                var existedTasks = await _service.GetAllDto();
                var similarTasks = tasks.ToDictionary(a => a, a =>
                {
                    var sts = existedTasks.Where(c =>
                            c.ThirdPartyId == a.ThirdPartyId && c.Type == a.Type && c.Key == a.Key)
                        .ToArray();
                    return string.Join(',', sts.Select(c => c.Name ?? c.Key));
                }).Where(a => a.Value.IsNotEmpty()).ToDictionary(a => a.Key.Name ?? a.Key.Key, a => a.Value);
                if (similarTasks.Any())
                    return ListResponseBuilder<DownloadTaskDbModel>.Build(ResponseCode.Conflict,
                        _localizer[SharedResource.Downloader_MayBeDuplicate,
                            string.Join(Environment.NewLine, similarTasks.Select(a => $"{a.Key}: {a.Value}"))]);
            }

            return await _service.AddRange(tasks);
        }

        return ListResponseBuilder<DownloadTaskDbModel>.NotModified;
    }

    [HttpDelete("{id}")]
    [SwaggerOperation(OperationId = "DeleteDownloadTask")]
    public async Task<BaseResponse> Delete(int id)
    {
        return await Delete(new DownloadTaskDeleteInputModel([id]));
    }

    [HttpDelete]
    [SwaggerOperation(OperationId = "DeleteDownloadTasks")]
    public async Task<BaseResponse> Delete([FromBody] DownloadTaskDeleteInputModel model)
    {
        return await _service.Delete(model);
    }

    [HttpPut("{id}")]
    [SwaggerOperation(OperationId = "PutDownloadTask")]
    public async Task<BaseResponse> Put(int id, [FromBody] DownloadTaskPutInputModel task)
    {
        return await _service.StopAndUpdateByKey(id, t =>
        {
            t.Interval = task.Interval;
            t.Checkpoint = task.Checkpoint;
            t.StartPage = task.StartPage;
            t.EndPage = task.EndPage;
            t.AutoRetry = task.AutoRetry;
        });
    }

    [HttpPost("download")]
    [SwaggerOperation(OperationId = "StartDownloadTasks")]
    public async Task<BaseResponse> StartAll([FromBody] DownloadTaskStartRequestModel model)
    {
        return await _service.Start(model.Ids.Any() ? t => model.Ids.Contains(t.Id) : null, model.ActionOnConflict);
    }

    [HttpDelete("download")]
    [SwaggerOperation(OperationId = "StopDownloadTasks")]
    public async Task<BaseResponse> StopAll([FromBody] int[] ids)
    {
        await _service.Stop(ids.Any() ? t => ids.Contains(t.Id) : null);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("xlsx")]
    [SwaggerOperation(OperationId = "ExportAllDownloadTasks")]
    public async Task<BaseResponse> Export()
    {
        await _service.Export();
        FileService.Open(_guiAdapter.GetDownloadsDirectory(), false);
        return BaseResponseBuilder.Ok;
    }

    [SwaggerOperation(OperationId = "GetDownloaderOptions")]
    [HttpGet("downloader/options/{thirdPartyId}")]
    public async Task<SingletonResponse<DownloaderOptions>> GetDownloaderOptions(ThirdPartyId thirdPartyId, int taskType)
    {
        return new SingletonResponse<DownloaderOptions>(await _downloaderFactory.GetHelper(thirdPartyId, taskType)
            .GetOptionsAsync());
    }

    [SwaggerOperation(OperationId = "PutDownloaderOptions")]
    [HttpPut("downloader/options/{thirdPartyId}")]
    public async Task<BaseResponse> PutDownloaderOptions(ThirdPartyId thirdPartyId, int taskType, [FromBody] DownloaderOptions options)
    {
        var helper = _downloaderFactory.GetHelper(thirdPartyId, taskType);
        await helper.PutOptionsAsync(options);
        return BaseResponseBuilder.Ok;
    }
}