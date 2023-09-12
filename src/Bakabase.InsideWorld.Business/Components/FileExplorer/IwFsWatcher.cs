using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bootstrap.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public class IwFsWatcher
    {
        private readonly ILogger<IwFsWatcher> _logger;
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _hubContext;
        private const int RaiseIntervalMs = 500;

        private readonly MemoryCache _memCache = new("IwFsWatcher");

        private ConcurrentBag<IwFsEntryChangeEvent> _pendingEvents = new();
        private readonly ConcurrentBag<IwFsEntryChangeEvent> _sendingEvents = new();
        private readonly object _lock = new object();

        private CancellationTokenSource? _cts;

        public IwFsWatcher(ILogger<IwFsWatcher> logger,
            IHubContext<WebGuiHub, IWebGuiClient> hubContext)
        {
            _logger = logger;
            _hubContext = hubContext;
        }

        public void Start(string path)
        {
            Stop();

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var watcher = new FileSystemWatcher(path)
            {
                IncludeSubdirectories = true,
                // Filter = null,
                InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                // | NotifyFilters.Attributes
                // | NotifyFilters.CreationTime
                // | NotifyFilters.LastAccess
                // | NotifyFilters.LastWrite
                // | NotifyFilters.Security
                // | NotifyFilters.Size
                ,
                // Site = null,
                // SynchronizingObject = null
            };

            // _watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;

            watcher.Error += OnError;
            watcher.Disposed += (sender, args) => { _logger.LogInformation($"[2]Watcher for [{watcher.Path}] is disposed"); };

            SendDataInBackground(ct);
            watcher.EnableRaisingEvents = true;

            ct.Register(() =>
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    _logger.LogInformation("[1]Disposing watcher");
                    watcher.Dispose();
                    _logger.LogInformation("[3]Disposed watcher");
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "[-1]Error disposing watcher");
                }

                _logger.LogInformation("[4]After disposing watcher");

                _pendingEvents.Clear();
                _sendingEvents.Clear();
            });
        }

        private void SendDataInBackground(CancellationToken ct)
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    Interlocked.Exchange(ref _pendingEvents, _sendingEvents);

                    if (_sendingEvents.Any())
                    {
                        var events = _sendingEvents.OrderBy(a => a.ChangedAt).ToList();
                        _sendingEvents.Clear();
                        _logger.LogInformation($"Sending events {JsonConvert.SerializeObject(events)}");
                        await _hubContext.Clients.All.IwFsEntriesChange(events, ct);
                    }

                    await Task.Delay(RaiseIntervalMs, ct);
                }
            }, ct);
        }

        // private void OnChanged(object sender, FileSystemEventArgs e)
        // {
        //     if (e.ChangeType != WatcherChangeTypes.Changed)
        //     {
        //         return;
        //     }
        //
        //     _hubContext.Clients.All.IwFsEntryChange(IwFsEntryChangeType.Changed, new IwFsEntry { });
        // }

        public void AddEvents(params IwFsEntryChangeEvent[] events)
        {
            lock (_lock)
            {
                foreach (var e in events)
                {
                    var key = $"{e.Type}-{e.PrevPath ?? e.Path}-{e.Task?.BackgroundTaskId}-{e.Task?.Percentage / 5}-{e.Task?.Error}";
                    if (!_memCache.Contains(key))
                    {
                        _memCache.Add(key, e, new CacheItemPolicy
                        {
                            AbsoluteExpiration = DateTimeOffset.Now.AddSeconds(0.5)
                        });
                        _pendingEvents.Add(e);
#if DEBUG
                        // _logger.LogInformation(JsonConvert.SerializeObject(events));
#endif
                    }
                }
            }
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Created, e.FullPath));
        }

        private void OnDeleted(object sender, FileSystemEventArgs e)
        {
            AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Deleted, e.FullPath));
        }

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            AddEvents(new IwFsEntryChangeEvent(IwFsEntryChangeType.Renamed, e.FullPath, e.OldFullPath));
        }

        private void OnError(object sender, ErrorEventArgs e) => PrintException(e.GetException());

        private void PrintException(Exception? ex)
        {
            if (ex != null)
            {
                _logger.LogError(ex, "An error occurred during watching changes of file system entries");
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}