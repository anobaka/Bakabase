using System.Collections.Generic;
using System.Linq;
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
public class MediaLibraryV2Controller(
    IMediaLibraryV2Service service,
    IMediaLibraryResourceMappingService mappingService) : ControllerBase
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

    [HttpPatch("{id:int}")]
    [SwaggerOperation(OperationId = "PatchMediaLibraryV2")]
    public async Task<BaseResponse> Patch(int id, [FromBody] MediaLibraryV2PatchInputModel model)
    {
        await service.Patch(id, model);
        return BaseResponseBuilder.Ok;
    }

    [HttpPatch("mark-as-synced")]
    [SwaggerOperation(OperationId = "MarkMediaLibraryV2AsSynced")]
    public async Task<BaseResponse> MarkAsSynced([FromBody] int[] ids)
    {
        await service.MarkAsSynced(ids);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut]
    [SwaggerOperation(OperationId = "SaveAllMediaLibrariesV2")]
    public async Task<BaseResponse> SaveAll([FromBody] MediaLibraryV2[] model)
    {
        await service.ReplaceAll(model);
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

    [HttpGet("outdated")]
    [SwaggerOperation(OperationId = "GetOutdatedMediaLibrariesV2")]
    public async Task<ListResponse<MediaLibraryV2>> GetOutdated()
    {
        var items = await service.GetAllSyncMayBeOutdated();
        return new ListResponse<MediaLibraryV2>(items);
    }

    #region Multi-Library Resource Management

    /// <summary>
    /// Get all resource mappings for a media library
    /// </summary>
    [HttpGet("{id:int}/resources")]
    [SwaggerOperation(OperationId = "GetMediaLibraryV2Resources")]
    public async Task<ListResponse<MediaLibraryResourceMapping>> GetResources(int id)
    {
        var mappings = await mappingService.GetByMediaLibraryId(id);
        return new ListResponse<MediaLibraryResourceMapping>(mappings);
    }

    /// <summary>
    /// Get resource count for a media library
    /// </summary>
    [HttpGet("{id:int}/resource-count")]
    [SwaggerOperation(OperationId = "GetMediaLibraryV2ResourceCount")]
    public async Task<SingletonResponse<int>> GetResourceCount(int id)
    {
        var mappings = await mappingService.GetByMediaLibraryId(id);
        return new SingletonResponse<int>(mappings.Count);
    }

    /// <summary>
    /// Remove all resource mappings for a media library (does not delete resources)
    /// </summary>
    [HttpDelete("{id:int}/resources")]
    [SwaggerOperation(OperationId = "RemoveAllMediaLibraryV2ResourceMappings")]
    public async Task<BaseResponse> RemoveAllResourceMappings(int id)
    {
        await mappingService.DeleteByMediaLibraryId(id);
        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// Get statistics for a media library including resource counts
    /// </summary>
    [HttpGet("{id:int}/statistics")]
    [SwaggerOperation(OperationId = "GetMediaLibraryV2Statistics")]
    public async Task<SingletonResponse<MediaLibraryStatistics>> GetStatistics(int id)
    {
        var mediaLibrary = await service.Get(id);
        if (mediaLibrary == null)
        {
            return SingletonResponseBuilder<MediaLibraryStatistics>.NotFound;
        }

        var mappings = await mappingService.GetByMediaLibraryId(id);

        var stats = new MediaLibraryStatistics
        {
            TotalResourceCount = mappings.Count
        };

        return new SingletonResponse<MediaLibraryStatistics>(stats);
    }

    #endregion
}

/// <summary>
/// Statistics for a media library
/// </summary>
public class MediaLibraryStatistics
{
    /// <summary>
    /// Total number of resources mapped to this library
    /// </summary>
    public int TotalResourceCount { get; set; }
}