using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.View;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

public class BTaskEventHandler : IBTaskEventHandler, IDisposable
{
    private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;
    private readonly IGuiAdapter _guiAdapter;
    private readonly ILogger<BTaskEventHandler> _logger;

    private readonly ConcurrentDictionary<string, BTaskViewModel> _pendingIncrementalTasks = new();
    private readonly SemaphoreSlim _allTasksLock = new(1, 1);
    private IEnumerable<BTaskViewModel>? _pendingAllTasks;
    private bool _hasAllTasksUpdate;

    private readonly Timer _flushTimer;
    private const int FlushIntervalMs = 500;

    public BTaskEventHandler(
        IHubContext<WebGuiHub, IWebGuiClient> uiHub,
        IGuiAdapter guiAdapter,
        ILogger<BTaskEventHandler> logger)
    {
        _uiHub = uiHub;
        _guiAdapter = guiAdapter;
        _logger = logger;

        _flushTimer = new Timer(FlushPendingEvents, null, FlushIntervalMs, FlushIntervalMs);
    }

    public async Task OnTaskChange(BTaskViewModel task)
    {
        // logger.LogInformation($"BTask status changed: [{task.Id}]{task.Name} -> {task.Status}", task);
        _pendingIncrementalTasks[task.Id] = task;
    }

    public async Task OnAllTasksChange(IEnumerable<BTaskViewModel> tasks)
    {
        await _allTasksLock.WaitAsync();
        try
        {
            _pendingAllTasks = tasks;
            _hasAllTasksUpdate = true;
        }
        finally
        {
            _allTasksLock.Release();
        }
    }

    public async Task OnTaskManagerStatusChange(bool isRunning)
    {
        var filename = isRunning ? "tray-running" : "favicon";
        var icon = $"Assets/{filename}.ico";
        _guiAdapter.SetTrayIcon(new Icon(icon));
    }

    private async void FlushPendingEvents(object? state)
    {
        try
        {
            // 处理全量数据更新（优先级更高）
            bool shouldSendAll = false;
            IEnumerable<BTaskViewModel>? allTasks = null;

            await _allTasksLock.WaitAsync();
            try
            {
                if (_hasAllTasksUpdate)
                {
                    shouldSendAll = true;
                    allTasks = _pendingAllTasks;
                    _hasAllTasksUpdate = false;
                    _pendingAllTasks = null;
                }
            }
            finally
            {
                _allTasksLock.Release();
            }

            if (shouldSendAll && allTasks != null)
            {
                // 合并增量更新到全量数据中，因为增量数据可能比全量快照更新
                // Merge incremental updates into full data, as incremental data may be newer than the snapshot
                if (!_pendingIncrementalTasks.IsEmpty)
                {
                    var taskList = allTasks.ToList();
                    foreach (var incrementalTask in _pendingIncrementalTasks.Values)
                    {
                        var idx = taskList.FindIndex(t => t.Id == incrementalTask.Id);
                        if (idx >= 0)
                        {
                            taskList[idx] = incrementalTask;
                        }
                        else
                        {
                            taskList.Add(incrementalTask);
                        }
                    }
                    allTasks = taskList;
                    _pendingIncrementalTasks.Clear();
                }

                await _uiHub.Clients.All.GetData("BTask", allTasks);
                return;
            }

            // 处理增量数据更新
            if (!_pendingIncrementalTasks.IsEmpty)
            {
                var tasksToSend = _pendingIncrementalTasks.Values.ToList();
                _pendingIncrementalTasks.Clear();

                foreach (var task in tasksToSend)
                {
                    await _uiHub.Clients.All.GetIncrementalData("BTask", task);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing pending BTask events");
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _allTasksLock?.Dispose();
    }
}