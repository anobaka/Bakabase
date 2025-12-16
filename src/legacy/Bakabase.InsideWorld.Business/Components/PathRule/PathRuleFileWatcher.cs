using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.PathRule;

/// <summary>
/// Background service that watches file system changes in paths covered by PathRules.
/// Enqueues changes to the PathRuleQueue for processing.
/// </summary>
public class PathRuleFileWatcher : BackgroundService
{
    private readonly ILogger<PathRuleFileWatcher> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly MemoryCache _debounceCache = new("PathRuleFileWatcher");
    private readonly ConcurrentBag<FileChangeEvent> _pendingEvents = new();
    private readonly object _lock = new();
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(500);
    private readonly TimeSpan _flushInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);

    public PathRuleFileWatcher(
        ILogger<PathRuleFileWatcher> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PathRuleFileWatcher started");

        // Wait for application to fully start
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        // Start background task to flush events to queue
        _ = FlushEventsInBackground(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshWatchers(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing file watchers");
            }

            await Task.Delay(_refreshInterval, stoppingToken);
        }

        StopAllWatchers();
        _logger.LogInformation("PathRuleFileWatcher stopped");
    }

    private async Task RefreshWatchers(CancellationToken ct)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var pathMarkService = scope.ServiceProvider.GetRequiredService<IPathMarkService>();

        var paths = await pathMarkService.GetAllPaths();
        var activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                continue;
            }

            var normalizedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            activePaths.Add(normalizedPath);

            if (!_watchers.ContainsKey(normalizedPath))
            {
                StartWatcher(normalizedPath);
            }
        }

        // Remove watchers for paths that are no longer covered by rules
        var pathsToRemove = _watchers.Keys.Where(p => !activePaths.Contains(p)).ToList();
        foreach (var path in pathsToRemove)
        {
            StopWatcher(path);
        }

        _logger.LogDebug("Watching {Count} paths for file changes", _watchers.Count);
    }

    private void StartWatcher(string path)
    {
        try
        {
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            watcher.EnableRaisingEvents = true;

            if (_watchers.TryAdd(path, watcher))
            {
                _logger.LogInformation("Started watching path: {Path}", path);
            }
            else
            {
                watcher.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start watcher for path: {Path}", path);
        }
    }

    private void StopWatcher(string path)
    {
        if (_watchers.TryRemove(path, out var watcher))
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _logger.LogInformation("Stopped watching path: {Path}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping watcher for path: {Path}", path);
            }
        }
    }

    private void StopAllWatchers()
    {
        foreach (var path in _watchers.Keys.ToList())
        {
            StopWatcher(path);
        }
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        AddEvent(FileChangeType.Created, e.FullPath);
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        AddEvent(FileChangeType.Deleted, e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // Handle rename as delete old + create new
        AddEvent(FileChangeType.Deleted, e.OldFullPath);
        AddEvent(FileChangeType.Created, e.FullPath);
    }

    private void OnError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        _logger.LogError(ex, "File system watcher error");

        // Try to restart the watcher if possible
        if (sender is FileSystemWatcher watcher)
        {
            var path = watcher.Path;
            StopWatcher(path);
            StartWatcher(path);
        }
    }

    private void AddEvent(FileChangeType type, string path)
    {
        lock (_lock)
        {
            var key = $"{type}-{path}";
            if (!_debounceCache.Contains(key))
            {
                _debounceCache.Add(key, true, new CacheItemPolicy
                {
                    AbsoluteExpiration = DateTimeOffset.Now.Add(_debounceInterval)
                });
                _pendingEvents.Add(new FileChangeEvent(type, path, DateTime.UtcNow));
            }
        }
    }

    private async Task FlushEventsInBackground(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_flushInterval, ct);
                await FlushEvents(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing file change events");
            }
        }
    }

    private async Task FlushEvents(CancellationToken ct)
    {
        var eventsToProcess = new List<FileChangeEvent>();

        while (_pendingEvents.TryTake(out var evt))
        {
            eventsToProcess.Add(evt);
        }

        if (!eventsToProcess.Any())
        {
            return;
        }

        _logger.LogInformation("Flushing {Count} file change events to queue", eventsToProcess.Count);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IPathRuleQueueService>();

        var queueItems = eventsToProcess.Select(e => new PathRuleQueueItem
        {
            Path = e.Path,
            Action = RuleQueueAction.FileSystemChange,
            CreateDt = e.Timestamp,
            Status = RuleQueueStatus.Pending
        }).ToList();

        await queueService.EnqueueRange(queueItems);
    }

    private enum FileChangeType
    {
        Created,
        Deleted
    }

    private record FileChangeEvent(FileChangeType Type, string Path, DateTime Timestamp);
}
