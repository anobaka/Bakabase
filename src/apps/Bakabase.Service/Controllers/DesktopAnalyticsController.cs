using System.Net;
using Bakabase.Abstractions.Components.Telemetry;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers;

/// <summary>Local native-window context, never a server identity offered to ordinary browsers.</summary>
[ApiController]
[Route("~/app/analytics-client-context")]
public sealed class DesktopAnalyticsController : ControllerBase
{
    [HttpGet]
    [SwaggerOperation(OperationId = "GetDesktopAnalyticsClientContext")]
    public ActionResult<SingletonResponse<DesktopAnalyticsContext>> Get()
    {
        Response.Headers.CacheControl = "no-store";
        var request = Request;
        var address = HttpContext.Connection.RemoteIpAddress;
        if (!DesktopAnalyticsRequest.IsAllowed(address != null && IPAddress.IsLoopback(address),
                request.Headers.UserAgent.ToString(), request.Scheme, request.Host.Value,
                request.Headers.Origin.ToString(), request.Headers["Sec-Fetch-Site"].ToString()) ||
            HttpContext.RequestServices.GetService<IDesktopAnalyticsContextProvider>() is not { } provider)
            return NotFound();
        return new SingletonResponse<DesktopAnalyticsContext>(provider.Read());
    }
}
