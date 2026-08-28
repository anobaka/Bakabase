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
    /// The outer half of the remote-access gate. It decides, for every request,
    /// whether the caller is the host machine, an authenticated device, or a
    /// stranger — and turns strangers away before they reach anything.
    /// <para>
    /// Loopback requests are passed straight through, so the desktop app behaves
    /// exactly as it always has. Everything else is judged against
    /// <see cref="RemoteAccessMode"/>. Fine-grained per-endpoint permission is left
    /// to <see cref="RemoteAccessAuthorizationFilter"/>, which runs later and can see
    /// the action's attributes; this middleware handles what MVC never sees — the SPA
    /// shell, the SignalR hub, MiniProfiler and Swagger.
    /// </para>
    /// </summary>
    public class RemoteAccessMiddleware(RequestDelegate next, ILogger<RemoteAccessMiddleware> logger)
    {
        /// <summary>
        /// Surfaces that exist for the host operator and have no remote story. Blocked
        /// outright rather than merely authenticated: a paired phone has no business
        /// reading profiler results or the Swagger document.
        /// </summary>
        private static readonly string[] HostOnlyPathPrefixes =
        [
            "/profiler",
            "/internal-doc"
        ];

        /// <summary>
        /// The SPA shell and its assets. A device that has not paired yet still has to
        /// be able to load the page that asks it to pair.
        /// </summary>
        private static readonly string[] AnonymousAssetExtensions =
        [
            ".js", ".mjs", ".css", ".map", ".html", ".ico", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp",
            ".woff", ".woff2", ".ttf", ".eot", ".json", ".txt", ".wasm"
        ];

        /// <summary>
        /// Endpoints a device must reach before it has a token. Kept deliberately tiny.
        /// </summary>
        private static readonly string[] AnonymousApiPaths =
        [
            "/remote-access/context",
            "/remote-access/pair"
        ];

        public async Task InvokeAsync(HttpContext context, IRemoteAccessService remoteAccessService)
        {
            var isLoopback = IsLoopback(context);
            var mode = remoteAccessService.GetEffectiveMode();

            if (isLoopback || mode == RemoteAccessMode.Open)
            {
                context.SetRemoteAccessContext(new RemoteAccessContext {IsLoopback = isLoopback, Mode = mode});
                await next(context);
                return;
            }

            if (mode == RemoteAccessMode.Disabled)
            {
                await WriteDenial(context, HttpStatusCode.Forbidden, ResponseCode.Unauthorized,
                    RemoteAccessDenialReason.Disabled,
                    "Remote access is turned off. Enable it in Bakabase on the host machine.");
                return;
            }

            var device = Authenticate(context, remoteAccessService);
            context.SetRemoteAccessContext(new RemoteAccessContext
            {
                IsLoopback = false,
                Mode = mode,
                Device = device
            });

            var path = context.Request.Path.Value ?? string.Empty;

            if (HostOnlyPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await WriteDenial(context, HttpStatusCode.Forbidden, ResponseCode.Unauthorized,
                    RemoteAccessDenialReason.HostOnly,
                    "This page is only available on the machine running Bakabase.");
                return;
            }

            if (device == null && !IsAnonymouslyReachable(context, path))
            {
                await WriteDenial(context, HttpStatusCode.Unauthorized, ResponseCode.Unauthenticated,
                    RemoteAccessDenialReason.Unauthenticated,
                    "This device is not paired with Bakabase.");
                return;
            }

            if (device != null)
            {
                // Fire-and-forget would race with the options writer; awaiting is cheap
                // because the service only persists once every few minutes per device.
                await remoteAccessService.TouchDeviceAsync(device.Id);
            }

            await next(context);
        }

        /// <summary>
        /// Reads the device token from the cookie the SPA carries, the Authorization
        /// header an explicit API client would use, or a signed single-path token in
        /// the query string.
        /// </summary>
        private static Bakabase.Abstractions.Models.Domain.RemoteDevice? Authenticate(HttpContext context,
            IRemoteAccessService service)
        {
            if (context.Request.Cookies.TryGetValue(RemoteAccessHttpContextExtensions.DeviceTokenCookieName,
                    out var cookieToken))
            {
                var device = service.Authenticate(cookieToken);
                if (device != null)
                {
                    return device;
                }
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            if (!string.IsNullOrEmpty(authorization) &&
                authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var device = service.Authenticate(authorization["Bearer ".Length..].Trim());
                if (device != null)
                {
                    return device;
                }
            }

            var pathToken = context.Request.Query[RemoteAccessHttpContextExtensions.PathTokenQueryKey].ToString();
            if (!string.IsNullOrEmpty(pathToken))
            {
                // A signed token authorizes one path, so it is validated against the
                // path this request actually asks for.
                var requestedPath = context.Request.Query["fullname"].ToString();
                if (string.IsNullOrEmpty(requestedPath))
                {
                    requestedPath = context.Request.Query["path"].ToString();
                }

                if (service.TryValidatePathToken(pathToken, requestedPath, out var device))
                {
                    return device;
                }
            }

            return null;
        }

        private static bool IsAnonymouslyReachable(HttpContext context, string path)
        {
            if (AnonymousApiPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Beyond the pairing handshake, an unpaired device may only read the shell.
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            {
                return false;
            }

            // The SPA shell itself.
            if (path is "" or "/")
            {
                return true;
            }

            var extension = System.IO.Path.GetExtension(path);
            return !string.IsNullOrEmpty(extension) &&
                   AnonymousAssetExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
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

        private async Task WriteDenial(HttpContext context, HttpStatusCode statusCode, ResponseCode responseCode,
            RemoteAccessDenialReason reason, string message)
        {
            logger.LogDebug("Remote access denied for {Method} {Path} from {Ip}: {Reason}",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress, reason);

            context.Response.StatusCode = (int) statusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers["X-Bakabase-Remote-Access"] = reason.ToString();

            var payload = BaseResponseBuilder.Build(responseCode, message);
            await context.Response.WriteAsync(JsonConvert.SerializeObject(payload));
        }
    }
}
