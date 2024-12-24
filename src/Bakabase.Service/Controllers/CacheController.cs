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

    [HttpDelete("category/{categoryId:int}/type/{type}")]
    [SwaggerOperation(OperationId = "DeleteResourceCacheByCategoryIdAndCacheType")]
    public async Task<BaseResponse> DeleteResourceCacheByCategoryIdAndCacheType(int categoryId, ResourceCacheType type)
    {
        await resourceService.DeleteResourceCacheByCategoryIdAndCacheType(categoryId, type);
        return BaseResponseBuilder.Ok;
    }
}