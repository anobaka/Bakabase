using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bootstrap.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components;

/// <summary>
/// Background service that manages path mark synchronization queue and BTask lifecycle.
/// This service is responsible for:
/// - Maintaining a queue of pending mark IDs
/// - Polling the queue periodically
/// - Managing BTask creation and lifecycle based on task status
/// </summary>
public class PathSyncManager : BackgroundService, IPathMarkSyncService
{
    private readonly ILogger<PathSyncManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly BTaskManager _btm;
    private readonly IBakabaseLocalizer _localizer;
    private readonly ConcurrentQueue<int> _pendingMarkIds = new();
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    private const string SyncTaskId = "SyncPathMarks";

    public PathSyncManager(
        ILogger<PathSyncManager> logger,
        IServiceProvider serviceProvider,
        BTaskManager btm,
        IBakabaseLocalizer localizer)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _btm = btm;
        _localizer = localizer;
    }

    /// <summary>
    /// Enqueue mark IDs for synchronization.
    /// </summary>
    public Task EnqueueSync(params int[] markIds)
    {
        foreach (var id in markIds)
        {
            _pendingMarkIds.Enqueue(id);
        }

        _logger.LogDebug("Enqueued {Count} mark IDs for synchronization", markIds.Length);
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PathSyncManager started. Poll interval: {Interval} seconds", _pollInterval.TotalSeconds);

        // Wait a bit before starting to allow the application to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Recover any marks that were left in Syncing state (e.g., due to app crash or shutdown during sync)
        await RecoverInterruptedSyncs(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueueIfNeeded(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing path sync queue");
            }

            try
            {
                await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PathSyncManager stopped");
    }

    private async Task ProcessQueueIfNeeded(CancellationToken ct)
    {
        // Check if queue is empty
        if (_pendingMarkIds.IsEmpty)
        {
            return;
        }

        // Check existing task status
        var existingTask = _btm.GetTaskViewModel(SyncTaskId);

        if (existingTask != null)
        {
            var status = existingTask.Status;

            if (status is BTaskStatus.Running or BTaskStatus.Paused or BTaskStatus.NotStarted)
            {
                // Task is active, let it continue processing
                _logger.LogDebug("Sync task is already active (status: {Status}), waiting for next poll", status);
                return;
            }

            // Task is in a terminal state (Completed, Error, Cancelled), clean it up
            _logger.LogDebug("Cleaning up completed sync task (status: {Status})", status);
            await _btm.Clean(SyncTaskId);
        }

        // Enqueue a new task
        _logger.LogInformation("Creating new sync task to process {Count} pending mark(s)", _pendingMarkIds.Count);
        await EnqueueNewSyncTask();
    }

    private async Task EnqueueNewSyncTask()
    {
        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = SyncTaskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncPathMarks"),
            GetDescription = () => _localizer.BTask_Description("SyncPathMarks"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncPathMarks"),
            ConflictKeys = [SyncTaskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore,
            Run = RunSyncLoop
        });
    }

    /// <summary>
    /// Recover marks that were left in Syncing state due to application interruption.
    /// Resets them to Pending and enqueues them for synchronization.
    /// </summary>
    private async Task RecoverInterruptedSyncs(CancellationToken ct)
    {
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();
            var pathMarkService = scope.ServiceProvider.GetRequiredService<IPathMarkService>();

            var syncingMarks = await pathMarkService.GetBySyncStatus(PathMarkSyncStatus.Syncing);

            if (syncingMarks.Count == 0)
            {
                _logger.LogDebug("No interrupted syncs to recover");
                return;
            }

            _logger.LogInformation("Found {Count} mark(s) left in Syncing state, resetting to Pending and re-queuing", syncingMarks.Count);

            var markIds = syncingMarks.Select(m => m.Id).ToList();
            await pathMarkService.MarkAsPendingBatch(markIds);

            foreach (var id in markIds)
            {
                _pendingMarkIds.Enqueue(id);
            }

            _logger.LogInformation("Successfully recovered {Count} interrupted sync(s)", syncingMarks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering interrupted syncs");
        }
    }

    private async Task RunSyncLoop(BTaskArgs args)
    {
        // 1. Drain queue
        var markIds = new List<int>();
        while (_pendingMarkIds.TryDequeue(out var id))
        {
            markIds.Add(id);
        }

        // 2. If queue is empty, task is done
        if (markIds.Count == 0)
        {
            return;
        }

        // 3. Initialize task state
        await args.UpdateTask(t =>
        {
            t.Percentage = 0;
            t.Process = null;
        });

        // 4. Call SyncMarks (handles deduplication internally)
        await using var scope = args.RootServiceProvider.CreateAsyncScope();
        var syncService = scope.ServiceProvider.GetRequiredService<PathMarkSyncService>();
        var result = await syncService.SyncMarks(
            markIds.ToArray(),
            async p => await args.UpdateTask(t => t.Percentage = p),
            async p => await args.UpdateTask(t => t.Process = p),
            args.PauseToken,
            args.CancellationToken);

        await args.UpdateTask(t => t.Data = result);
    }
}
