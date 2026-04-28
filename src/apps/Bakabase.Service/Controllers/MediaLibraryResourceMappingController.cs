using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[ApiController]
[Route("~/media-library-resource-mapping")]
public class MediaLibraryResourceMappingController(IMediaLibraryResourceMappingService service) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllMediaLibraryResourceMappings")]
    public async Task<ListResponse<MediaLibraryResourceMapping>> GetAll()
    {
        var items = await service.GetAll();
        return new ListResponse<MediaLibraryResourceMapping>(items);
    }

    [HttpGet("by-resource/{resourceId:int}")]
    [SwaggerOperation(OperationId = "GetMappingsByResourceId")]
    public async Task<ListResponse<MediaLibraryResourceMapping>> GetByResourceId(int resourceId)
    {
        var items = await service.GetByResourceId(resourceId);
        return new ListResponse<MediaLibraryResourceMapping>(items);
    }

    [HttpGet("by-media-library/{mediaLibraryId:int}")]
    [SwaggerOperation(OperationId = "GetMappingsByMediaLibraryId")]
    public async Task<ListResponse<MediaLibraryResourceMapping>> GetByMediaLibraryId(int mediaLibraryId)
    {
        var items = await service.GetByMediaLibraryId(mediaLibraryId);
        return new ListResponse<MediaLibraryResourceMapping>(items);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddMediaLibraryResourceMapping")]
    public async Task<SingletonResponse<MediaLibraryResourceMapping>> Add([FromBody] MediaLibraryResourceMapping mapping)
    {
        var result = await service.Add(mapping);
        return new SingletonResponse<MediaLibraryResourceMapping>(result);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteMediaLibraryResourceMapping")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.Delete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("by-resource/{resourceId:int}")]
    [SwaggerOperation(OperationId = "DeleteMappingsByResourceId")]
    public async Task<BaseResponse> DeleteByResourceId(int resourceId)
    {
        await service.DeleteByResourceId(resourceId);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("ensure")]
    [SwaggerOperation(OperationId = "EnsureMediaLibraryResourceMappings")]
    public async Task<BaseResponse> EnsureMappings([FromBody] EnsureMappingsInput input)
    {
        await service.EnsureMappings(input.ResourceId, input.MediaLibraryIds);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("replace")]
    [SwaggerOperation(OperationId = "ReplaceMediaLibraryResourceMappings")]
    public async Task<BaseResponse> ReplaceMappings([FromBody] EnsureMappingsInput input)
    {
        await service.ReplaceMappings(input.ResourceId, input.MediaLibraryIds);
        return BaseResponseBuilder.Ok;
    }
}

public class EnsureMappingsInput
{
    public int ResourceId { get; set; }
    public List<int> MediaLibraryIds { get; set; } = new();
}
