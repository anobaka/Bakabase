using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

[Route("~/steam-app")]
public class SteamAppController(ISteamAppService service, BTaskManager btm, IBakabaseLocalizer localizer, IPathMarkSyncService pathMarkSyncService) : Controller
{
    public const string SyncTaskId = "SyncSteam";

    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllSteamApps")]
    public async Task<SearchResponse<SteamAppDbModel>> GetAll([FromQuery] string? keyword, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return await service.Search(keyword, pageIndex, pageSize);
    }

    [HttpGet("{appId:int}")]
    [SwaggerOperation(OperationId = "GetSteamAppByAppId")]
    public async Task<SingletonResponse<SteamAppDbModel>> GetByAppId(int appId)
    {
        var data = await service.GetByAppId(appId);
        return new SingletonResponse<SteamAppDbModel>(data);
    }

    [HttpDelete("{appId:int}")]
    [SwaggerOperation(OperationId = "DeleteSteamApp")]
    public async Task<BaseResponse> Delete(int appId)
    {
        await service.DeleteByAppId(appId);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{appId:int}/hidden")]
    [SwaggerOperation(OperationId = "SetSteamAppHidden")]
    public async Task<BaseResponse> SetHidden(int appId, [FromBody] bool isHidden)
    {
        await service.SetHidden(appId, isHidden);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("sync")]
    [SwaggerOperation(OperationId = "SyncSteamApps")]
    public async Task<BaseResponse> Sync([FromQuery] bool redownloadCover = false)
    {
        if (redownloadCover)
        {
            var sourceLinkService = HttpContext.RequestServices.GetRequiredService<IResourceSourceLinkService>();
            await sourceLinkService.ClearAllLocalCoverPaths(ResourceSource.Steam);
        }

        await btm.Start(SyncTaskId, () => new BTaskHandlerBuilder
        {
            Id = SyncTaskId,
            GetName = () => localizer.BTask_Name(SyncTaskId),
            GetDescription = () => localizer.BTask_Description(SyncTaskId),
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<ISteamAppService>();
                await svc.SyncFromApi(
                    async (percentage, count) =>
                    {
                        await args.UpdateTask(t =>
                        {
                            t.Percentage = percentage;
                            t.Process = count.ToString();
                        });
                    },
                    args.CancellationToken);

                // Enqueue resource sync for Steam source after API sync completes
                await pathMarkSyncService.EnqueueSync(ResourceSource.Steam);
            },
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            IsPersistent = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            RootServiceProvider = HttpContext.RequestServices
        });
        return BaseResponseBuilder.Ok;
    }
}
