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

[Route("~/exhentai-gallery")]
public class ExHentaiGalleryController(IExHentaiGalleryService service, BTaskManager btm, IBakabaseLocalizer localizer, IPathMarkSyncService pathMarkSyncService)
    : Controller
{
    public const string SyncTaskId = "SyncExHentai";

    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllExHentaiGalleries")]
    public async Task<SearchResponse<ExHentaiGalleryDbModel>> GetAll([FromQuery] string? keyword, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return await service.Search(keyword, pageIndex, pageSize);
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "DeleteExHentaiGallery")]
    public async Task<BaseResponse> Delete(int id)
    {
        await service.DeleteById(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{galleryId:long}/{galleryToken}/local-files")]
    [SwaggerOperation(OperationId = "DeleteExHentaiGalleryLocalFiles")]
    public async Task<BaseResponse> DeleteLocalFiles(long galleryId, string galleryToken)
    {
        await service.DeleteLocalFiles(galleryId, galleryToken);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{galleryId:long}/{galleryToken}/hidden")]
    [SwaggerOperation(OperationId = "SetExHentaiGalleryHidden")]
    public async Task<BaseResponse> SetHidden(long galleryId, string galleryToken, [FromBody] bool isHidden)
    {
        await service.SetHidden(galleryId, galleryToken, isHidden);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("sync")]
    [SwaggerOperation(OperationId = "SyncExHentaiGalleries")]
    public async Task<BaseResponse> Sync([FromQuery] bool redownloadCover = false)
    {
        if (redownloadCover)
        {
            var sourceLinkService = HttpContext.RequestServices.GetRequiredService<IResourceSourceLinkService>();
            await sourceLinkService.ClearAllLocalCoverPaths(ResourceSource.ExHentai);
        }

        await btm.Start(SyncTaskId, () => new BTaskHandlerBuilder
        {
            Id = SyncTaskId,
            GetName = () => localizer.BTask_Name(SyncTaskId),
            GetDescription = () => localizer.BTask_Description(SyncTaskId),
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IExHentaiGalleryService>();
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

                // Enqueue resource sync for ExHentai source after API sync completes
                await pathMarkSyncService.EnqueueSync(ResourceSource.ExHentai);
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
