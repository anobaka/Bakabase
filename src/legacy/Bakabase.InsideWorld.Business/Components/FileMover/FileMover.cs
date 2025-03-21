using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.FileMover.Models;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Components.Tasks;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using static Bakabase.InsideWorld.Models.Configs.FileSystemOptions;

namespace Bakabase.InsideWorld.Business.Components.FileMover
{
    public class FileMover : IFileMover
    {
        private readonly IBOptions<FileSystemOptions> _options;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
        private readonly object _lock = new object();
        protected readonly ConcurrentDictionary<string, DateTime> CreationTimeCache = new();
        private readonly BackgroundTaskManager _backgroundTaskManager;
        private readonly ILogger<FileMover> _logger;
        public ConcurrentDictionary<string, FileMovingProgress> Progresses { get; } = new();
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;

        public FileMover(AspNetCoreOptionsManager<FileSystemOptions> optionsManager,
            BackgroundTaskManager backgroundTaskManager, ILogger<FileMover> logger,
            IHubContext<WebGuiHub, IWebGuiClient> uiHub)
        {
            _options = optionsManager;
            _backgroundTaskManager = backgroundTaskManager;
            _logger = logger;
            _uiHub = uiHub;
            optionsManager.OnChange(fsOptions =>
            {
                if (fsOptions.FileMover != null)
                {
                    OnOptionsChange(fsOptions.FileMover);
                }
            });
        }

        private bool _shouldMove(string standardPath, DateTime maxCreatedAt)
        {
            var s = !CreationTimeCache.TryGetValue(standardPath, out var dt) || dt < maxCreatedAt;
            return s;
        }

        protected async Task MoveInternal(FileMoverOptions options, Func<int, Task>? onGlobalProgressChange, PauseToken pt, CancellationToken ct)
        {
            _logger.LogInformation($"Discovering files to move...");

            var targets = options.Targets ?? [];
            var totalSourceAndTargetPairCount = targets.Sum(s => s.Sources.Count);
            var singleStPairPercentage = totalSourceAndTargetPairCount == 0 ? 0 : 100m / totalSourceAndTargetPairCount;
            var doneStPair = 0;

            var maxCreatedAt = DateTime.Now - options.Delay;

            // source - target
            foreach (var target in targets)
            {
                var targetPath = target.Path.StandardizePath()!;
                var sources = target.Sources.Select(a => a.StandardizePath()!).ToArray();
                Directory.CreateDirectory(targetPath);
                foreach (var s in sources)
                {
                    var files = Directory.GetFiles(s).Select(a => a.StandardizePath()!).Where(a => _shouldMove(a, maxCreatedAt))
                        .ToArray();
                    var dirs = Directory.GetDirectories(s).Select(a => a.StandardizePath()!).Where(a => _shouldMove(a, maxCreatedAt))
                        .ToArray();
                    var entries = dirs.Concat(files).ToArray();
                    var tasks = new Dictionary<string, string>();

                    var fileSet = files.ToHashSet();

                    foreach (var e in entries)
                    {
                        var relativeName = e.Replace(s, null).Trim(Path.DirectorySeparatorChar,
                            Path.VolumeSeparatorChar, Path.AltDirectorySeparatorChar);
                        var finalPath = Path.Combine(targetPath, relativeName).StandardizePath()!;
                        tasks.Add(e, finalPath);
                    }

                    if (tasks.Any())
                    {
                        _logger.LogInformation($"Found {entries.Length} items to move in top level of {s}");
                        var singleItemPercentage = 100m / tasks.Count;
                        var doneItemCount = 0;

                        var progress = Progresses.GetOrAdd(s, _ => new());
                        progress.Moving = true;
                        var baseStPairPercentage = doneStPair * singleStPairPercentage;

                        var globalPercentage = 0;

                        try
                        {
                            foreach (var (source, dest) in tasks)
                            {
                                await pt.WaitWhilePausedAsync();
                                ct.ThrowIfCancellationRequested();
                                var count = doneItemCount;
                                try
                                {
                                    async Task ProgressChange(int p)
                                    {
                                        var stPairPercentage = (int) (count * singleItemPercentage +
                                                                      p * singleItemPercentage / 100);

                                        var newGlobalPercentage = (int) (baseStPairPercentage + stPairPercentage * singleStPairPercentage / 100);
                                        if (globalPercentage != newGlobalPercentage)
                                        {
                                            globalPercentage = newGlobalPercentage;
                                            if (onGlobalProgressChange != null)
                                            {
                                                await onGlobalProgressChange(globalPercentage);
                                            }
                                        }

                                        if (progress.Percentage != stPairPercentage)
                                        {
                                            await OnInternalProgressChange();
                                        }

                                        progress.Percentage = stPairPercentage;
                                    }

                                    if (fileSet.Contains(source))
                                    {
                                        await FileUtils.MoveAsync(source, dest, false, (Func<int, Task>) ProgressChange,
                                            pt, ct);
                                    }
                                    else
                                    {
                                        await DirectoryUtils.MoveAsync(source, dest, false,
                                            (Func<int, Task>) ProgressChange, pt, ct);
                                    }

                                    doneItemCount++;
                                }
                                catch (Exception e)
                                {
                                    progress.Error = $"{e.Message}{Environment.NewLine}{e.StackTrace}";
                                    _logger.LogError(e,
                                        $"An error occurred while moving files: {e.Message}");
                                    throw;
                                }
                            }
                        }
                        finally
                        {
                            progress.Moving = false;
                            await OnInternalProgressChange();
                        }
                    }

                    doneStPair++;
                }
            }
        }

        public async Task MovingFiles(Func<int, Task>? onProgressChange, PauseToken pt, CancellationToken ct)
        {
            var options = _options.Value.FileMover;
            if (options?.Enabled == true)
            {
                var dtExpired = DateTime.Now - options.Delay;
                var hasSomethingToMove = options.Targets?.Any(a =>
                    a.Sources?.Where(b => b.IsNotEmpty()).Any(b =>
                        Directory.GetFileSystemEntries(b).Any(c => _shouldMove(c, dtExpired))) == true) == true;

                if (hasSomethingToMove)
                {
                    await MoveInternal(options, onProgressChange, pt, ct);
                }
            }
        }

        private async Task OnInternalProgressChange()
        {
            await _uiHub.Clients.All.GetData(nameof(FileMovingProgress), Progresses);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            CreationTimeCache[e.FullPath.StandardizePath()!] = DateTime.Now;
        }

        private void OnOptionsChange(FileSystemOptions.FileMoverOptions options)
        {
            lock (_lock)
            {
                string[] invalidSources;
                var newSources = Array.Empty<string>();
                if (options?.Enabled == true)
                {
                    var sources =
                        options.Targets?.SelectMany(a => a.Sources).Distinct().ToArray() ??
                        Array.Empty<string>();
                    invalidSources = _watchers.Keys.Except(sources).ToArray();
                    newSources = sources.Except(_watchers.Keys).Distinct().ToArray();
                }
                else
                {
                    invalidSources = _watchers.Keys.ToArray();
                }

                if (invalidSources.Any())
                {
                    foreach (var k in invalidSources)
                    {
                        if (_watchers.TryGetValue(k, out var watcher))
                        {
                            watcher!.Dispose();
                        }

                        Progresses.TryRemove(k, out _);
                    }
                }

                if (newSources.Any())
                {
                    foreach (var k in newSources)
                    {
                        var watcher = new FileSystemWatcher()
                        {
                            Path = k,
                            // Filter = "*", 
                            IncludeSubdirectories = false
                        };
                        // Add event handlers for all events you want to handle
                        watcher.Created += OnChanged;
                        watcher.Changed += OnChanged;
                        watcher.Renamed += OnChanged;
                        // Activate the watcher
                        watcher.EnableRaisingEvents = true;
                        _watchers[k] = watcher;

                        Progresses.GetOrAdd(k, _ => new());
                    }
                }
            }
        }
    }
}