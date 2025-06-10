using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/media-library-v2")]
public class MediaLibraryV2Controller(IMediaLibraryV2Service service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllMediaLibraryV2")]
    public async Task<ListResponse<MediaLibraryV2>> GetAll(
        MediaLibraryV2AdditionalItem additionalItems = MediaLibraryV2AdditionalItem.None)
    {
        var items = await service.GetAll(null, additionalItems);
        return new ListResponse<MediaLibraryV2>(items);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetMediaLibraryV2")]
    public async Task<SingletonResponse<MediaLibraryV2>> Get(int id)
    {
        var item = await service.Get(id);
        return new SingletonResponse<MediaLibraryV2>(item);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddMediaLibraryV2")]
    public async Task<BaseResponse> Add([FromBody] MediaLibraryV2AddOrPutInputModel model)
    {
        await service.Add(model);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "PutMediaLibraryV2")]
    public async Task<BaseResponse> Put(int id, [FromBody] MediaLibraryV2AddOrPutInputModel model)
    {
        await service.Put(id, model);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut]
    [SwaggerOperation(OperationId = "SaveAllMediaLibrariesV2")]
    public async Task<BaseResponse> SaveAll([FromBody] MediaLibraryV2[] model)
    {
        await service.SaveAll(model);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteMediaLibraryV2")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/sync")]
    [SwaggerOperation(OperationId = "SyncMediaLibraryV2")]
    public async Task<BaseResponse> Sync(int id)
    {
        await service.StartSyncAll([id]);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("sync-all")]
    [SwaggerOperation(OperationId = "SyncAllMediaLibrariesV2")]
    public async Task<BaseResponse> SyncAll()
    {
        await service.StartSyncAll();
        return BaseResponseBuilder.Ok;
    }
}