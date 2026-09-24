using System.Net;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// The after-routing half of <see cref="LoopbackCrossSiteGuard"/>: refuses a request from
/// an untrusted page on another site when the action it reaches runs on this machine — a
/// page the browser labels with fetch metadata, or, from an engine that sends none, one
/// whose <c>Origin</c> is neither this Service's own nor trusted.
/// </summary>
/// <remarks>
/// <para>
/// The middleware lets every GET that is not a load into a frame or a WebSocket handshake
/// through, because a GET
/// changes nothing on its own — the exceptions are the actions marked <see cref="RunsOnUserMachineAttribute"/>. Several of
/// them are GETs (open a folder, open a link, play a file) and <c>/gui/url</c> hands its
/// argument straight to the shell, so an image tag on any website could otherwise start a
/// program here. Which action a request reaches is only known after routing, and
/// <c>AppStartup</c> owns the routing pipeline, so the check lives here, next to
/// <see cref="RemoteAccessAuthorizationFilter"/>, which reads the same marker for callers
/// on other machines.
/// </para>
/// <para>
/// A request the middleware already refused never gets here; this one only adds the
/// actions, whatever their method.
/// </para>
/// </remarks>
public sealed class LoopbackCrossSiteUserMachineFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (RemoteAccessAuthorizationFilter.FindUserMachineAttribute(context) == null ||
            !LoopbackCrossSiteGuard.IsFromUntrustedPage(context.HttpContext))
        {
            return;
        }

        context.HttpContext.Response.Headers["X-Bakabase-Remote-Access"] =
            RemoteAccessDenialReason.HostOnly.ToString();
        context.Result = new ObjectResult(BaseResponseBuilder.Build(ResponseCode.Unauthorized,
            LoopbackCrossSiteGuard.RefusalMessage))
        {
            StatusCode = (int) HttpStatusCode.Forbidden
        };
    }
}
