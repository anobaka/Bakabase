using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Components.Gui;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Search.Index;

/// <summary>
/// Temporary service to notify frontend about resource index updates.
/// Uses a 2-second window to batch notifications.
/// TODO: Remove this service when no longer needed.
/// </summary>
public class ResourceIndexNotificationService : IHostedService, IDisposable
{
    private readonly IResourceDataChangeEvent _resourceDataChangeEvent;
    private readonly IHubContext<WebGuiHub, IWebGuiClient> _hubContext;
    private readonly ILogger<ResourceIndexNotificationService> _logger;

    private readonly HashSet<int> _pendingResourceIds = new();
    private readonly object _lock = new();
    private Timer? _notificationTimer;
    private const int WindowMs = 2000;

    // Use a fixed notification ID so frontend can update the same toast
    private const string NotificationId = "resource-index-update";

    public ResourceIndexNotificationService(
        IResourceDataChangeEvent resourceDataChangeEvent,
        IHubContext<WebGuiHub, IWebGuiClient> hubContext,
        ILogger<ResourceIndexNotificationService> logger)
    {
        _resourceDataChangeEvent = resourceDataChangeEvent;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Subscribe to resource data change events
        _resourceDataChangeEvent.OnResourceDataChanged += OnResourceDataChanged;
        _resourceDataChangeEvent.OnResourceRemoved += OnResourceRemoved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // Unsubscribe from events
        _resourceDataChangeEvent.OnResourceDataChanged -= OnResourceDataChanged;
        _resourceDataChangeEvent.OnResourceRemoved -= OnResourceRemoved;
        return Task.CompletedTask;
    }

    private void OnResourceDataChanged(ResourceDataChangedEventArgs args)
    {
        AddPendingResources(args.ResourceIds);
    }

    private void OnResourceRemoved(ResourceRemovedEventArgs args)
    {
        AddPendingResources(args.ResourceIds);
    }

    private void AddPendingResources(IEnumerable<int> resourceIds)
    {
        lock (_lock)
        {
            foreach (var id in resourceIds)
            {
                _pendingResourceIds.Add(id);
            }

            // Reset or start the timer
            _notificationTimer?.Dispose();
            _notificationTimer = new Timer(SendNotification, null, WindowMs, Timeout.Infinite);
        }
    }

    private void SendNotification(object? state)
    {
        int count;
        lock (_lock)
        {
            count = _pendingResourceIds.Count;
            _pendingResourceIds.Clear();
            _notificationTimer?.Dispose();
            _notificationTimer = null;
        }

        if (count > 0)
        {
            try
            {
                var notification = new AppNotificationMessageViewModel
                {
                    Id = NotificationId,
                    Title = "索引更新",
                    Message = $"正在为 {count} 个资源建立索引",
                    Severity = AppNotificationSeverity.Info
                };

                _hubContext.Clients.All.OnNotification(notification);
                _logger.LogDebug("Sent index update notification for {Count} resources", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send index update notification");
            }
        }
    }

    public void Dispose()
    {
        _notificationTimer?.Dispose();
    }
}
