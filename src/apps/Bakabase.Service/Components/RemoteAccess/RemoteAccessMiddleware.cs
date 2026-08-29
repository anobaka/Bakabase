using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.Service.Components.RemoteAccess
{
    /// <summary>
    /// The outer half of the remote-access gate: decides whether a request from
    /// outside this machine is served at all.
    /// <para>
    /// Loopback requests pass straight through, so the desktop app behaves exactly
    /// as it always has. Everything else is judged against
    /// <see cref="RemoteAccessMode"/>. There is no identity involved — anything that
    /// can reach the port is trusted — so this is a switch, not an access check.
    /// </para>
    /// <para>
    /// Per-endpoint decisions are left to <see cref="RemoteAccessAuthorizationFilter"/>,
    /// which runs later and can see the action's attributes; this middleware handles
    /// what MVC never sees — the SPA shell, the SignalR hub, MiniProfiler and Swagger.
    /// </para>
    /// </summary>
    public class RemoteAccessMiddleware(RequestDelegate next, ILogger<RemoteAccessMiddleware> logger)
    {
        /// <summary>
        /// Surfaces that exist for whoever is sitting at the host and have no remote
        /// story. Blocked in <see cref="RemoteAccessMode.Enabled"/>; still reachable
        /// in <see cref="RemoteAccessMode.Unrestricted"/>, where the remote browser
        /// belongs to the operator.
        /// </summary>
        private static readonly string[] HostOnlyPathPrefixes =
        [
            "/profiler",
            "/internal-doc"
        ];

        public async Task InvokeAsync(HttpContext context, IRemoteAccessService remoteAccessService)
        {
            var isLoopback = IsLoopback(context);
            var mode = remoteAccessService.GetEffectiveMode();

            context.SetRemoteAccessContext(new RemoteAccessContext {IsLoopback = isLoopback, Mode = mode});

            if (isLoopback || mode == RemoteAccessMode.Unrestricted)
            {
                await next(context);
                return;
            }

            if (mode == RemoteAccessMode.Disabled)
            {
                await WriteDenial(context, HttpStatusCode.Forbidden, RemoteAccessDenialReason.Disabled,
                    "Remote access is turned off. Enable it in Bakabase on the host machine.");
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            if (HostOnlyPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await WriteDenial(context, HttpStatusCode.Forbidden, RemoteAccessDenialReason.HostOnly,
                    "This page is only available on the machine running Bakabase.");
                return;
            }

            await next(context);
        }

        private static bool IsLoopback(HttpContext context)
        {
            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null)
            {
                // No peer address means an in-process call (the test host, or a
                // framework-internal request). Treat it as local.
                return true;
            }

            if (remoteIp.IsIPv4MappedToIPv6)
            {
                remoteIp = remoteIp.MapToIPv4();
            }

            return IPAddress.IsLoopback(remoteIp);
        }

        private async Task WriteDenial(HttpContext context, HttpStatusCode statusCode,
            RemoteAccessDenialReason reason, string message)
        {
            logger.LogDebug("Remote access denied for {Method} {Path} from {Ip}: {Reason}",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress, reason);

            context.Response.StatusCode = (int) statusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Bakabase-Remote-Access"] = reason.ToString();

            var payload = BaseResponseBuilder.Build(ResponseCode.Unauthorized, message);
            await context.Response.WriteAsync(JsonConvert.SerializeObject(payload));
        }
    }
}
