using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Models.View;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Models.Db;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Components.Tasks;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/cache")]
public class CacheController(
    IResourceService resourceService,
    BTaskManager taskManager,
    IBakabaseLocalizer localizer) : Controller
{
    private const string RefreshResourcesCacheTaskId = "RefreshResourcesCache";

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

    [HttpDelete("media-library/{mediaLibraryId:int}/type/{type}")]
    [SwaggerOperation(OperationId = "DeleteResourceCacheByMediaLibraryIdAndCacheType")]
    public async Task<BaseResponse> DeleteResourceCacheByMediaLibraryIdAndCacheType(int mediaLibraryId,
        ResourceCacheType type)
    {
        await resourceService.DeleteResourceCacheByMediaLibraryIdAndCacheType(mediaLibraryId, type);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("unassociated/type/{type}")]
    [SwaggerOperation(OperationId = "DeleteUnassociatedResourceCacheByCacheType")]
    public async Task<BaseResponse> DeleteUnassociatedResourceCacheByCacheType(ResourceCacheType type)
    {
        await resourceService.DeleteUnassociatedResourceCacheByCacheType(type);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("resource/{resourceId:int}/refresh")]
    [SwaggerOperation(OperationId = "RefreshResourceCache")]
    public async Task<SingletonResponse<ResourceFileSystemCache>> RefreshResourceCache(int resourceId, CancellationToken ct)
    {
        var cache = await resourceService.RefreshResourceCache(resourceId, ct);
        return new SingletonResponse<ResourceFileSystemCache>(cache);
    }

    [HttpPost("resources/refresh")]
    [SwaggerOperation(OperationId = "RefreshResourcesCache")]
    public async Task<BaseResponse> RefreshResourcesCache([FromBody] RefreshResourcesCacheInputModel model)
    {
        var ids = model.Ids?.Distinct().ToArray() ?? [];
        if (ids.Length == 0)
        {
            return BaseResponseBuilder.Ok;
        }

        // Refreshing a large selection scans the filesystem and regenerates thumbnails per
        // resource, so run it as a cancellable background task instead of blocking the request.
        await taskManager.Enqueue(
            BTaskBuilder.Create(RefreshResourcesCacheTaskId)
                .Named(() => localizer.BTask_Name(RefreshResourcesCacheTaskId))
                .Describe(() => localizer.BTask_Description(RefreshResourcesCacheTaskId))
                .OfResourceType(BTaskResourceType.Resource)
                .ForResources(ids.Cast<object>().ToArray())
                .ConflictsWith(RefreshResourcesCacheTaskId)
                .Persistent()
                .ReplaceIfExists()
                .Run(async args =>
                {
                    await using var scope = args.RootServiceProvider.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<IResourceService>();
                    await service.RefreshResourcesCache(
                        ids,
                        async (percentage, process) =>
                        {
                            await args.UpdateTask(t =>
                            {
                                t.Percentage = percentage;
                                t.Process = process;
                            });
                        },
                        args.CancellationToken);
                }));

        return BaseResponseBuilder.Ok;
    }
}