using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Bridges backend resource-data-change events to the web UI over SignalR so the
/// resource list can reload affected resources without a full re-search.
///
/// Only the changed resource ids are pushed (key "Resource"); the frontend reloads
/// just those that are currently displayed. A short aggregation window coalesces
/// bursts — e.g. a batch cache refresh fires one event per resource — into periodic
/// pushes, while staying short enough that the grid still updates incrementally as a
/// long batch progresses.
/// </summary>
public class ResourceChangePushService : IHostedService, IDisposable
{
    private static readonly TimeSpan AggregationWindow = TimeSpan.FromMilliseconds(400);

    private readonly IResourceDataChangeEvent _resourceDataChangeEvent;
    private readonly IHubContext<WebGuiHub, IWebGuiClient> _uiHub;
    private readonly ILogger<ResourceChangePushService> _logger;

    private readonly HashSet<int> _pendingResourceIds = new();
    private readonly object _lock = new();
    private Timer? _timer;

    public ResourceChangePushService(
        IResourceDataChangeEvent resourceDataChangeEvent,
        IHubContext<WebGuiHub, IWebGuiClient> uiHub,
        ILogger<ResourceChangePushService> logger)
    {
        _resourceDataChangeEvent = resourceDataChangeEvent;
        _uiHub = uiHub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _resourceDataChangeEvent.OnResourceDataChanged += OnResourceDataChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _resourceDataChangeEvent.OnResourceDataChanged -= OnResourceDataChanged;
        FlushPending();
        return Task.CompletedTask;
    }

    private void OnResourceDataChanged(ResourceDataChangedEventArgs args)
    {
        lock (_lock)
        {
            foreach (var id in args.ResourceIds)
            {
                _pendingResourceIds.Add(id);
            }

            _timer ??= new Timer(_ => FlushPending(), null, AggregationWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushPending()
    {
        int[] resourceIds;

        lock (_lock)
        {
            if (_pendingResourceIds.Count == 0)
            {
                return;
            }

            resourceIds = [.. _pendingResourceIds];
            _pendingResourceIds.Clear();

            _timer?.Dispose();
            _timer = null;
        }

        _ = PushAsync(resourceIds);
    }

    private async Task PushAsync(int[] resourceIds)
    {
        try
        {
            await _uiHub.Clients.All.GetIncrementalData("Resource", resourceIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push resource change for {Count} resources", resourceIds.Length);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
