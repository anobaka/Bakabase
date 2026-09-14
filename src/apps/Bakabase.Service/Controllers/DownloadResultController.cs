using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Downloader;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Result inspection and server-side workflow retry; no caller-supplied filesystem paths.</summary>
[ApiController]
[Route("~/downloader/result")]
public sealed class DownloadResultController(BakabaseDbContext db, DownloadResultWorkflowService service) : ControllerBase
{
    [HttpGet]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "GetDownloadResults")]
    public async Task<ListResponse<DownloadResultViewModel>> Get([FromQuery] int taskId)
    {
        var ct = HttpContext.RequestAborted;
        var rows = await db.Set<DownloadResultDbModel>().AsNoTracking().Where(r => r.DownloadTaskId == taskId)
            .OrderByDescending(r => r.Id).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var states = await db.Set<DownloadResultProcessingDbModel>().AsNoTracking()
            .Where(p => ids.Contains(p.DownloadResultId)).ToDictionaryAsync(p => p.DownloadResultId, ct);
        var owner = await db.Set<DownloadResultOwnerDbModel>().AsNoTracking()
            .SingleOrDefaultAsync(o => o.DownloadTaskId == taskId, ct);
        var runIds = states.Values.Where(s => s.WorkflowRunId != null).Select(s => s.WorkflowRunId!.Value).ToList();
        if (owner != null) runIds.Add(owner.WorkflowRunId);
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var definitionIds = rows.Where(r => r.WorkflowDefinitionId != null).Select(r => r.WorkflowDefinitionId!.Value)
            .Concat(runs.Values.Select(r => r.WorkflowDefinitionId)).Distinct().ToList();
        var definitions = await db.Set<WorkflowDefinitionDbModel>().Where(d => definitionIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        return new ListResponse<DownloadResultViewModel>(rows.Select(result =>
        {
            var state = states.GetValueOrDefault(result.Id);
            var runId = owner?.WorkflowRunId ?? state?.WorkflowRunId;
            var run = runId is { } id ? runs.GetValueOrDefault(id) : null;
            var definitionId = run?.WorkflowDefinitionId ?? result.WorkflowDefinitionId;
            var directory = state?.ContentsDirectory ?? (result.Kind == DownloadResultKind.LocalFiles ? result.Path : null);
            return new DownloadResultViewModel
            {
                Id = result.Id, DownloadTaskId = taskId, SourceKey = result.SourceKey, Name = result.Name,
                Kind = result.Kind, CreatedAt = result.CreatedAt,
                WorkflowDefinitionId = definitionId, WorkflowRunId = runId, WorkflowStatus = run?.Status,
                WorkflowName = definitionId is { } defId ? definitions.GetValueOrDefault(defId)?.Name : null,
                WorkflowIsBuiltin = definitionId is { } builtinId && definitions.GetValueOrDefault(builtinId)?.IsBuiltin == true,
                AcquisitionTaskId = owner?.AcquisitionTaskId, ResourceId = owner?.ResourceId ?? state?.ResourceId,
                ContentsReady = (state?.ContentsReadyAt != null || result.Kind == DownloadResultKind.LocalFiles) &&
                    FilesArePresent(directory, state?.ContentsFilesJson ?? result.FilesJson),
                ContentsDirectory = directory, Error = state?.DispatchError ?? run?.ErrorMessage,
                FilterDidNotMatch = state?.FilterDidNotMatch ?? false,
                CanRetry = owner == null && result.WorkflowDefinitionId != null &&
                    (run?.Status is WorkflowRunStatus.Failed or WorkflowRunStatus.Cancelled or WorkflowRunStatus.Interrupted ||
                     runId == null && state?.DispatchError != null)
            };
        }).ToList());
    }

    private static bool FilesArePresent(string? directory, string? filesJson)
    {
        if (!Directory.Exists(directory) || string.IsNullOrWhiteSpace(filesJson)) return false;
        try
        {
            var files = JsonSerializer.Deserialize<List<string>>(filesJson);
            return files is {Count: > 0} && files.All(System.IO.File.Exists);
        }
        catch (JsonException) {return false;}
    }

    [HttpPost("{id:int}/retry")]
    [RemoteAccessible]
    [SwaggerOperation(OperationId = "RetryDownloadResultWorkflow")]
    public async Task<BaseResponse> Retry(int id)
    {
        try
        {
            await service.RetryAsync(id, HttpContext.RequestAborted);
            return BaseResponseBuilder.Ok;
        }
        catch (InvalidOperationException ex) {return BaseResponseBuilder.BuildBadRequest(ex.Message);}
    }
}
