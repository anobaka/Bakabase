using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Presets.Abstractions;
using Bakabase.Modules.Presets.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using System.Text.Json;
using System.Threading;
using Bakabase.Abstractions.Components.Tracing;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/media-library-template")]
[Obsolete]
public class MediaLibraryTemplateController(
    IMediaLibraryTemplateService service,
    IPresetsService presetsService) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllMediaLibraryTemplates")]
    public async Task<ListResponse<Abstractions.Models.Domain.MediaLibraryTemplate>> GetAll(
        MediaLibraryTemplateAdditionalItem additionalItems = MediaLibraryTemplateAdditionalItem.None)
    {
        var templates = await service.GetAll(additionalItems);
        return new ListResponse<Abstractions.Models.Domain.MediaLibraryTemplate>(templates);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetMediaLibraryTemplate")]
    public async Task<SingletonResponse<Abstractions.Models.Domain.MediaLibraryTemplate>> Get(int id,
        MediaLibraryTemplateAdditionalItem additionalItems = MediaLibraryTemplateAdditionalItem.None)
    {
        var template = await service.Get(id, additionalItems);
        return new SingletonResponse<Abstractions.Models.Domain.MediaLibraryTemplate>(template);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddMediaLibraryTemplate")]
    public async Task<SingletonResponse<int>> Add([FromBody] MediaLibraryTemplateAddInputModel model)
    {
        var data = await service.Add(model);
        return new SingletonResponse<int>(data: data.Id);
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "PutMediaLibraryTemplate")]
    public async Task<BaseResponse> Put(int id, [FromBody] Abstractions.Models.Domain.MediaLibraryTemplate template)
    {
        await service.Put(id, template);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteMediaLibraryTemplate")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("{id:int}/share-text")]
    [SwaggerOperation(OperationId = "GetMediaLibraryTemplateShareCode")]
    public async Task<SingletonResponse<string>> GetShareCode(int id)
    {
        var text = await service.GenerateShareCode(id);
        return new SingletonResponse<string>(text);
    }

    [HttpPost("share-code/import-configuration")]
    [SwaggerOperation(OperationId = "GetMediaLibraryTemplateImportConfiguration")]
    public async Task<SingletonResponse<MediaLibraryTemplateImportConfigurationViewModel>> GetImportConfiguration(
        [FromBody] string shareCode)
    {
        var r = await service.GetImportConfiguration(shareCode);
        return new SingletonResponse<MediaLibraryTemplateImportConfigurationViewModel>(r);
    }

    [HttpPost("share-code/import")]
    [SwaggerOperation(OperationId = "ImportMediaLibraryTemplate")]
    public async Task<SingletonResponse<int>> Import(
        [FromBody] MediaLibraryTemplateImportInputModel model)
    {
        return new(data: await service.Import(model));
    }

    // [HttpPut("{id:int}/share-png/code")]
    // [SwaggerOperation(OperationId = "AppendMediaLibraryTemplateShareCodeToPng")]
    // public async Task<IActionResult> AppendShareCodeToPng(int id)
    // {
    //     var code = await service.GenerateShareCode(id);
    //     throw new NotImplementedException();
    // }

    [HttpPost("by-media-library-v1")]
    [SwaggerOperation(OperationId = "AddMediaLibraryTemplateByMediaLibraryV1")]
    public async Task<BaseResponse> AddByMediaLibraryV1(
        [FromBody] MediaLibraryTemplateAddByMediaLibraryV1InputModel model)
    {
        await service.AddByMediaLibraryV1(model.V1Id, model.PcIdx, model.Name);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/duplicate")]
    [SwaggerOperation(OperationId = "DuplicateMediaLibraryTemplate")]
    public async Task<BaseResponse> Duplicate(int id)
    {
        await service.Duplicate(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("preset-data-pool")]
    [SwaggerOperation(OperationId = "GetMediaLibraryTemplatePresetDataPool")]
    public async Task<SingletonResponse<MediaLibraryTemplatePresetDataPool>> GetPresetDataPool()
    {
        return new(presetsService.GetMediaLibraryTemplatePresetDataPool());
    }

    [HttpPost("from-preset-builder")]
    [SwaggerOperation(OperationId = "AddMediaLibraryTemplateFromPresetBuilder")]
    public async Task<SingletonResponse<int>> AddFromPresetBuilder([FromBody] MediaLibraryTemplateCompactBuilder model)
    {
        return new(data: await presetsService.AddMediaLibrary(model));
    }

    [HttpPost("{id:int}/validate")]
    [SwaggerOperation(OperationId = "ValidateMediaLibraryTemplate")]
    public async Task<IActionResult> Validate(int id, [FromBody] MediaLibraryTemplateValidationInputModel model,
        [FromServices] BakaTracingContext tracingContext)
    {
        Response.Headers.ContentType = "application/x-ndjson";
        var validationTask = Task.Run(async () =>
        {
            try
            {
                await service.Validate(id, model, HttpContext.RequestAborted);
            }
            finally
            {
                tracingContext.Complete();
            }
        });

        await foreach (var trace in tracingContext.ReadTracesAsync())
        {
            var json = JsonSerializer.Serialize(trace, JsonSerializerOptions.Web);
            await Response.WriteAsync(json + "\n", HttpContext.RequestAborted);
            await Response.Body.FlushAsync(HttpContext.RequestAborted);
        }

        await validationTask;
        return new EmptyResult();
    }
}