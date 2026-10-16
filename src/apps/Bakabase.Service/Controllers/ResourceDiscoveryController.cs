using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Bakabase.Service.Components.RemoteAccess;

namespace Bakabase.Service.Controllers;

public class DiscoverySubscribeRequest
{
    public int ResourceId { get; set; }
    public DataOrigin Origin { get; set; }
    public ResourceDataType DataType { get; set; }
}

[Route("~/resource/discovery")]
public class ResourceDiscoveryController(ResourceDiscoveryService discoveryService) : Controller
{
    /// <summary>
    /// SSE endpoint for receiving discovery results
    /// </summary>
    [HttpGet("stream")]
    [SwaggerOperation(OperationId = "StreamResourceDiscovery")]
    [RemoteAccessible]
    public async Task Stream()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering

        var connectionId = discoveryService.RegisterConnection(Response);

        try
        {
            var ct = HttpContext.RequestAborted;
            // An empty flush starts Kestrel's response, but a relay may hold those
            // headers until body data arrives. Send a complete SSE comment now:
            // the browser waits for onopen before submitting its first discovery.
            // Register first so an immediate subscription cannot lose its result.
            await discoveryService.SendHeartbeatAsync(connectionId, ct);

            // Keep connection alive until client disconnects. Use the same writer
            // as discovery results so heartbeats cannot split an event's payload.
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(30000, ct); // 30 second heartbeat
                await discoveryService.SendHeartbeatAsync(connectionId, ct);
            }
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected, this is expected
        }
        finally
        {
            discoveryService.UnregisterConnection(connectionId);
        }
    }

    /// <summary>
    /// Subscribe to receive discovery results for a resource
    /// </summary>
    [HttpPost("subscribe")]
    [SwaggerOperation(OperationId = "SubscribeResourceDiscovery")]
    [RemoteAccessible]
    public async Task<BaseResponse> Subscribe([FromBody] DiscoverySubscribeRequest request)
    {
        await discoveryService.EnqueueRequest(request.ResourceId, request.Origin, request.DataType);
        return new BaseResponse();
    }

    /// <summary>
    /// Subscribe to receive discovery results for multiple resources (batch)
    /// </summary>
    [HttpPost("subscribe/batch")]
    [SwaggerOperation(OperationId = "SubscribeResourceDiscoveryBatch")]
    [RemoteAccessible]
    public async Task<BaseResponse> SubscribeBatch([FromBody] DiscoverySubscribeRequest[] requests)
    {
        foreach (var request in requests)
        {
            await discoveryService.EnqueueRequest(request.ResourceId, request.Origin, request.DataType);
        }
        return new BaseResponse();
    }
}
