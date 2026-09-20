using System;
using System.Net.Http;
using System.Text.Json;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Federation;

/// <summary>Handles protocol errors inside MVC, before the legacy middleware converts exceptions to HTTP 200.</summary>
public sealed class FederationExceptionFilter(ILogger<FederationExceptionFilter> logger) : IExceptionFilter, IOrderedFilter
{
    public int Order => int.MaxValue;

    public void OnException(ExceptionContext context)
    {
        if (FederationLocalAccessFilter.GetEndpointAttribute(context) == null) return;
        object payload;
        int status;
        switch (context.Exception)
        {
            case FederationQueryException query:
                status = query.StatusCode;
                payload = new { code = query.Code, message = query.Message, retryable = query.Retryable,
                    field = query.Field, nodeId = query.NodeId, omittedNodes = query.OmittedNodes };
                break;
            case FederationAccessException access:
                status = access.StatusCode;
                payload = new { code = access.ErrorCode, message = access.Message,
                    retryable = status is 429 or 502 or 503 };
                break;
            case OperationCanceledException:
                if (context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    context.HttpContext.Abort();
                    context.ExceptionHandled = true;
                    return;
                }
                status = 503;
                payload = new { code = "QueryDeadlineExceeded", message = "The operation did not finish before its deadline.", retryable = true };
                break;
            case HttpRequestException:
                status = 502;
                payload = new { code = "PeerUnavailable", message = "The source node could not be reached.", retryable = true };
                break;
            default:
                logger.LogError(context.Exception, "Federation action {Action} failed", context.ActionDescriptor.DisplayName);
                status = 500;
                payload = new { code = "InternalError", message = "The library operation failed.", retryable = false };
                break;
        }
        if (context.HttpContext.Response.HasStarted) context.HttpContext.Abort();
        else context.Result = new ContentResult
        {
            StatusCode = status, ContentType = "application/json; charset=utf-8",
            Content = JsonSerializer.Serialize(payload, FederationJson.Options)
        };
        context.ExceptionHandled = true;
    }
}
