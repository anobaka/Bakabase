using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Gui;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.AspNetCore.SignalR;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask
{
    public class ResourceTaskManager
    {
        private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;

        private readonly ConcurrentDictionary<int, ResourceTaskInfo> _tasks = new();

        public ResourceTaskManager(IHubContext<WebGuiHub, IWebGuiClient> uiHub)
        {
            _uiHub = uiHub;
        }

        public async Task Add(ResourceTaskInfo task)
        {
            _tasks[task.Id] = task;
            await PushToGui(task.Id);
        }

        public ResourceTaskInfo Get(int id)
        {
            return _tasks.TryGetValue(id, out var task) ? task : null;
        }

        public async Task PushToGui(params int[] ids)
        {
            foreach (var id in ids)
            {
                await _uiHub.Clients.All.GetResourceTask(id, Get(id));
            }
        }

        public async Task Update(int[] ids, Action<ResourceTaskInfo> modify)
        {
            foreach (var id in ids)
            {
                if (_tasks.TryGetValue(id, out var task))
                {
                    modify(task);
                }
            }

            await PushToGui(ids);
        }

        public async Task Update(int id, Action<ResourceTaskInfo> modify)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                modify(task);
                await PushToGui(id);
            }
        }

        public async Task Clear(params int[] ids)
        {
            foreach (var id in ids)
            {
                _tasks.TryRemove(id, out _);
            }

            await PushToGui(ids);
        }

    }
}
