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
    public const string DownloadTaskIdPrefix = "DownloadDLsite_";

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

    [HttpPost("{workId}/download")]
    [SwaggerOperation(OperationId = "DownloadDLsiteWork")]
    public async Task<BaseResponse> Download(string workId)
    {
        var taskId = $"{DownloadTaskIdPrefix}{workId}";
        await btm.Start(taskId, () => new BTaskHandlerBuilder
        {
            Id = taskId,
            GetName = () => $"Download DLsite: {workId}",
            GetDescription = () => $"Downloading work {workId} from DLsite",
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDLsiteWorkService>();
                await svc.DownloadWork(
                    workId,
                    async (percentage, process) =>
                    {
                        await args.UpdateTask(t =>
                        {
                            t.Percentage = percentage;
                            t.Process = process;
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

    [HttpPost("{workId}/launch")]
    [SwaggerOperation(OperationId = "LaunchDLsiteWork")]
    public async Task<BaseResponse> Launch(string workId)
    {
        await service.LaunchWork(workId);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("{workId}/playable-files")]
    [SwaggerOperation(OperationId = "GetDLsiteWorkPlayableFiles")]
    public async Task<ListResponse<string>> GetPlayableFiles(string workId)
    {
        var work = await service.GetByWorkId(workId);
        if (work == null)
        {
            return new ListResponse<string>();
        }

        if (string.IsNullOrEmpty(work.LocalPath))
        {
            return new ListResponse<string>();
        }

        var files = service.FindPlayableFiles(work.LocalPath, work.WorkType);
        return new ListResponse<string>(files);
    }

    [HttpPost("scan-folder")]
    [SwaggerOperation(OperationId = "ScanDLsiteFolder")]
    public async Task<BaseResponse> ScanFolder([FromBody] string folderPath)
    {
        var taskId = "ScanDLsiteFolder";
        await btm.Start(taskId, () => new BTaskHandlerBuilder
        {
            Id = taskId,
            GetName = () => localizer.BTask_Name(taskId),
            GetDescription = () => localizer.BTask_Description(taskId),
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDLsiteWorkService>();
                await svc.ScanFolder(
                    folderPath,
                    async (percentage, matched) =>
                    {
                        await args.UpdateTask(t =>
                        {
                            t.Percentage = percentage;
                            t.Process = matched.ToString();
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

    [HttpPut("{workId}/hidden")]
    [SwaggerOperation(OperationId = "SetDLsiteWorkHidden")]
    public async Task<BaseResponse> SetHidden(string workId, [FromBody] bool isHidden)
    {
        await service.SetHidden(workId, isHidden);
        return BaseResponseBuilder.Ok;
    }
}
