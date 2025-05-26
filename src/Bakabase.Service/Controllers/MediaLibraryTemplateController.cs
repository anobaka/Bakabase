using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Input;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.View;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/media-library-template")]
public class MediaLibraryTemplateController(IMediaLibraryTemplateService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllMediaLibraryTemplates")]
    public async Task<ListResponse<MediaLibraryTemplate>> GetAll()
    {
        var templates = await service.GetAll();
        return new ListResponse<MediaLibraryTemplate>(templates);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetMediaLibraryTemplate")]
    public async Task<SingletonResponse<MediaLibraryTemplate>> Get(int id)
    {
        var template = await service.Get(id);
        return new SingletonResponse<MediaLibraryTemplate>(template);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddMediaLibraryTemplate")]
    public async Task<BaseResponse> Add([FromBody] MediaLibraryTemplateAddInputModel model)
    {
        await service.Add(model);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "PutMediaLibraryTemplate")]
    public async Task<BaseResponse> Put(int id, [FromBody] MediaLibraryTemplate template)
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

    [HttpPost("share-code/validate")]
    [SwaggerOperation(OperationId = "ValidateMediaLibraryTemplateShareCode")]
    public async Task<SingletonResponse<MediaLibraryTemplateValidationViewModel>> ValidateShareCode(
        [FromBody] string shareCode)
    {
        var r = await service.Validate(shareCode);
        return new SingletonResponse<MediaLibraryTemplateValidationViewModel>(r);
    }

    [HttpPost("share-code/import")]
    [SwaggerOperation(OperationId = "ImportMediaLibraryTemplate")]
    public async Task<BaseResponse> Import(
        [FromBody] MediaLibraryTemplateImportInputModel model)
    {
        await service.Import(model);
        return BaseResponseBuilder.Ok;
    }

    // [HttpPut("{id:int}/share-png/code")]
    // [SwaggerOperation(OperationId = "AppendMediaLibraryTemplateShareCodeToPng")]
    // public async Task<IActionResult> AppendShareCodeToPng(int id)
    // {
    //     var code = await service.GenerateShareCode(id);
    //     throw new NotImplementedException();
    // }
}