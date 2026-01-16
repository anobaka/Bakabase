using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Services;

/// <summary>
/// Hosted service that subscribes to resource cover change events and invalidates the cover cache.
/// Uses a 5-second aggregation window to batch multiple events together.
/// </summary>
public class ResourceCoverCacheInvalidationService : IHostedService, IDisposable
{
    private static readonly TimeSpan AggregationWindow = TimeSpan.FromSeconds(5);

    private readonly IResourceDataChangeEvent _resourceDataChangeEvent;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ResourceCoverCacheInvalidationService> _logger;

    private readonly HashSet<int> _pendingResourceIds = new();
    private readonly object _lock = new();
    private Timer? _timer;

    public ResourceCoverCacheInvalidationService(
        IResourceDataChangeEvent resourceDataChangeEvent,
        IServiceScopeFactory scopeFactory,
        ILogger<ResourceCoverCacheInvalidationService> logger)
    {
        _resourceDataChangeEvent = resourceDataChangeEvent;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _resourceDataChangeEvent.OnResourceCoverChanged += OnResourceCoverChanged;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _resourceDataChangeEvent.OnResourceCoverChanged -= OnResourceCoverChanged;

        // Process any remaining pending invalidations
        FlushPendingInvalidations();

        return Task.CompletedTask;
    }

    private void OnResourceCoverChanged(ResourceCoverChangedEventArgs args)
    {
        lock (_lock)
        {
            foreach (var id in args.ResourceIds)
            {
                _pendingResourceIds.Add(id);
            }

            // Start timer if not already running
            _timer ??= new Timer(OnTimerElapsed, null, AggregationWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnTimerElapsed(object? state)
    {
        FlushPendingInvalidations();
    }

    private void FlushPendingInvalidations()
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

        _ = InvalidateCacheAsync(resourceIds);
    }

    private async Task InvalidateCacheAsync(int[] resourceIds)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var resourceService = scope.ServiceProvider.GetRequiredService<IResourceService>();
            await resourceService.DeleteResourceCacheByResourceIdsAndCacheType(resourceIds, ResourceCacheType.Covers);
            _logger.LogDebug("Invalidated cover cache for {Count} resources (aggregated)", resourceIds.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate cover cache for {Count} resources", resourceIds.Length);
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
