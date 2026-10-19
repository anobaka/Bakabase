using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Downloader.Models;
using Bakabase.Modules.Downloader.Models.Tasks;
using Bakabase.Modules.HttpDownloads.Abstractions;
using Bakabase.Modules.HttpDownloads.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Models.Input;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using CommonTask = Bakabase.Modules.Downloader.Models.Tasks.DownloadTask;
using SiteTask = Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.DownloadTask;

namespace Bakabase.Service.Components.Downloader;

/// <summary>One task identity and command surface; providers retain their queues, configuration and recovery.</summary>
public sealed class DownloadCenterService(BakabaseDbContext db, DownloadTaskService sites,
    IBtDownloadManager bt, IHttpDownloadManager http, DownloadTaskExecutionService executions,
    DownloadTaskRegistry registry, BTaskManager backgroundTasks)
{
    public async Task<IReadOnlyList<CommonTask>> ListAsync(CancellationToken ct = default)
    {
        var siteTasks = await sites.GetAllDto();
        var btJobs = await bt.ListAsync(ct);
        var httpJobs = await http.ListAsync(ct);
        var sourceRows = await db.DownloadTasks.AsNoTracking().ToDictionaryAsync(t => t.Id, ct);
        var linkedResultIds = btJobs.Select(j => j.DownloadResultId).Concat(httpJobs.Select(j => j.DownloadResultId))
            .Where(id => id != null).Select(id => id!.Value).Distinct().ToArray();
        var resultRows = await db.DownloadResults.AsNoTracking().Where(r => linkedResultIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var owners = await db.DownloadResultOwners.AsNoTracking().ToDictionaryAsync(o => o.DownloadTaskId, ct);
        var ownerRunIds = owners.Values.Select(o => o.WorkflowRunId).Distinct().ToArray();
        var ownerRuns = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => ownerRunIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        var latestResults = (await db.DownloadResults.AsNoTracking().Where(r => linkedResultIds.Contains(r.Id) &&
            !db.DownloadResults.Any(newer => newer.DownloadTaskId == r.DownloadTaskId && newer.SourceKey == r.SourceKey && newer.Id > r.Id))
            .Select(r => r.Id).ToListAsync(ct)).ToHashSet();
        var summaries = (await executions.GetAsync(siteTasks.Select(t => t.Id).ToArray(), ct))
            .ToDictionary(s => s.DownloadTaskId);
        var snapshots = new List<CommonTask>(siteTasks.Length + btJobs.Count + httpJobs.Count);
        foreach (var source in siteTasks)
        {
            var owner = owners.GetValueOrDefault(source.Id);
            var run = owner == null ? null : ownerRuns.GetValueOrDefault(owner.WorkflowRunId);
            var summary = summaries.GetValueOrDefault(source.Id);
            var status = summary?.Status ?? SiteStatus(source.Status);
            snapshots.Add(new CommonTask
            {
                Id = $"site:{source.Id}", Category = "site", Provider = "site", ProviderTaskId = source.Id.ToString(),
                SiteTaskId = source.Id, ThirdPartyId = (int)source.ThirdPartyId, Key = source.Key,
                Name = source.DisplayName, OutputDirectory = source.DownloadPath, Status = status,
                Stage = summary?.Status ?? source.Current, Progress = summary != null ? summary.Progress : (double)source.Progress,
                CompletedBytes = source.DownloadedBytes, SpeedBytesPerSecond = summary?.SpeedBytesPerSecond ?? source.DownloadSpeedBytesPerSecond,
                Error = status is "failed" or "processingFailed" ? source.Message : null,
                CreatedAt = source.CreatedAt.ToUniversalTime(), UpdatedAt = source.DownloadStatusUpdateDt.ToUniversalTime(),
                CompletedAt = status == "completed" ? source.CompletedAt : null,
                OriginKind = owner == null ? "manual" : "workflow", OriginId = owner?.AcquisitionTaskId.ToString(),
                WorkflowRunId = run?.Id ?? owner?.WorkflowRunId, WorkflowDefinitionId = run?.WorkflowDefinitionId,
                AvailableActions = owner == null ? SiteActions(source, status) : [],
                Execution = summary == null ? null : new DownloadTaskExecution
                {
                    DownloadTaskId = source.Id, Status = summary.Status, TotalCount = summary.TotalCount,
                    CompletedCount = summary.CompletedCount, RunningCount = summary.RunningCount,
                    QueuedCount = summary.QueuedCount, StoppedCount = summary.StoppedCount,
                    FailedCount = summary.FailedCount, WaitingCount = summary.WaitingCount,
                    SourceCompleted = summary.SourceCompleted, Progress = summary.Progress,
                    SpeedBytesPerSecond = summary.SpeedBytesPerSecond
                }
            });
        }
        bool CanResume(int? sourceId, int? resultId, int? workflowRunId, string sourceKind, bool isBt = false)
        {
            if (workflowRunId != null || sourceKind == "workflow") return false;
            if (sourceKind == "manual" && sourceId == null && resultId == null) return true;
            return resultId is { } id && resultRows.TryGetValue(id, out var result) && latestResults.Contains(id) &&
                sourceRows.TryGetValue(result.DownloadTaskId, out var source) && !owners.ContainsKey(source.Id) &&
                source.Status != DownloadTaskDbModelStatus.Disabled &&
                DownloadTaskExecutionLifecycle.CanContinue(source, result, independentBt: isBt);
        }
        foreach (var job in btJobs)
        {
            var sourceId = job.DownloadTaskId ?? (job.DownloadResultId is { } id ? resultRows.GetValueOrDefault(id)?.DownloadTaskId : null);
            snapshots.Add(new CommonTask
            {
                Id = $"bt:{job.Id}", Category = "general", Provider = "bt", ProviderTaskId = job.Id,
                ParentId = sourceId is { } parent && sourceRows.ContainsKey(parent) ? $"site:{parent}" : null,
                SiteTaskId = sourceId, Name = job.Name, OutputDirectory = job.OutputDirectory, Files = job.Files,
                Status = TransferStatus((int)job.Status), Stage = job.Stage, Progress = job.Percentage,
                CompletedBytes = job.CompletedBytes, TotalBytes = job.TotalBytes, SpeedBytesPerSecond = job.SpeedBytesPerSecond,
                Error = job.Error, CreatedAt = job.CreatedAt, UpdatedAt = job.UpdatedAt, CompletedAt = job.CompletedAt,
                OriginKind = job.SourceKind, OriginId = job.SourceId, WorkflowRunId = job.WorkflowRunId,
                WorkflowDefinitionId = job.WorkflowDefinitionId, DownloadResultId = job.DownloadResultId,
                AvailableActions = TransferActions(job.CanStop, job.CanResume, job.CanRetry, job.CanDelete,
                    CanResume(sourceId, job.DownloadResultId, job.WorkflowRunId, job.SourceKind, isBt: true))
            });
        }
        foreach (var job in httpJobs)
        {
            var sourceId = job.DownloadTaskId ?? (job.DownloadResultId is { } id ? resultRows.GetValueOrDefault(id)?.DownloadTaskId : null);
            snapshots.Add(new CommonTask
            {
                Id = $"http:{job.Id}", Category = "general", Provider = "http", ProviderTaskId = job.Id,
                ParentId = sourceId is { } parent && sourceRows.ContainsKey(parent) ? $"site:{parent}" : null,
                SiteTaskId = sourceId, Name = job.Name, OutputDirectory = job.OutputDirectory, Files = job.Files,
                Status = TransferStatus((int)job.Status), Stage = job.Stage, Progress = job.Percentage,
                CompletedBytes = job.CompletedBytes, TotalBytes = job.TotalBytes, SpeedBytesPerSecond = job.SpeedBytesPerSecond,
                Error = job.Error, CreatedAt = job.CreatedAt, UpdatedAt = job.UpdatedAt, CompletedAt = job.CompletedAt,
                OriginKind = job.SourceKind, OriginId = job.SourceId, WorkflowRunId = job.WorkflowRunId,
                WorkflowDefinitionId = job.WorkflowDefinitionId, DownloadResultId = job.DownloadResultId,
                AvailableActions = TransferActions(job.CanStop, job.CanResume, job.CanRetry, job.CanDelete,
                    CanResume(sourceId, job.DownloadResultId, job.WorkflowRunId, job.SourceKind))
            });
        }
        await registry.EnsureBindingsAsync(snapshots.Select(ToBinding).ToArray(), ct);
        return snapshots.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<CommonTask?> GetAsync(string id, CancellationToken ct = default) =>
        (await ListAsync(ct)).SingleOrDefault(t => t.Id == id);

    public async Task ReconcileAsync(CancellationToken ct = default) => _ = await ListAsync(ct);

    public async Task<CommonTask> CreateAsync(DownloadCenterCreateInputModel input, string outputDirectory, CancellationToken ct)
    {
        string id;
        switch (input.Provider)
        {
            case "bt":
                if (input.Url != null || input.FileName != null) throw new ArgumentException("HTTP options cannot be used for a BT task.");
                var job = await bt.EnsureQueuedAsync(new BtDownloadRequest
                { Name = input.Name, MagnetUri = input.MagnetUri, TorrentUrl = input.TorrentUrl, OutputDirectory = outputDirectory }, ct);
                await registry.EnsureBindingAsync(DownloadCenterBindings.For(job), ct);
                id = $"bt:{job.Id}";
                break;
            case "http":
                if (input.MagnetUri != null || input.TorrentUrl != null) throw new ArgumentException("BT options cannot be used for an HTTP task.");
                var httpJob = await http.EnsureQueuedAsync(new Bakabase.Modules.HttpDownloads.Models.HttpDownloadRequest
                { Name = input.Name, Url = input.Url ?? string.Empty, FileName = input.FileName, OutputDirectory = outputDirectory, HttpClientName = InternalOptions.HttpClientNames.Default }, ct);
                await registry.EnsureBindingAsync(DownloadCenterBindings.For(httpJob), ct);
                id = $"http:{httpJob.Id}";
                break;
            default: throw new ArgumentException("Choose BT or HTTP for a general download.");
        }
        return (await GetAsync(id, ct))!;
    }

    public async Task<CommonTask> UploadTorrentAsync(byte[] metadata, string outputDirectory, string? name, CancellationToken ct)
    {
        var job = await bt.EnsureQueuedAsync(new BtDownloadRequest { Name = name, Metadata = metadata, OutputDirectory = outputDirectory }, ct);
        await registry.EnsureBindingAsync(DownloadCenterBindings.For(job), ct);
        return (await GetAsync($"bt:{job.Id}", ct))!;
    }

    public async Task<CommonTask?> ExecuteAsync(string id, string action, CancellationToken ct = default,
        DownloadTaskActionOnConflict actionOnConflict = DownloadTaskActionOnConflict.Ignore)
    {
        var task = await GetAsync(id, ct) ?? throw new KeyNotFoundException("The download task does not exist.");
        await ExecuteTaskAsync(task, action, ct, actionOnConflict);
        return action == "delete" ? null : await GetAsync(id, ct);
    }

    private async Task ExecuteTaskAsync(CommonTask task, string action, CancellationToken ct,
        DownloadTaskActionOnConflict actionOnConflict)
    {
        var id = task.Id;
        if (!task.AvailableActions.Contains(action)) throw new InvalidOperationException("This action is not available for the current task. Workflow-owned tasks continue through their workflow.");
        if (task.Provider == "site")
        {
            var sourceId = task.SiteTaskId!.Value;
            switch (action)
            {
                case "start": case "retry":
                    CheckResponse(await sites.Start(t => t.Id == sourceId, actionOnConflict, targeted: true));
                    break;
                case "stop": await sites.Stop(t => t.Id == sourceId); break;
                case "delete": CheckResponse(await sites.Delete(new DownloadTaskDeleteInputModel([sourceId]))); break;
            }
        }
        else
        {
            await DownloadResultBtService.Gate.WaitAsync(ct);
            try
            {
                if (action is "start" or "retry")
                {
                    await ValidateContinuationAsync(task, ct);
                    await registry.SetUserStoppedAsync(id, false, ct);
                }
                else if (action == "stop")
                {
                    await registry.SetUserStoppedAsync(id, true, ct);
                    if (task.WorkflowRunId is { } runId) await StopWorkflowAsync(runId, ct);
                }
                if (task.Provider == "bt")
                {
                    switch (action)
                    {
                        case "start": case "retry": await bt.ResumeAsync(task.ProviderTaskId, ct); break;
                        case "stop": await bt.StopAsync(task.ProviderTaskId, ct); break;
                        case "delete": await bt.DeleteAsync(task.ProviderTaskId, ct); break;
                    }
                }
                else
                {
                    switch (action)
                    {
                        case "start": case "retry": await http.ResumeAsync(task.ProviderTaskId, ct); break;
                        case "stop": await http.StopAsync(task.ProviderTaskId, ct); break;
                        case "delete": await http.DeleteAsync(task.ProviderTaskId, ct); break;
                    }
                }
            }
            finally { DownloadResultBtService.Gate.Release(); }
        }
        if (action == "delete")
        {
            await registry.RemoveAsync(id, ct);
        }
    }

    public async Task<IReadOnlyList<DownloadTaskActionResult>> BatchAsync(DownloadCenterBatchInputModel input, CancellationToken ct)
    {
        if (input.Action is not ("start" or "stop" or "retry" or "delete")) throw new ArgumentException("Unknown download action.");
        if (input.Ids.Length == 0) throw new ArgumentException("Select at least one download task.");
        var before = (await ListAsync(ct)).ToDictionary(t => t.Id, StringComparer.Ordinal);
        var ids = input.Ids.Distinct(StringComparer.Ordinal).ToArray();
        var output = new Dictionary<string, DownloadTaskActionResult>(StringComparer.Ordinal);
        var siteTasks = ids.Where(before.ContainsKey).Select(id => before[id])
            .Where(t => t.Provider == "site" && t.AvailableActions.Contains(input.Action)).ToArray();
        // Site scheduling already has a bulk precheck and source conflict policy. Keep that single pass,
        // instead of re-running all 698 source queries once for every selected row.
        if (siteTasks.Length > 0)
        {
            var sourceIds = siteTasks.Select(t => t.SiteTaskId!.Value).ToArray();
            try
            {
                switch (input.Action)
                {
                    case "start": case "retry":
                        CheckResponse(await sites.Start(t => sourceIds.Contains(t.Id), input.ActionOnConflict, targeted: true));
                        break;
                    case "stop": await sites.Stop(t => sourceIds.Contains(t.Id)); break;
                    case "delete":
                        CheckResponse(await sites.Delete(new DownloadTaskDeleteInputModel(sourceIds)));
                        foreach (var task in siteTasks) await registry.RemoveAsync(task.Id, ct);
                        break;
                }
                foreach (var task in siteTasks) output[task.Id] = new DownloadTaskActionResult { Id = task.Id, Success = true };
            }
            catch (DownloadCenterCommandException ex)
            {
                foreach (var task in siteTasks) output[task.Id] = new DownloadTaskActionResult
                { Id = task.Id, Code = ex.Code, Error = ex.Message };
            }
        }
        var afterParents = siteTasks.Length > 0 && input.Action is "start" or "retry"
            ? (await ListAsync(ct)).ToDictionary(t => t.Id, StringComparer.Ordinal) : before;
        foreach (var id in ids.Where(id => !output.ContainsKey(id)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!afterParents.TryGetValue(id, out var task)) throw new KeyNotFoundException("The download task does not exist.");
                // A parent stop already stops its dependent transfer. Counting the selected child again
                // would incorrectly turn the parent's stop into an explicit, permanent child stop.
                if (input.Action == "stop" && task.ParentId is { } parent && output.TryGetValue(parent, out var parentResult) && parentResult.Success)
                {
                    output[id] = new DownloadTaskActionResult { Id = id, Success = true };
                    continue;
                }
                if (input.Action is "start" or "retry" && task.ParentId is { } continuedParent &&
                    output.TryGetValue(continuedParent, out var continued) && continued.Success &&
                    before.TryGetValue(id, out var prior) && prior.Status is "stopped" or "failed" &&
                    task.Status is "queued" or "running" or "completed")
                {
                    output[id] = new DownloadTaskActionResult { Id = id, Success = true };
                    continue;
                }
                await ExecuteTaskAsync(task, input.Action, ct, input.ActionOnConflict);
                output[id] = new DownloadTaskActionResult { Id = id, Success = true };
            }
            catch (DownloadCenterCommandException ex)
            { output[id] = new DownloadTaskActionResult { Id = id, Code = ex.Code, Error = ex.Message }; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
            { output[id] = new DownloadTaskActionResult { Id = id, Code = (int)ResponseCode.InvalidPayloadOrOperation, Error = ex.Message }; }
        }
        var after = (await ListAsync(ct)).ToDictionary(t => t.Id, StringComparer.Ordinal);
        return ids.Select(id => output[id] with { Task = after.GetValueOrDefault(id) }).ToArray();
    }

    private async Task ValidateContinuationAsync(CommonTask task, CancellationToken ct)
    {
        if (task.WorkflowRunId != null || task.OriginKind == "workflow")
            throw new InvalidOperationException("Continue this task through its workflow.");
        if (task.OriginKind == "manual" && task.SiteTaskId == null && task.DownloadResultId == null) return;
        var result = task.DownloadResultId is { } resultId
            ? await db.DownloadResults.AsNoTracking().SingleOrDefaultAsync(r => r.Id == resultId, ct) : null;
        var source = result == null ? null : await db.DownloadTasks.AsNoTracking().SingleOrDefaultAsync(s => s.Id == result.DownloadTaskId, ct);
        if (result == null || source == null || source.Status == DownloadTaskDbModelStatus.Disabled ||
            !DownloadTaskExecutionLifecycle.CanContinue(source, result, independentBt: task.Provider == "bt") ||
            await db.DownloadResultOwners.AnyAsync(o => o.DownloadTaskId == source.Id, ct) ||
            await db.DownloadResults.AnyAsync(r => r.DownloadTaskId == source.Id && r.SourceKey == result.SourceKey && r.Id > result.Id, ct))
            throw new InvalidOperationException("The source is stopped, removed, or no longer owns this download. Continue the source task first.");
    }

    private async Task StopWorkflowAsync(int runId, CancellationToken ct)
    {
        await backgroundTasks.Stop($"workflow.run.{runId}");
        // The running body owns its final status. Persist cancellation for a waiting or orphaned run
        // whose task has no body left to observe the cancellation signal.
        var orphaned = backgroundTasks.GetTaskViewModel($"workflow.run.{runId}") is not { } live || !live.Status.IsActive();
        await db.Set<WorkflowRunDbModel>().Where(r => r.Id == runId &&
            (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Waiting ||
             r.Status == WorkflowRunStatus.Running && orphaned))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled)
                .SetProperty(r => r.CompletedAt, DateTime.Now).SetProperty(r => r.WaitReason, (string?)null)
                .SetProperty(r => r.WaitPromptJson, (string?)null).SetProperty(r => r.WaitingSince, (DateTime?)null)
                .SetProperty(r => r.PendingSignalJson, (string?)null), ct);
    }

    private static DownloadTaskBinding ToBinding(CommonTask task) => new()
    {
        Provider = task.Provider, ProviderTaskId = task.ProviderTaskId, Category = task.Category,
        ParentId = task.ParentId, SiteTaskId = task.SiteTaskId, OriginKind = task.OriginKind, OriginId = task.OriginId,
        WorkflowRunId = task.WorkflowRunId, WorkflowDefinitionId = task.WorkflowDefinitionId,
        DownloadResultId = task.DownloadResultId, CreatedAt = task.CreatedAt
    };

    private static string SiteStatus(DownloadTaskStatus status) => status switch
    {
        DownloadTaskStatus.InQueue or DownloadTaskStatus.Idle => "queued",
        DownloadTaskStatus.Starting or DownloadTaskStatus.Downloading => "running",
        DownloadTaskStatus.Stopping => "stopping", DownloadTaskStatus.Complete => "completed",
        DownloadTaskStatus.Failed => "failed", DownloadTaskStatus.Disabled => "stopped", _ => "queued"
    };

    private static string TransferStatus(int status) => status switch
    { 1 => "queued", 2 => "running", 3 => "stopping", 4 => "stopped", 5 => "completed", 6 => "failed", _ => "queued" };

    private static IReadOnlyList<string> SiteActions(SiteTask task, string status)
    {
        var actions = new List<string>();
        if (status is "failed" or "processingFailed") actions.Add("retry");
        else if (status == "stopped" || task.CanStart && status is not ("running" or "processing" or "waiting" or "stopping")) actions.Add("start");
        if (status is "queued" or "running" or "waiting" or "processing" or "preparing" ||
            task.AvailableActions.Contains(DownloadTaskAction.Disable)) actions.Add("stop");
        actions.Add("delete");
        return actions;
    }

    private static IReadOnlyList<string> TransferActions(bool stop, bool resume, bool retry, bool delete, bool canContinue)
    {
        var actions = new List<string>();
        if (stop) actions.Add("stop");
        if (resume && canContinue) actions.Add("start");
        if (retry && canContinue) actions.Add("retry");
        if (delete) actions.Add("delete");
        return actions;
    }

    private static void CheckResponse(BaseResponse response)
    {
        if (response.Code != 0) throw new DownloadCenterCommandException(response.Code, response.Message ?? "The download action failed.");
    }
}

public sealed class DownloadCenterCommandException(int code, string message) : InvalidOperationException(message)
{
    public int Code { get; } = code;
}
