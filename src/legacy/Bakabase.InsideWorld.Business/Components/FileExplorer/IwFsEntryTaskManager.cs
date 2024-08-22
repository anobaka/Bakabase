using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public class IwFsEntryTaskManager
    {
        private readonly ConcurrentDictionary<string, IwFsTaskInfo> _tasks = new();
        private readonly IwFsWatcher _watcher;

        public IwFsTaskInfo[] Tasks => _tasks.Values.ToArray();

        public IwFsEntryTaskManager(IwFsWatcher watcher)
        {
            _watcher = watcher;
        }

        public async Task Add(IwFsTaskInfo task)
        {
            _tasks[task.Path] = task;
            await PushToGui(task.Path);
        }

        public IwFsTaskInfo Get(string path)
        {
            return _tasks.TryGetValue(path, out var task) ? task : null;
        }

        public async Task PushToGui(params string[] paths)
        {
            _watcher.AddEvents(paths
                .Select(p => new IwFsEntryChangeEvent(IwFsEntryChangeType.TaskChanged, p, null, Get(p))).ToArray());
        }

        public async Task Update(string[] paths, Action<IwFsTaskInfo> modify)
        {
            foreach (var path in paths)
            {
                if (_tasks.TryGetValue(path, out var task))
                {
                    modify(task);
                }
            }

            await PushToGui(paths);
        }

        public async Task Update(string path, Action<IwFsTaskInfo> modify)
        {
            if (_tasks.TryGetValue(path, out var task))
            {
                modify(task);
                await PushToGui(path);
            }
        }

        public async Task Clear(params string[] paths)
        {
            foreach (var p in paths)
            {
                _tasks.TryRemove(p, out _);
            }

            await PushToGui(paths);
        }
    }
}