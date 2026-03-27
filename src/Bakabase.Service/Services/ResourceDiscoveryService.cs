using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Services;

public record DiscoveryRequest(int ResourceId, DataOrigin Origin, ResourceDataType DataType);

public record DiscoveryResult
{
    public int ResourceId { get; init; }
    public DataOrigin Origin { get; init; }
    public ResourceDataType DataType { get; init; }
    public bool Success { get; init; }
    public string[]? CoverPaths { get; init; }         // when DataType=Cover
    public PlayableItem[]? PlayableItems { get; init; } // when DataType=PlayableItem
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

    public async Task EnqueueRequest(int resourceId, DataOrigin origin, ResourceDataType dataType)
    {
        var requestKey = $"{resourceId}-{origin}-{dataType}";

        // Avoid duplicate requests
        if (!_pendingRequests.TryAdd(requestKey, 0))
        {
            _logger.LogDebug("Request already pending: {RequestKey}", requestKey);
            return;
        }

        await _requestChannel.Writer.WriteAsync(new DiscoveryRequest(resourceId, origin, dataType));
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
            var requestKey = $"{request.ResourceId}-{request.Origin}-{request.DataType}";

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
                    Origin = request.Origin,
                    DataType = request.DataType,
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

        var resource = await resourceService.Get(request.ResourceId,
            ResourceAdditionalItem.Cover | ResourceAdditionalItem.PlayableItem);
        if (resource == null) return;

        DiscoveryResult result;

        if (request.DataType == ResourceDataType.Cover)
        {
            var coverProviders = scope.ServiceProvider.GetRequiredService<IEnumerable<ICoverProvider>>();
            var provider = coverProviders.FirstOrDefault(p => p.Origin == request.Origin);

            string[]? coverPaths = null;
            if (provider != null && provider.AppliesTo(resource))
            {
                var covers = await provider.GetCoversAsync(resource, ct);
                coverPaths = covers?.ToArray();
            }

            result = new DiscoveryResult
            {
                ResourceId = request.ResourceId,
                Origin = request.Origin,
                DataType = request.DataType,
                Success = true,
                CoverPaths = coverPaths,
            };
        }
        else if (request.DataType == ResourceDataType.PlayableItem)
        {
            var playableItemProviders = scope.ServiceProvider.GetRequiredService<IEnumerable<IPlayableItemProvider>>();
            var provider = playableItemProviders.FirstOrDefault(p => p.Origin == request.Origin);

            PlayableItem[]? items = null;
            if (provider != null && provider.AppliesTo(resource))
            {
                var providerResult = await provider.GetPlayableItemsAsync(resource, ct);
                items = providerResult.Items.Count > 0 ? providerResult.Items.ToArray() : null;
            }

            result = new DiscoveryResult
            {
                ResourceId = request.ResourceId,
                Origin = request.Origin,
                DataType = request.DataType,
                Success = true,
                PlayableItems = items,
            };
        }
        else
        {
            result = new DiscoveryResult
            {
                ResourceId = request.ResourceId,
                Origin = request.Origin,
                DataType = request.DataType,
                Success = false,
                Error = $"Unsupported data type: {request.DataType}"
            };
        }

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
                var json = System.Text.Json.JsonSerializer.Serialize(result, System.Text.Json.JsonSerializerOptions.Web);

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
