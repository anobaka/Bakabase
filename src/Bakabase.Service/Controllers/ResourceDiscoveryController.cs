using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

public class DiscoverySubscribeRequest
{
    public int ResourceId { get; set; }
    public ResourceCacheType Types { get; set; }
}

[Route("~/resource/discovery")]
public class ResourceDiscoveryController(ResourceDiscoveryService discoveryService) : Controller
{
    /// <summary>
    /// SSE endpoint for receiving discovery results
    /// </summary>
    [HttpGet("stream")]
    [SwaggerOperation(OperationId = "StreamResourceDiscovery")]
    public async Task Stream()
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no"; // Disable nginx buffering

        // Flush immediately so client receives headers and triggers onopen
        await Response.Body.FlushAsync();

        var connectionId = discoveryService.RegisterConnection(Response);

        try
        {
            // Keep connection alive until client disconnects
            var ct = HttpContext.RequestAborted;
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(30000, ct); // 30 second heartbeat
                await Response.WriteAsync(": heartbeat\n\n", cancellationToken: ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (TaskCanceledException)
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
    public async Task<BaseResponse> Subscribe([FromBody] DiscoverySubscribeRequest request)
    {
        await discoveryService.EnqueueRequest(request.ResourceId, request.Types);
        return new BaseResponse();
    }

    /// <summary>
    /// Subscribe to receive discovery results for multiple resources (batch)
    /// </summary>
    [HttpPost("subscribe/batch")]
    [SwaggerOperation(OperationId = "SubscribeResourceDiscoveryBatch")]
    public async Task<BaseResponse> SubscribeBatch([FromBody] DiscoverySubscribeRequest[] requests)
    {
        foreach (var request in requests)
        {
            await discoveryService.EnqueueRequest(request.ResourceId, request.Types);
        }
        return new BaseResponse();
    }
}
