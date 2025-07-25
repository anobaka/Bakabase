﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.Storage.Services;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Implementations;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Services;
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
using DownloadTask = Bakabase.InsideWorld.Business.Components.Downloader.Models.Domain.DownloadTask;

namespace Bakabase.Service.Controllers;

[Route("~/download-task")]
public class DownloadTaskController : Controller
{
    private readonly IBOptions<ExHentaiOptions> _exhentaiOptions;
    private readonly IGuiAdapter _guiAdapter;
    private readonly IStringLocalizer<SharedResource> _localizer;
    private readonly DownloadTaskService _service;

    public DownloadTaskController(DownloadTaskService service, IStringLocalizer<SharedResource> localizer,
        IGuiAdapter guiAdapter, IBOptions<ExHentaiOptions> exhentaiOptions)
    {
        _service = service;
        _localizer = localizer;
        _guiAdapter = guiAdapter;
        _exhentaiOptions = exhentaiOptions;
    }

    [SwaggerOperation(OperationId = "GetAllDownloaderNamingDefinitions")]
    [HttpGet("downloader/naming-definitions")]
    public async Task<SingletonResponse<Dictionary<int, DownloaderNamingDefinitions>>>
        GetAllDownloaderNamingDefinitions()
    {
        var dict = new Dictionary<ThirdPartyId, DownloaderNamingDefinitions>
        {
            {
                ThirdPartyId.Bilibili, new DownloaderNamingDefinitions
                {
                    Fields = DownloaderNamingFieldsExtractor<BilibiliNamingFields>.AllFields,
                    DefaultConvention = BilibiliDownloader.DefaultNamingConvention
                }
            },
            {
                ThirdPartyId.ExHentai, new DownloaderNamingDefinitions
                {
                    Fields = DownloaderNamingFieldsExtractor<ExHentaiNamingFields>.AllFields,
                    DefaultConvention = AbstractExHentaiDownloader.DefaultNamingConvention
                }
            },
            {
                ThirdPartyId.Pixiv, new DownloaderNamingDefinitions
                {
                    Fields = DownloaderNamingFieldsExtractor<PixivNamingFields>.AllFields,
                    DefaultConvention = AbstractPixivDownloader.DefaultNamingConvention
                }
            }
        };

        return new SingletonResponse<Dictionary<int, DownloaderNamingDefinitions>>(
            dict.ToDictionary(a => (int) a.Key, a => a.Value));
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

    [HttpPost("exhentai")]
    [SwaggerOperation(OperationId = "AddExHentaiDownloadTask")]
    public async Task<BaseResponse> AddExHentaiTask([FromBody] ExHentaiDownloadTaskAddInputModel model)
    {
        var downloadPath = _exhentaiOptions.Value.Downloader?.DefaultPath;
        if (downloadPath.IsNullOrEmpty()) throw new Exception("Download path for exhentai is not set");

        var baseModel = new DownloadTaskAddInputModel
        {
            Type = (int) model.Type,
            ThirdPartyId = ThirdPartyId.ExHentai,
            KeyAndNames = new Dictionary<string, string> {{model.Link, null}},
            DownloadPath = downloadPath,
            AutoRetry = false
        };


        var rsp = await Add(baseModel);
        if (rsp.Code != 0) return rsp;

        var task = rsp.Data!.First();
        await _service.Start(x => x.Id == task.Id);
        return BaseResponseBuilder.Ok;
    }
}