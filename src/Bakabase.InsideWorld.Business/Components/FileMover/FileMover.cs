using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.FileMover.Models;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Extensions;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.FileMover
{
    public class FileMover : IFileMover
    {
        private readonly IBOptions<FileSystemOptions> _options;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, DateTime> _creationTimeCache = new();
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

        private bool _shouldMove(string path, DateTime dtExpired)
        {
            return !_creationTimeCache.TryGetValue(path, out var dt) || dt < dtExpired;
        }

        public void TryStartMovingFiles()
        {
            var options = _options.Value.FileMover;
            if (options?.Enabled == true)
            {
                const string taskName = "Moving Files";
                if (_backgroundTaskManager.IsRunningByName(taskName))
                {
                    return;
                }

                var dtExpired = DateTime.Now - options.Delay;
                var hasSomethingToMove = options.Targets?.Any(a =>
                    a.Sources?.Where(b => b.IsNotEmpty()).Any(b =>
                        Directory.GetFileSystemEntries(b).Any(c => _shouldMove(c, dtExpired))) == true) == true;

                if (hasSomethingToMove)
                {
                    if (_backgroundTaskManager.IsRunningByName(taskName))
                    {
                        return;
                    }

                    _backgroundTaskManager.RunInBackground(taskName, new CancellationTokenSource(), async (task, sp) =>
                    {
                        _logger.LogInformation($"Discovering files to move...");
                        var totalSourceAndTargetPairCount = options.Targets?.Sum(s => s.Sources.Count) ?? 0;
                        var singleStPairPercentage =
                            totalSourceAndTargetPairCount == 0 ? 0 : 100m / totalSourceAndTargetPairCount;
                        var doneStPair = 0;
                        // source - target
                        foreach (var target in options.Targets!)
                        {
                            var targetPath = target.Path.StandardizePath()!;
                            var sources = target.Sources.Select(a => a.StandardizePath()!).ToArray();
                            Directory.CreateDirectory(targetPath);
                            foreach (var s in sources)
                            {
                                var files = Directory.GetFiles(s).Where(a => _shouldMove(a, dtExpired))
                                    .Select(a => a.StandardizePath()!).ToArray();
                                var dirs = Directory.GetDirectories(s).Where(a => _shouldMove(a, dtExpired))
                                    .Select(a => a.StandardizePath()!).ToArray();
                                var entries = dirs.Concat(files).ToArray();
                                var tasks = new Dictionary<string, string>();

                                foreach (var e in entries)
                                {
                                    var relativeName = e.Replace(s, null).Trim(Path.DirectorySeparatorChar,
                                        Path.VolumeSeparatorChar, Path.AltDirectorySeparatorChar);
                                    var targetDirectory = Path.Combine(targetPath,
                                        Path.GetDirectoryName(relativeName).StandardizePath() ?? string.Empty);
                                    tasks.Add(e, targetDirectory);
                                }

                                if (tasks.Any())
                                {
                                    _logger.LogInformation($"Found {entries.Length} items to move in top layer of {s}");
                                    var singleItemPercentage = 100m / tasks.Count;
                                    var doneItemCount = 0;

                                    var progress = Progresses.GetOrAdd(s, _ => new());
                                    progress.Moving = true;
                                    var baseStPairPercentage = doneStPair * singleStPairPercentage;
                                    try
                                    {
                                        foreach (var (source, dest) in tasks)
                                        {
                                            var count = doneItemCount;
                                            try
                                            {
                                                await DirectoryUtils.MoveAsync(source, dest, false, async p =>
                                                {
                                                    var stPairPercentage = (int) (count * singleItemPercentage +
                                                        p * singleItemPercentage / 100);

                                                    task.Percentage = (int) (baseStPairPercentage +
                                                                             stPairPercentage * singleStPairPercentage /
                                                                             100);

                                                    if (progress.Percentage != stPairPercentage)
                                                    {
                                                        await OnProgressChange();
                                                    }

                                                    progress.Percentage = stPairPercentage;
                                                }, task.Cts.Token);
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
                                        await OnProgressChange();
                                    }
                                }

                                doneStPair++;
                            }
                        }

                        return BaseResponseBuilder.Ok;
                    });
                }
            }
        }

        private async Task OnProgressChange()
        {
            await _uiHub.Clients.All.GetData(nameof(FileMovingProgress), Progresses);
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            _creationTimeCache[e.FullPath] = DateTime.Now;
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
                        watcher.Created += OnCreated;
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