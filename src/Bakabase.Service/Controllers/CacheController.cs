using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/cache")]
public class CacheController(IResourceService resourceService) : Controller
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetCacheOverview")]
    public async Task<SingletonResponse<CacheOverviewViewModel>> GetOverview()
    {
        return new SingletonResponse<CacheOverviewViewModel>(await resourceService.GetCacheOverview());
    }

    [HttpGet("resource/{resourceId:int}/type/{type}/existence")]
    [SwaggerOperation(OperationId = "CheckResourceCacheExistence")]
    public async Task<SingletonResponse<bool>> CheckResourceCacheExistence(int resourceId, ResourceCacheType type)
    {
        var cache = await resourceService.GetResourceCache(resourceId);
        return new SingletonResponse<bool>(cache?.CachedTypes?.Contains(type) == true);
    }
    
    [HttpDelete("resource/{resourceId:int}/type/{type}")]
    [SwaggerOperation(OperationId = "DeleteResourceCacheByResourceIdAndCacheType")]
    public async Task<BaseResponse> DeleteResourceCacheByResourceIdAndCacheType(int resourceId, ResourceCacheType type)
    {
        await resourceService.DeleteResourceCacheByResourceIdAndCacheType(resourceId, type);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("category/{categoryId:int}/type/{type}")]
    [SwaggerOperation(OperationId = "DeleteResourceCacheByCategoryIdAndCacheType")]
    public async Task<BaseResponse> DeleteResourceCacheByCategoryIdAndCacheType(int categoryId, ResourceCacheType type)
    {
        await resourceService.DeleteResourceCacheByCategoryIdAndCacheType(categoryId, type);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("media-library/{mediaLibraryId:int}/type/{type}")]
    [SwaggerOperation(OperationId = "DeleteResourceCacheByMediaLibraryIdAndCacheType")]
    public async Task<BaseResponse> DeleteResourceCacheByMediaLibraryIdAndCacheType(int mediaLibraryId,
        ResourceCacheType type)
    {
        await resourceService.DeleteResourceCacheByMediaLibraryIdAndCacheType(mediaLibraryId, type);
        return BaseResponseBuilder.Ok;
    }
}