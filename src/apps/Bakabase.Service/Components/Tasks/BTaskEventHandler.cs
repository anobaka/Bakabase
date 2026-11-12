using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

public class BTaskEventHandler : IBTaskEventHandler, IDisposable
{
    private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;
    private readonly ITrayIconController? _trayIconController;
    private readonly ILogger<BTaskEventHandler> _logger;

    private readonly object _pendingLock = new();
    private Dictionary<string, BTaskViewModel> _pendingIncrementalTasks = new(StringComparer.Ordinal);
    private BTaskViewModel[]? _pendingAllTasks;
    private int _flushing;
    private bool _disposed;

    private readonly Timer _flushTimer;
    private const int FlushIntervalMs = 500;

    public BTaskEventHandler(
        IHubContext<WebGuiHub, IWebGuiClient> uiHub,
        ILogger<BTaskEventHandler> logger,
        ITrayIconController? trayIconController = null)
        : this(uiHub, logger, trayIconController, true) { }

    internal BTaskEventHandler(IHubContext<WebGuiHub, IWebGuiClient> uiHub,
        ILogger<BTaskEventHandler> logger, ITrayIconController? trayIconController, bool startTimer)
    {
        _uiHub = uiHub;
        _logger = logger;
        _trayIconController = trayIconController;

        _flushTimer = new Timer(FlushPendingEvents, null,
            startTimer ? FlushIntervalMs : Timeout.Infinite, startTimer ? FlushIntervalMs : Timeout.Infinite);
    }

    public Task OnTaskChange(BTaskViewModel task)
    {
        // logger.LogInformation($"BTask status changed: [{task.Id}]{task.Name} -> {task.Status}", task);
        lock (_pendingLock)
            if (!_disposed) _pendingIncrementalTasks[task.Id] = task;
        return Task.CompletedTask;
    }

    public Task OnAllTasksChange(IEnumerable<BTaskViewModel> tasks)
    {
        lock (_pendingLock)
            if (!_disposed)
            {
                _pendingAllTasks = tasks.ToArray();
                // The complete snapshot supersedes all earlier incremental updates,
                // including tasks removed from it. Only later updates may overlay it.
                _pendingIncrementalTasks.Clear();
            }
        return Task.CompletedTask;
    }

    public Task OnTaskManagerStatusChange(bool isRunning)
    {
        _trayIconController?.SetTrayIcon(isRunning);
        return Task.CompletedTask;
    }

    private void FlushPendingEvents(object? state) => _ = FlushPendingEventsAsync();

    internal async Task FlushPendingEventsAsync()
    {
        // Timer callbacks can overlap while SignalR awaits a slow client. Keep their
        // sends ordered, so an earlier progress batch cannot arrive after completion.
        if (Interlocked.CompareExchange(ref _flushing, 1, 0) != 0) return;
        BTaskViewModel[]? allTasks = null;
        BTaskViewModel[] incremental = [];
        var sent = 0;
        try
        {
            lock (_pendingLock)
            {
                if (_disposed) return;
                allTasks = _pendingAllTasks;
                if (allTasks != null)
                {
                    var latest = allTasks.ToDictionary(t => t.Id, StringComparer.Ordinal);
                    foreach (var task in _pendingIncrementalTasks.Values) latest[task.Id] = task;
                    allTasks = latest.Values.ToArray();
                    _pendingAllTasks = null;
                }
                else incremental = _pendingIncrementalTasks.Values.ToArray();
                // Producers now write into a different buffer. Never clear a live
                // concurrent collection after enumerating it.
                _pendingIncrementalTasks = new(StringComparer.Ordinal);
            }
            if (allTasks != null)
            {
                await _uiHub.Clients.All.GetData("BTask", allTasks);
            }
            else for (; sent < incremental.Length; sent++)
                await _uiHub.Clients.All.GetIncrementalData("BTask", incremental[sent]);
        }
        catch (Exception ex)
        {
            // Keep an unsent completion/error for the next tick. A newer snapshot or
            // newer update wins over this retry; never restore stale progress over it.
            lock (_pendingLock)
                if (!_disposed && _pendingAllTasks == null)
                {
                    if (allTasks != null) _pendingAllTasks = allTasks;
                    else for (var index = sent; index < incremental.Length; index++)
                        _pendingIncrementalTasks.TryAdd(incremental[index].Id, incremental[index]);
                }
            _logger.LogError(ex, "Error flushing pending BTask events");
        }
        finally { Interlocked.Exchange(ref _flushing, 0); }
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        lock (_pendingLock)
        { _disposed = true; _pendingAllTasks = null; _pendingIncrementalTasks.Clear(); }
    }
}
