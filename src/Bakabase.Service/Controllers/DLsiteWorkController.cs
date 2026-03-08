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

[Route("~/dlsite-work")]
public class DLsiteWorkController(IDLsiteWorkService service, BTaskManager btm, IBakabaseLocalizer localizer)
    : Controller
{
    public const string SyncTaskId = "SyncDLsite";

    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllDLsiteWorks")]
    public async Task<ListResponse<DLsiteWorkDbModel>> GetAll()
    {
        var data = await service.GetAll();
        return new ListResponse<DLsiteWorkDbModel>(data);
    }

    [HttpGet("{workId}")]
    [SwaggerOperation(OperationId = "GetDLsiteWorkByWorkId")]
    public async Task<SingletonResponse<DLsiteWorkDbModel>> GetByWorkId(string workId)
    {
        var data = await service.GetByWorkId(workId);
        return new SingletonResponse<DLsiteWorkDbModel>(data);
    }

    [HttpDelete("{workId}")]
    [SwaggerOperation(OperationId = "DeleteDLsiteWork")]
    public async Task<BaseResponse> Delete(string workId)
    {
        await service.DeleteByWorkId(workId);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("sync")]
    [SwaggerOperation(OperationId = "SyncDLsiteWorks")]
    public async Task<BaseResponse> Sync()
    {
        await btm.Start(SyncTaskId, () => new BTaskHandlerBuilder
        {
            Id = SyncTaskId,
            GetName = () => localizer.BTask_Name(SyncTaskId),
            GetDescription = () => localizer.BTask_Description(SyncTaskId),
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDLsiteWorkService>();
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
