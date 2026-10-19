using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Downloader.Components.Downloaders.ExHentai;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Bittorrent.Abstractions;
using Bakabase.Modules.Bittorrent.Models;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Downloader;

/// <summary>Coordinates user start/stop intent across source tasks and their independently managed executions.</summary>
public sealed class DownloadTaskExecutionLifecycle(IServiceScopeFactory scopes, IBtDownloadManager downloads,
    BTaskManager backgroundTasks, ILogger<DownloadTaskExecutionLifecycle> logger) : IDownloadTaskExecutionLifecycle
{
    public async Task OnStoppedAsync(IReadOnlyCollection<int> taskIds)
    {
        if (taskIds.Count == 0) return;
        await DownloadResultBtService.Gate.WaitAsync();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var independent = await IndependentTaskIds(db, taskIds);
            var resultIds = await db.DownloadResults.Where(r => independent.Contains(r.DownloadTaskId))
                .Select(r => r.Id).ToListAsync();
            var jobs = (await downloads.ListAsync()).Where(j => j.DownloadTaskId is { } taskId && independent.Contains(taskId) ||
                j.DownloadResultId is { } resultId && resultIds.Contains(resultId)).ToArray();
            var runIds = await db.DownloadResultProcessing.Where(p => resultIds.Contains(p.DownloadResultId) && p.WorkflowRunId != null)
                .Select(p => p.WorkflowRunId!.Value).ToListAsync();
            runIds.AddRange(jobs.Where(j => j.WorkflowRunId != null).Select(j => j.WorkflowRunId!.Value));
            runIds = runIds.Distinct().ToList();
            foreach (var runId in runIds) await backgroundTasks.Stop($"workflow.run.{runId}");
            foreach (var job in jobs.Where(j => j.CanStop || j.Status == BtDownloadStatus.Stopping))
                await downloads.StopAsync(job.Id);
            foreach (var runId in runIds) await backgroundTasks.WaitForExecutionAsync($"workflow.run.{runId}");

            // A waiting/pending run may have no active BTask body to record cancellation for it.
            // Running bodies retain ownership of their final status until they observe cancellation.
            var orphaned = runIds.Where(id => backgroundTasks.GetTaskViewModel($"workflow.run.{id}") is not { } task ||
                                             !task.Status.IsActive()).ToArray();
            await db.Set<WorkflowRunDbModel>().Where(r => runIds.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Waiting ||
                 r.Status == WorkflowRunStatus.Running && orphaned.Contains(r.Id)))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, WorkflowRunStatus.Cancelled)
                    .SetProperty(r => r.CompletedAt, DateTime.Now)
                    .SetProperty(r => r.WaitReason, (string?)null)
                    .SetProperty(r => r.WaitPromptJson, (string?)null)
                    .SetProperty(r => r.WaitingSince, (DateTime?)null)
                    .SetProperty(r => r.PendingSignalJson, (string?)null));
        }
        finally { DownloadResultBtService.Gate.Release(); }
    }

    public async Task OnStartedAsync(IReadOnlyCollection<int> taskIds)
    {
        if (taskIds.Count == 0 || backgroundTasks.IsShuttingDown) return;
        var retries = new List<int>();
        await DownloadResultBtService.Gate.WaitAsync();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
            var independent = await IndependentTaskIds(db, taskIds);
            var tasks = await db.Set<DownloadTaskDbModel>().AsNoTracking()
                .Where(t => independent.Contains(t.Id) && t.Status != DownloadTaskDbModelStatus.Disabled)
                .ToDictionaryAsync(t => t.Id);
            var currentTaskIds = tasks.Keys.ToArray();
            var results = await db.DownloadResults.AsNoTracking().Where(r => currentTaskIds.Contains(r.DownloadTaskId) &&
                !db.DownloadResults.Any(newer => newer.DownloadTaskId == r.DownloadTaskId && newer.SourceKey == r.SourceKey && newer.Id > r.Id))
                .ToListAsync();
            var allowed = results.Where(r => CanContinue(tasks[r.DownloadTaskId], r)).ToDictionary(r => r.Id);
            var held = await db.ManagedDownloadTasks.AsNoTracking().Where(t => t.UserStopped)
                .Select(t => new {t.Provider, t.ProviderTaskId, t.DownloadResultId}).ToListAsync();
            foreach (var resultId in held.Where(t => t.DownloadResultId.HasValue).Select(t => t.DownloadResultId!.Value))
                allowed.Remove(resultId);
            var heldBtIds = held.Where(t => t.Provider == "bt").Select(t => t.ProviderTaskId).ToHashSet();
            var jobs = (await downloads.ListAsync()).Where(j => j.WorkflowRunId == null && j.SourceKind != "workflow" &&
                !heldBtIds.Contains(j.Id) && j.DownloadResultId is { } id && allowed.ContainsKey(id)).ToArray();
            foreach (var job in jobs.Where(j => j.Status is BtDownloadStatus.Stopped or BtDownloadStatus.Failed))
                await downloads.ResumeAsync(job.Id);
            var allowedIds = allowed.Keys.ToArray();
            var states = await db.DownloadResultProcessing.Where(p => allowedIds.Contains(p.DownloadResultId))
                .ToDictionaryAsync(p => p.DownloadResultId);
            var stateRunIds = states.Values.Where(s => s.WorkflowRunId != null).Select(s => s.WorkflowRunId!.Value).ToArray();
            var activeRunIds = await db.Set<WorkflowRunDbModel>().Where(r => stateRunIds.Contains(r.Id) &&
                (r.Status == WorkflowRunStatus.Pending || r.Status == WorkflowRunStatus.Running || r.Status == WorkflowRunStatus.Waiting))
                .Select(r => r.Id).ToListAsync();
            foreach (var job in jobs.Where(j => j.Status == BtDownloadStatus.Completed))
            {
                var result = allowed[job.DownloadResultId!.Value];
                var state = states.GetValueOrDefault(result.Id);
                // A processing step may currently be moving files and has not published their new location yet.
                if (state?.WorkflowRunId is { } runId && activeRunIds.Contains(runId)) continue;
                if (DownloadResultContents.Resolve(result, state) != null ||
                    DownloadResultContents.Read(job.OutputDirectory, JsonSerializer.Serialize(job.Files)) != null) continue;
                await downloads.RecheckAsync(job.Id);
                if (state == null) continue;
                state.ContentsDirectory = null;
                state.ContentsFilesJson = null;
                state.ContentsReadyAt = null;
                state.BtDispatchError = null;
            }
            await db.SaveChangesAsync();
            retries = await db.DownloadResultProcessing.Where(p => allowedIds.Contains(p.DownloadResultId) && p.WorkflowRunId != null)
                .Join(db.Set<WorkflowRunDbModel>(), p => p.WorkflowRunId, r => r.Id, (p, r) => new { State = p, Run = r })
                .Where(x => x.Run.Status == WorkflowRunStatus.Failed || x.Run.Status == WorkflowRunStatus.Cancelled ||
                            x.Run.Status == WorkflowRunStatus.Interrupted)
                .Select(x => x.State.DownloadResultId).ToListAsync();
        }
        finally { DownloadResultBtService.Gate.Release(); }

        // Retry owns the same gate and revalidates the workflow/source before requeueing it.
        foreach (var resultId in retries)
        {
            await using var scope = scopes.CreateAsyncScope();
            try { await scope.ServiceProvider.GetRequiredService<DownloadResultWorkflowService>().RetryAsync(resultId); }
            catch (InvalidOperationException ex)
            {
                var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
                db.ChangeTracker.Clear();
                await db.DownloadResultProcessing.Where(p => p.DownloadResultId == resultId)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.DispatchError, ex.Message));
                logger.LogWarning(ex, "Could not continue the workflow for download result {ResultId}", resultId);
            }
        }
    }

    private static Task<List<int>> IndependentTaskIds(BakabaseDbContext db, IReadOnlyCollection<int> taskIds) =>
        db.Set<DownloadTaskDbModel>().Where(t => taskIds.Contains(t.Id) &&
            !db.DownloadResultOwners.Any(o => o.DownloadTaskId == t.Id)).Select(t => t.Id).ToListAsync();

    internal static bool CanContinue(DownloadTaskDbModel task, DownloadResultDbModel result)
    {
        if (task.ThirdPartyId != ThirdPartyId.ExHentai) return true;
        ExHentaiTaskOptions options;
        try { options = string.IsNullOrWhiteSpace(task.Options) ? new() : JsonSerializer.Deserialize<ExHentaiTaskOptions>(task.Options, JsonSerializerOptions.Web) ?? new(); }
        catch (JsonException) { return false; }
        return result.Kind != DownloadResultKind.TorrentMetadata || options.EffectiveDownloadMode == ExHentaiDownloadMode.Torrent ||
               options.EffectiveDownloadMode == ExHentaiDownloadMode.TorrentFile &&
               !result.RequiresContentsDownload && result.WorkflowDefinitionId != null;
    }
}
