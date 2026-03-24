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
/// Background service that manages resource sync and property sync queues.
///
/// Two independent queues:
/// 1. Resource sync queue: ResourceSource values, aggregated (deduplicates same source).
///    When resource sync completes with new resources, it marks related path marks as pending,
///    which triggers the path mark sync queue.
/// 2. Path mark sync queue: Triggers PathMarkSyncService when pending path marks exist.
///
/// Flow:
/// - User syncs specific marks -> marks set to pending -> path mark sync triggers
/// - User syncs a source -> resource sync triggers -> new resources found ->
///   related marks set to pending -> path mark sync triggers automatically
/// </summary>
public class PathSyncManager : BackgroundService, IPathMarkSyncService
{
    private readonly ILogger<PathSyncManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly BTaskManager _btm;
    private readonly IBakabaseLocalizer _localizer;

    /// <summary>
    /// Resource sync queue. Uses ConcurrentDictionary for deduplication (only one entry per source).
    /// </summary>
    private readonly ConcurrentDictionary<ResourceSource, byte> _pendingResourceSources = new();

    /// <summary>
    /// Flag indicating that path mark sync should run (pending property/media library marks exist).
    /// </summary>
    private volatile bool _pathMarkSyncRequested;

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    private const string ResourceSyncTaskId = "SyncResources";
    private const string PathMarkSyncTaskId = "SyncPathMarks";

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
    /// Marks the specified marks as pending and requests path mark sync.
    /// If markIds is empty, loads all pending marks from the database.
    /// </summary>
    public async Task EnqueueSync(params int[] markIds)
    {
        if (markIds.Length == 0)
        {
            // All pending marks - just trigger path mark sync
            _pathMarkSyncRequested = true;
            _logger.LogDebug("Requested path mark sync for all pending marks");
            return;
        }

        // Mark specific marks as pending
        await using var scope = _serviceProvider.CreateAsyncScope();
        var pathMarkService = scope.ServiceProvider.GetRequiredService<IPathMarkService>();
        await pathMarkService.MarkAsPendingBatch(markIds);

        // Check if any of these are resource marks - if so, also enqueue FileSystem resource sync
        var marks = await pathMarkService.GetAll();
        var hasResourceMarks = marks
            .Where(m => markIds.Contains(m.Id))
            .Any(m => m.Type == PathMarkType.Resource);

        if (hasResourceMarks)
        {
            _pendingResourceSources.TryAdd(ResourceSource.FileSystem, 0);
            _logger.LogDebug("Enqueued FileSystem resource sync for resource marks");
        }

        // Always request path mark sync for property/media library marks
        _pathMarkSyncRequested = true;
        _logger.LogDebug("Enqueued {Count} mark IDs for synchronization", markIds.Length);
    }

    /// <summary>
    /// Enqueue a source-based sync request.
    /// For any source, this adds to the resource sync queue (with deduplication).
    /// </summary>
    public async Task EnqueueSync(ResourceSource source, params int[] markIds)
    {
        if (markIds.Length > 0)
        {
            // Mark specific marks as pending first
            await using var scope = _serviceProvider.CreateAsyncScope();
            var pathMarkService = scope.ServiceProvider.GetRequiredService<IPathMarkService>();
            await pathMarkService.MarkAsPendingBatch(markIds);
        }

        // Add source to resource sync queue (deduplicates automatically)
        _pendingResourceSources.TryAdd(source, 0);
        _logger.LogDebug("Enqueued {Source} source for resource synchronization", source);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PathSyncManager started. Poll interval: {Interval} seconds",
            _pollInterval.TotalSeconds);

        // Wait a bit before starting to allow the application to fully initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        // Recover any marks that were left in Syncing state
        await RecoverInterruptedSyncs(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessQueuesIfNeeded(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing sync queues");
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

    private async Task ProcessQueuesIfNeeded(CancellationToken ct)
    {
        // Process resource sync queue
        if (!_pendingResourceSources.IsEmpty)
        {
            await ProcessResourceSyncQueue(ct);
        }

        // Process path mark sync queue
        if (_pathMarkSyncRequested)
        {
            await ProcessPathMarkSyncQueue(ct);
        }
    }

    private async Task ProcessResourceSyncQueue(CancellationToken ct)
    {
        // Check if resource sync task is already running
        var existingTask = _btm.GetTaskViewModel(ResourceSyncTaskId);
        if (existingTask != null)
        {
            var status = existingTask.Status;
            if (status is BTaskStatus.Running or BTaskStatus.Paused or BTaskStatus.NotStarted)
            {
                return;
            }

            await _btm.Clean(ResourceSyncTaskId);
        }

        // Drain all pending sources
        var sources = new HashSet<ResourceSource>();
        foreach (var key in _pendingResourceSources.Keys.ToList())
        {
            if (_pendingResourceSources.TryRemove(key, out _))
            {
                sources.Add(key);
            }
        }

        if (sources.Count == 0) return;

        _logger.LogInformation("Creating resource sync task for sources: {Sources}",
            string.Join(", ", sources));

        var capturedSources = sources.ToList();

        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = ResourceSyncTaskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncResources"),
            GetDescription = () => _localizer.BTask_Description("SyncResources"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncResources"),
            ConflictKeys = [ResourceSyncTaskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore,
            Run = async args => await RunResourceSync(args, capturedSources)
        });
    }

    private async Task ProcessPathMarkSyncQueue(CancellationToken ct)
    {
        // Check if path mark sync task is already running
        var existingTask = _btm.GetTaskViewModel(PathMarkSyncTaskId);
        if (existingTask != null)
        {
            var status = existingTask.Status;
            if (status is BTaskStatus.Running or BTaskStatus.Paused or BTaskStatus.NotStarted)
            {
                return;
            }

            await _btm.Clean(PathMarkSyncTaskId);
        }

        _pathMarkSyncRequested = false;

        _logger.LogInformation("Creating path mark sync task");

        await _btm.Enqueue(new BTaskHandlerBuilder
        {
            Id = PathMarkSyncTaskId,
            Type = BTaskType.Any,
            ResourceType = BTaskResourceType.Any,
            GetName = () => _localizer.BTask_Name("SyncPathMarks"),
            GetDescription = () => _localizer.BTask_Description("SyncPathMarks"),
            GetMessageOnInterruption = () => _localizer.BTask_MessageOnInterruption("SyncPathMarks"),
            ConflictKeys = [PathMarkSyncTaskId],
            Level = BTaskLevel.Default,
            IsPersistent = false,
            StartNow = true,
            DuplicateIdHandling = BTaskDuplicateIdHandling.Ignore,
            Run = RunPathMarkSync
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

            _logger.LogInformation(
                "Found {Count} mark(s) left in Syncing state, resetting to Pending and re-queuing",
                syncingMarks.Count);

            var markIds = syncingMarks.Select(m => m.Id).ToList();
            await pathMarkService.MarkAsPendingBatch(markIds);

            // Check if any are resource marks
            if (syncingMarks.Any(m => m.Type == PathMarkType.Resource))
            {
                _pendingResourceSources.TryAdd(ResourceSource.FileSystem, 0);
            }

            // Request path mark sync for property/media library marks
            if (syncingMarks.Any(m => m.Type is PathMarkType.Property or PathMarkType.MediaLibrary))
            {
                _pathMarkSyncRequested = true;
            }

            _logger.LogInformation("Successfully recovered {Count} interrupted sync(s)", syncingMarks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering interrupted syncs");
        }
    }

    /// <summary>
    /// Runs resource sync for the given sources.
    /// After each source sync, if new resources were created, path mark sync is triggered automatically.
    /// </summary>
    private async Task RunResourceSync(BTaskArgs args, List<ResourceSource> sources)
    {
        await using var scope = args.RootServiceProvider.CreateAsyncScope();
        var resourceSyncService = scope.ServiceProvider.GetRequiredService<ResourceSyncService>();

        for (var i = 0; i < sources.Count; i++)
        {
            args.CancellationToken.ThrowIfCancellationRequested();

            var source = sources[i];
            var baseProgress = (int)(100.0 * i / sources.Count);
            var progressRange = (int)(100.0 / sources.Count);

            var result = await resourceSyncService.SyncResources(
                source,
                async p => await args.UpdateTask(t => t.Percentage = baseProgress + p * progressRange / 100),
                async p => await args.UpdateTask(t => t.Process = p),
                args.PauseToken,
                args.CancellationToken);

            await args.UpdateTask(t => t.Data = result);

            // If new resources were created or marks were marked pending, trigger path mark sync
            if (result.PathMarksMarkedPending)
            {
                _pathMarkSyncRequested = true;
                _logger.LogInformation(
                    "[ResourceSync] New resources from {Source}, path mark sync will be triggered", source);
            }
        }

        // Check if any new sources were added while we were running
        if (!_pendingResourceSources.IsEmpty)
        {
            // Re-add them so they get picked up on next poll
            _logger.LogDebug("Additional resource sources were queued during sync, will process on next poll");
        }
    }

    /// <summary>
    /// Runs path mark sync for pending property/media library marks.
    /// </summary>
    private async Task RunPathMarkSync(BTaskArgs args)
    {
        await args.UpdateTask(t =>
        {
            t.Percentage = 0;
            t.Process = null;
        });

        await using var scope = args.RootServiceProvider.CreateAsyncScope();
        var syncService = scope.ServiceProvider.GetRequiredService<PathMarkSyncService>();

        var result = await syncService.SyncMarks(
            async p => await args.UpdateTask(t => t.Percentage = p),
            async p => await args.UpdateTask(t => t.Process = p),
            args.PauseToken,
            args.CancellationToken);

        await args.UpdateTask(t => t.Data = result);
    }
}
