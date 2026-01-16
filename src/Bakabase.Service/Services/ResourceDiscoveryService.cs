using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

public record DiscoveryRequest(int ResourceId, ResourceCacheType Types);

public record DiscoveryResult
{
    public int ResourceId { get; init; }
    public bool Success { get; init; }
    public string[]? CoverPaths { get; init; }
    public string[]? PlayableFilePaths { get; init; }
    public bool HasMorePlayableFiles { get; init; }
    public string? Error { get; init; }
}

public class ResourceDiscoveryService : BackgroundService
{
    private readonly ILogger<ResourceDiscoveryService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // SSE connections
    private readonly ConcurrentDictionary<string, SseConnection> _connections = new();

    // Processing queue
    private readonly Channel<DiscoveryRequest> _requestChannel = Channel.CreateUnbounded<DiscoveryRequest>();

    // Track pending requests to avoid duplicates
    private readonly ConcurrentDictionary<string, byte> _pendingRequests = new();

    public ResourceDiscoveryService(
        ILogger<ResourceDiscoveryService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public string RegisterConnection(HttpResponse response)
    {
        var connectionId = Guid.NewGuid().ToString();
        var connection = new SseConnection(connectionId, response);
        _connections[connectionId] = connection;
        _logger.LogInformation("SSE connection registered: {ConnectionId}", connectionId);
        return connectionId;
    }

    public void UnregisterConnection(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out _))
        {
            _logger.LogInformation("SSE connection unregistered: {ConnectionId}", connectionId);
        }
    }

    public async Task EnqueueRequest(int resourceId, ResourceCacheType types)
    {
        var requestKey = $"{resourceId}-{(int)types}";

        // Avoid duplicate requests
        if (!_pendingRequests.TryAdd(requestKey, 0))
        {
            _logger.LogDebug("Request already pending: {RequestKey}", requestKey);
            return;
        }

        // Always enqueue for real-time discovery.
        // Frontend already checks cache before calling subscribe, so if we receive a request,
        // it means either cache is disabled or cache doesn't have the required data.
        // The discovery process will write results to cache after completion.
        await _requestChannel.Writer.WriteAsync(new DiscoveryRequest(resourceId, types));
        _logger.LogDebug("Request enqueued: {RequestKey}", requestKey);
    }

    private async Task BroadcastResult(DiscoveryResult result)
    {
        var deadConnections = new List<string>();

        foreach (var (connectionId, connection) in _connections)
        {
            try
            {
                await connection.SendResultAsync(result);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send result to connection {ConnectionId}", connectionId);
                deadConnections.Add(connectionId);
            }
        }

        // Clean up dead connections
        foreach (var connectionId in deadConnections)
        {
            _connections.TryRemove(connectionId, out _);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ResourceDiscoveryService started");

        await foreach (var request in _requestChannel.Reader.ReadAllAsync(stoppingToken))
        {
            var requestKey = $"{request.ResourceId}-{(int)request.Types}";

            try
            {
                await ProcessRequest(request, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing discovery request for resource {ResourceId}", request.ResourceId);

                await BroadcastResult(new DiscoveryResult
                {
                    ResourceId = request.ResourceId,
                    Success = false,
                    Error = ex.Message
                });
            }
            finally
            {
                _pendingRequests.TryRemove(requestKey, out _);
            }
        }

        _logger.LogInformation("ResourceDiscoveryService stopped");
    }

    private async Task ProcessRequest(DiscoveryRequest request, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var resourceService = scope.ServiceProvider.GetRequiredService<IResourceService>();

        string[]? coverPaths = null;
        string[]? playableFilePaths = null;
        var hasMorePlayableFiles = false;

        // Discover covers if requested
        if (request.Types.HasFlag(ResourceCacheType.Covers))
        {
            _logger.LogDebug("Discovering cover for resource {ResourceId}", request.ResourceId);
            var coverPath = await resourceService.DiscoverAndCacheCover(request.ResourceId, ct);
            if (!string.IsNullOrEmpty(coverPath))
            {
                coverPaths = [coverPath];
            }
        }

        // Discover playable files if requested
        if (request.Types.HasFlag(ResourceCacheType.PlayableFiles))
        {
            _logger.LogDebug("Discovering playable files for resource {ResourceId}", request.ResourceId);
            playableFilePaths = await resourceService.DiscoverAndCachePlayableFiles(request.ResourceId, ct);

            // Check cache for hasMorePlayableFiles flag
            var cache = await resourceService.GetResourceCache(request.ResourceId);
            hasMorePlayableFiles = cache?.HasMorePlayableFiles ?? false;
        }

        var result = new DiscoveryResult
        {
            ResourceId = request.ResourceId,
            Success = true,
            CoverPaths = coverPaths,
            PlayableFilePaths = playableFilePaths,
            HasMorePlayableFiles = hasMorePlayableFiles
        };

        await BroadcastResult(result);
        _logger.LogDebug("Discovery completed for resource {ResourceId}", request.ResourceId);
    }

    private class SseConnection
    {
        private readonly string _connectionId;
        private readonly HttpResponse _response;
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public SseConnection(string connectionId, HttpResponse response)
        {
            _connectionId = connectionId;
            _response = response;
        }

        public async Task SendResultAsync(DiscoveryResult result)
        {
            await _writeLock.WaitAsync();
            try
            {
                var eventType = result.Success ? "result" : "error";
                var json = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                });

                await _response.WriteAsync($"event: {eventType}\n");
                await _response.WriteAsync($"data: {json}\n\n");
                await _response.Body.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public async Task SendHeartbeatAsync()
        {
            await _writeLock.WaitAsync();
            try
            {
                await _response.WriteAsync(": heartbeat\n\n");
                await _response.Body.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }
}
