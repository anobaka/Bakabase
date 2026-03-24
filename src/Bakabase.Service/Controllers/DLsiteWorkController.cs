using System.Collections.Generic;
using System.Threading;
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
public class DLsiteWorkController(IDLsiteWorkService service, BTaskManager btm, IBakabaseLocalizer localizer, IPathMarkSyncService pathMarkSyncService)
    : Controller
{
    public const string SyncTaskId = "SyncDLsite";
    public const string DownloadTaskIdPrefix = "DownloadDLsite_";
    public const string ExtractTaskIdPrefix = "ExtractDLsite_";

    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllDLsiteWorks")]
    public async Task<SearchResponse<DLsiteWorkDbModel>> GetAll([FromQuery] string? keyword, [FromQuery] bool showHidden = false, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20)
    {
        return await service.Search(keyword, showHidden, pageIndex, pageSize);
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
    public async Task<BaseResponse> Sync([FromQuery] bool redownloadCover = false)
    {
        if (redownloadCover)
        {
            var sourceLinkService = HttpContext.RequestServices.GetRequiredService<IResourceSourceLinkService>();
            await sourceLinkService.ClearAllLocalCoverPaths(ResourceSource.DLsite);
        }

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

                // Enqueue resource sync for DLsite source after API sync completes
                await pathMarkSyncService.EnqueueSync(ResourceSource.DLsite);
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
    public async Task<SingletonResponse<string>> Download(string workId)
    {
        // Prepare directory and set LocalPath before starting async task
        var localPath = await service.PrepareDownloadDirectory(workId);

        var taskId = $"{DownloadTaskIdPrefix}{workId}";
        await btm.Start(taskId, () => new BTaskHandlerBuilder
        {
            Id = taskId,
            GetName = () => localizer["BTask_Name_DownloadDLsite", workId],
            GetDescription = () => localizer["BTask_Description_DownloadDLsite", workId],
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
            IsPersistent = false,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            RootServiceProvider = HttpContext.RequestServices
        });
        return new SingletonResponse<string>(localPath);
    }

    [HttpGet("{workId}/drm-key")]
    [SwaggerOperation(OperationId = "GetDLsiteWorkDrmKey")]
    public async Task<SingletonResponse<string>> GetDrmKey(string workId, CancellationToken ct)
    {
        var key = await service.FetchDrmKey(workId, ct);
        return new SingletonResponse<string>(key);
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

    [HttpPost("scan-folders")]
    [SwaggerOperation(OperationId = "ScanDLsiteFolders")]
    public async Task<BaseResponse> ScanFolders()
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
                await svc.ScanConfiguredFolders(
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

    [HttpPost("{workId}/extract")]
    [SwaggerOperation(OperationId = "ExtractDLsiteWork")]
    public async Task<BaseResponse> Extract(string workId)
    {
        var taskId = $"{ExtractTaskIdPrefix}{workId}";
        await btm.Start(taskId, () => new BTaskHandlerBuilder
        {
            Id = taskId,
            GetName = () => localizer["BTask_Name_ExtractDLsite", workId],
            GetDescription = () => localizer["BTask_Description_ExtractDLsite", workId],
            Run = async args =>
            {
                await using var scope = args.RootServiceProvider.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDLsiteWorkService>();
                await svc.ExtractWork(
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
            IsPersistent = false,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Replace,
            RootServiceProvider = HttpContext.RequestServices
        });
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{workId}/local-files")]
    [SwaggerOperation(OperationId = "DeleteDLsiteWorkLocalFiles")]
    public async Task<BaseResponse> DeleteLocalFiles(string workId)
    {
        await service.DeleteLocalFiles(workId);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{workId}/hidden")]
    [SwaggerOperation(OperationId = "SetDLsiteWorkHidden")]
    public async Task<BaseResponse> SetHidden(string workId, [FromBody] bool isHidden)
    {
        await service.SetHidden(workId, isHidden);
        return BaseResponseBuilder.Ok;
    }

    [HttpPut("{workId}/use-locale-emulator")]
    [SwaggerOperation(OperationId = "SetDLsiteWorkUseLocaleEmulator")]
    public async Task<BaseResponse> SetUseLocaleEmulator(string workId, [FromBody] bool useLocaleEmulator)
    {
        await service.SetUseLocaleEmulator(workId, useLocaleEmulator);
        return BaseResponseBuilder.Ok;
    }
}
