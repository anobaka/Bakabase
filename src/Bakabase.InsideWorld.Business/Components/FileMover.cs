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
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components
{
    public class FileMover
    {
        private readonly IBOptions<FileSystemOptions> _options;
        private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, DateTime> _creationTimeCache = new();
        private readonly BackgroundTaskManager _backgroundTaskManager;
        private readonly ILogger<FileMover> _logger;

        public FileMover(AspNetCoreOptionsManager<FileSystemOptions> optionsManager,
            BackgroundTaskManager backgroundTaskManager, ILogger<FileMover> logger)
        {
            _options = optionsManager;
            _backgroundTaskManager = backgroundTaskManager;
            _logger = logger;
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
                        // source - target
                        var tasks = new Dictionary<string, string>();
                        foreach (var target in options.Targets!)
                        {
                            var targetPath = target.Path;
                            var sources = target.Sources;
                            Directory.CreateDirectory(targetPath);
                            foreach (var s in sources)
                            {
                                var files = Directory.GetFiles(s).Where(a => _shouldMove(a, dtExpired)).ToArray();
                                var dirs = Directory.GetDirectories(s).Where(a => _shouldMove(a, dtExpired))
                                    .ToArray();

                                foreach (var e in files.Concat(dirs))
                                {
                                    var relativeName = e.Replace(s, null).Trim(Path.DirectorySeparatorChar,
                                        Path.VolumeSeparatorChar, Path.AltDirectorySeparatorChar);
                                    var targetFullname = Path.Combine(targetPath, relativeName);
                                    tasks.Add(e, targetFullname);
                                }
                            }
                        }

                        if (tasks.Any())
                        {
                            var unitPercentage = 100m / tasks.Count;
                            var doneTaskCount = 0;
                            foreach (var (source, dest) in tasks)
                            {
                                await DirectoryUtils.MoveAsync(source, dest, false, p =>
                                {
                                    task.Percentage = (int) (doneTaskCount * unitPercentage + unitPercentage * p / 100);
                                    return Task.CompletedTask;
                                }, task.Cts.Token);
                                doneTaskCount++;
                            }
                        }

                        return BaseResponseBuilder.Ok;
                    });
                }
            }
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
                        new string[] { };
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
                    }
                }
            }
        }
    }
}