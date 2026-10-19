using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Models.View;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Reads capability state without changing the platform downloader's scheduler or progress.</summary>
public sealed class DownloadTaskExecutionService(BakabaseDbContext db, IBtDownloadManager bt, BTaskManager tasks)
{
    public async Task<IReadOnlyList<DownloadTaskExecutionSummary>> GetAsync(int[] ids, CancellationToken ct)
    {
        var sources = await db.Set<DownloadTaskDbModel>().AsNoTracking()
            .Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var rows = await db.DownloadResults.AsNoTracking().Where(r => ids.Contains(r.DownloadTaskId) &&
            !db.DownloadResults.Any(newer => newer.DownloadTaskId == r.DownloadTaskId && newer.SourceKey == r.SourceKey && newer.Id > r.Id))
            .ToListAsync(ct);
        var resultIds = rows.Select(r => r.Id).ToList();
        var states = await db.DownloadResultProcessing.AsNoTracking().Where(s => resultIds.Contains(s.DownloadResultId))
            .ToDictionaryAsync(s => s.DownloadResultId, ct);
        var owners = await db.DownloadResultOwners.AsNoTracking().Where(o => ids.Contains(o.DownloadTaskId))
            .ToDictionaryAsync(o => o.DownloadTaskId, ct);
        var runIds = states.Values.Where(s => s.WorkflowRunId != null).Select(s => s.WorkflowRunId!.Value)
            .Concat(owners.Values.Select(o => o.WorkflowRunId)).Distinct().ToList();
        var runs = await db.Set<WorkflowRunDbModel>().AsNoTracking().Where(r => runIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var jobs = (await bt.ListAsync(ct)).ToDictionary(j => j.Id);
        var output = new List<DownloadTaskExecutionSummary>();
        foreach (var group in rows.Where(r => sources.TryGetValue(r.DownloadTaskId, out var source) &&
                     DownloadTaskExecutionLifecycle.CanContinue(source, r)).GroupBy(r => r.DownloadTaskId))
        {
            if (!sources.TryGetValue(group.Key, out var source)) continue;
            var relevant = group.Any(r => r.RequiresContentsDownload || r.WorkflowDefinitionId != null ||
                                         states.GetValueOrDefault(r.Id)?.BtDownloadId != null) || owners.ContainsKey(group.Key);
            if (!relevant) continue;
            var snapshots = group.Select(r =>
            {
                var state = states.GetValueOrDefault(r.Id);
                var runId = owners.GetValueOrDefault(group.Key)?.WorkflowRunId ?? state?.WorkflowRunId;
                var run = runId is { } id ? runs.GetValueOrDefault(id) : null;
                var job = state?.BtDownloadId is { } btId ? jobs.GetValueOrDefault(btId) : null;
                job ??= jobs.Values.FirstOrDefault(j => j.DownloadResultId == r.Id);
                var ready = DownloadResultContents.Resolve(r, state) != null || job?.Status == BtDownloadStatus.Completed &&
                    DownloadResultContents.Read(job.OutputDirectory, System.Text.Json.JsonSerializer.Serialize(job.Files)) != null;
                var live = run == null ? null : tasks.GetTaskViewModel($"workflow.run.{run.Id}");
                var status = "completed";
                double progress = 100;
                if (r.RequiresContentsDownload && !ready || job != null && !ready)
                {
                    status = job?.Status switch
                    {
                        BtDownloadStatus.Running => "running",
                        BtDownloadStatus.Stopping or BtDownloadStatus.Stopped => "stopped",
                        BtDownloadStatus.Failed or BtDownloadStatus.Completed => "failed",
                        _ => state?.BtDispatchError != null ? "failed" : "queued"
                    };
                    progress = job?.Percentage ?? 0;
                }
                else if (run != null)
                {
                    status = run.Status switch
                    {
                        WorkflowRunStatus.Pending => "queued",
                        WorkflowRunStatus.Running => ready ? "processing" : "running",
                        WorkflowRunStatus.Waiting => "waiting",
                        WorkflowRunStatus.Cancelled or WorkflowRunStatus.Interrupted => "stopped",
                        WorkflowRunStatus.Failed => ready ? "processingFailed" : "failed",
                        _ => "completed"
                    };
                    progress = status == "completed" ? 100 : live?.Percentage ?? 0;
                }
                else if (state?.DispatchError != null) status = ready ? "processingFailed" : "failed";
                else if (r.WorkflowDefinitionId != null && state?.FilterDidNotMatch != true)
                {
                    status = "queued";
                    progress = 0;
                }
                if (source.Status == DownloadTaskDbModelStatus.Disabled && status == "queued") status = "stopped";
                return (Status: status, Progress: progress, Speed: job?.SpeedBytesPerSecond);
            }).ToArray();
            var sourceComplete = source.Status == DownloadTaskDbModelStatus.Complete;
            var status = snapshots.Any(s => s.Status == "running") ? "running" :
                snapshots.Any(s => s.Status == "processing") ? "processing" :
                snapshots.Any(s => s.Status == "waiting") ? "waiting" :
                snapshots.Any(s => s.Status == "queued") ? "queued" :
                snapshots.Any(s => s.Status == "failed") || source.Status == DownloadTaskDbModelStatus.Failed ? "failed" :
                snapshots.Any(s => s.Status == "processingFailed") ? "processingFailed" :
                snapshots.Any(s => s.Status == "stopped") || source.Status == DownloadTaskDbModelStatus.Disabled ? "stopped" :
                sourceComplete ? "completed" : "preparing";
            output.Add(new DownloadTaskExecutionSummary
            {
                DownloadTaskId = group.Key, Status = status, TotalCount = snapshots.Length,
                CompletedCount = snapshots.Count(s => s.Status == "completed"),
                RunningCount = snapshots.Count(s => s.Status is "running" or "processing"),
                QueuedCount = snapshots.Count(s => s.Status == "queued"),
                FailedCount = snapshots.Count(s => s.Status is "failed" or "processingFailed"),
                StoppedCount = snapshots.Count(s => s.Status == "stopped"),
                WaitingCount = snapshots.Count(s => s.Status == "waiting"), SourceCompleted = sourceComplete,
                // Until discovery finishes the denominator is unknown. Do not report 100% for an unfinished list.
                Progress = sourceComplete ? snapshots.Average(s => s.Progress) : null,
                SpeedBytesPerSecond = snapshots.Any(s => s.Speed != null) ? snapshots.Sum(s => s.Speed ?? 0) : null
            });
        }
        return output;
    }
}
