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
[Route("~/path-mark")]
public class PathMarkController(IPathMarkService service, IPathMarkSyncService syncService) : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetAllPathMarks")]
    public async Task<ListResponse<PathMark>> GetAll([FromQuery] PathMarkAdditionalItem additionalItems = PathMarkAdditionalItem.None)
    {
        var items = await service.GetAll(additionalItems: additionalItems);
        return new ListResponse<PathMark>(items);
    }

    [HttpGet("{id:int}")]
    [SwaggerOperation(OperationId = "GetPathMark")]
    public async Task<SingletonResponse<PathMark?>> Get(int id)
    {
        var item = await service.Get(id);
        return new SingletonResponse<PathMark?>(item);
    }

    [HttpGet("by-path")]
    [SwaggerOperation(OperationId = "GetPathMarksByPath")]
    public async Task<ListResponse<PathMark>> GetByPath([FromQuery] string path, [FromQuery] bool includeDeleted = false)
    {
        var items = await service.GetByPath(path, includeDeleted);
        return new ListResponse<PathMark>(items);
    }

    [HttpGet("paths")]
    [SwaggerOperation(OperationId = "GetAllPathMarkPaths")]
    public async Task<ListResponse<string>> GetAllPaths()
    {
        var paths = await service.GetAllPaths();
        return new ListResponse<string>(paths);
    }

    [HttpGet("pending")]
    [SwaggerOperation(OperationId = "GetPendingPathMarks")]
    public async Task<ListResponse<PathMark>> GetPending()
    {
        var items = await service.GetPendingMarks();
        return new ListResponse<PathMark>(items);
    }

    [HttpGet("pending/count")]
    [SwaggerOperation(OperationId = "GetPendingPathMarksCount")]
    public async Task<SingletonResponse<int>> GetPendingCount()
    {
        var count = await service.GetPendingCount();
        return new SingletonResponse<int>(data: count);
    }

    [HttpGet("by-status/{status}")]
    [SwaggerOperation(OperationId = "GetPathMarksBySyncStatus")]
    public async Task<ListResponse<PathMark>> GetBySyncStatus(PathMarkSyncStatus status)
    {
        var items = await service.GetBySyncStatus(status);
        return new ListResponse<PathMark>(items);
    }

    [HttpPost]
    [SwaggerOperation(OperationId = "AddPathMark")]
    public async Task<SingletonResponse<PathMark>> Add([FromBody] PathMark mark)
    {
        var result = await service.Add(mark);
        return new SingletonResponse<PathMark>(result);
    }

    [HttpPost("batch")]
    [SwaggerOperation(OperationId = "AddPathMarks")]
    public async Task<ListResponse<PathMark>> AddRange([FromBody] List<PathMark> marks)
    {
        var results = await service.AddRange(marks);
        return new ListResponse<PathMark>(results);
    }

    [HttpPut("{id:int}")]
    [SwaggerOperation(OperationId = "UpdatePathMark")]
    public async Task<BaseResponse> Update(int id, [FromBody] PathMark mark)
    {
        mark.Id = id;
        await service.Update(mark);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}")]
    [SwaggerOperation(OperationId = "SoftDeletePathMark")]
    public async Task<BaseResponse> SoftDelete(int id)
    {
        await service.SoftDelete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("by-path")]
    [SwaggerOperation(OperationId = "SoftDeletePathMarksByPath")]
    public async Task<BaseResponse> SoftDeleteByPath([FromQuery] string path)
    {
        await service.SoftDeleteByPath(path);
        return BaseResponseBuilder.Ok;
    }

    [HttpDelete("{id:int}/hard")]
    [SwaggerOperation(OperationId = "HardDeletePathMark")]
    public async Task<BaseResponse> HardDelete(int id)
    {
        await service.HardDelete(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/sync/start")]
    [SwaggerOperation(OperationId = "StartSyncPathMark")]
    public async Task<BaseResponse> MarkAsSyncing(int id)
    {
        await service.MarkAsSyncing(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/sync/complete")]
    [SwaggerOperation(OperationId = "CompleteSyncPathMark")]
    public async Task<BaseResponse> MarkAsSynced(int id)
    {
        await service.MarkAsSynced(id);
        return BaseResponseBuilder.Ok;
    }

    [HttpPost("{id:int}/sync/fail")]
    [SwaggerOperation(OperationId = "FailSyncPathMark")]
    public async Task<BaseResponse> MarkAsFailed(int id, [FromBody] string? error)
    {
        await service.MarkAsFailed(id, error);
        return BaseResponseBuilder.Ok;
    }

    [HttpGet("sync-status")]
    [SwaggerOperation(OperationId = "GetPathMarkSyncStatus")]
    public async Task<SingletonResponse<PathMarkSyncStatusResponse>> GetSyncStatus()
    {
        var pending = await service.GetPendingCount();
        var syncing = (await service.GetBySyncStatus(PathMarkSyncStatus.Syncing)).Count;
        var failed = (await service.GetBySyncStatus(PathMarkSyncStatus.Failed)).Count;

        return new SingletonResponse<PathMarkSyncStatusResponse>(new PathMarkSyncStatusResponse
        {
            PendingCount = pending,
            SyncingCount = syncing,
            FailedCount = failed
        });
    }

    [HttpPost("preview")]
    [SwaggerOperation(OperationId = "PreviewPathMarkMatchedPaths")]
    public async Task<ListResponse<PathMarkPreviewResult>> PreviewMatchedPaths([FromBody] PathMarkPreviewRequest request)
    {
        var results = await service.PreviewMatchedPaths(request);
        return new ListResponse<PathMarkPreviewResult>(results);
    }

    /// <summary>
    /// Check if paths of path marks exist on the file system
    /// Returns a dictionary of path to existence status
    /// </summary>
    [HttpGet("check-paths-exist")]
    [SwaggerOperation(OperationId = "CheckPathMarkPathsExist")]
    public async Task<SingletonResponse<Dictionary<string, bool>>> CheckPathsExist()
    {
        var paths = await service.GetAllPaths();
        var results = new Dictionary<string, bool>();
        foreach (var path in paths)
        {
            results[path] = System.IO.Directory.Exists(path) || System.IO.File.Exists(path);
        }
        return new SingletonResponse<Dictionary<string, bool>>(results);
    }

    /// <summary>
    /// Migrate path marks from old path to new path
    /// This will update the path of all marks with the old path to the new path
    /// </summary>
    [HttpPost("migrate-path")]
    [SwaggerOperation(OperationId = "MigratePathMarkPath")]
    public async Task<BaseResponse> MigratePath([FromBody] PathMigrationRequest request)
    {
        await service.MigratePath(request.OldPath, request.NewPath);
        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// Start synchronization of all pending path marks
    /// </summary>
    [HttpPost("sync/start-all")]
    [SwaggerOperation(OperationId = "StartPathMarkSyncAll")]
    public async Task<BaseResponse> StartSyncAll()
    {
        await syncService.EnqueueSync();
        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// Start synchronization of specific path marks
    /// </summary>
    [HttpPost("sync/start")]
    [SwaggerOperation(OperationId = "StartPathMarkSync")]
    public async Task<BaseResponse> StartSync([FromBody] int[] markIds)
    {
        if (markIds.Length == 0)
        {
            return BaseResponseBuilder.Ok;
        }

        await syncService.EnqueueSync(markIds);
        return BaseResponseBuilder.Ok;
    }

    /// <summary>
    /// Start synchronization of all path marks on a specific path
    /// </summary>
    [HttpPost("sync/by-path")]
    [SwaggerOperation(OperationId = "StartPathMarkSyncByPath")]
    public async Task<BaseResponse> StartSyncByPath([FromQuery] string path)
    {
        var marks = await service.GetByPath(path);
        if (marks.Count == 0)
        {
            return BaseResponseBuilder.Ok;
        }

        var markIds = marks.Select(m => m.Id).ToArray();
        await syncService.EnqueueSync(markIds);
        return BaseResponseBuilder.Ok;
    }
}

public class PathMarkSyncStatusResponse
{
    public int PendingCount { get; set; }
    public int SyncingCount { get; set; }
    public int FailedCount { get; set; }
}

public class PathMigrationRequest
{
    public string OldPath { get; set; } = null!;
    public string NewPath { get; set; } = null!;
}
