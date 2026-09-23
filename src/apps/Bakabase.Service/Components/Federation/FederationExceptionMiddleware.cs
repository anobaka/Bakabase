using System;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.Federation;

public sealed class FederationExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/federation"))
        {
            await next(context);
            return;
        }
        context.Response.Headers.CacheControl = "no-store";
        try { await next(context); }
        catch (FederationQueryException e)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.StatusCode = e.StatusCode;
            await context.Response.WriteAsJsonAsync(new
            {
                code = e.Code, message = e.Message, retryable = e.Retryable,
                field = e.Field, nodeId = e.NodeId, omittedNodes = e.OmittedNodes
            }, FederationJson.Options, context.RequestAborted);
        }
        catch (FederationAccessException e)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.StatusCode = e.StatusCode;
            await context.Response.WriteAsJsonAsync(new { code = e.ErrorCode, message = e.Message },
                FederationJson.Options, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
        }
    }
}
