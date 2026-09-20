using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Modules.RemoteAccess.Components.Pairing;
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
    /// <see cref="RemoteAccessMode"/>, and — if it offered one — against its device
    /// signature.
    /// </para>
    /// <para>
    /// Signing is optional by default: a LAN caller that presents nothing is anonymous,
    /// which is what every existing phone does. Pairing raises what a caller may reach;
    /// it becomes a requirement only when the operator turns
    /// <c>RequirePairing</c> on.
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

        /// <summary>
        /// Reachable without pairing even when pairing is required, because a device
        /// with no credentials has to be able to get some.
        /// </summary>
        /// <remarks>
        /// The trailing slash on the pairing prefix is load-bearing: without it this
        /// would also match <c>/remote-access/pairing/...</c>, which is the management
        /// side — issuing codes, approving requests — and would hand an unpaired caller
        /// the ability to approve itself. Public so a test can hold this list and the
        /// controller's routes against each other rather than trusting them to agree.
        /// </remarks>
        public static readonly IReadOnlyList<string> AnonymousPathPrefixes =
        [
            "/remote-access/server-info",
            "/remote-access/pair/"
        ];

        public async Task InvokeAsync(HttpContext context, IRemoteAccessService remoteAccessService,
            RemoteDeviceAuthenticator authenticator, IRemoteDeviceService deviceService)
        {
            if (Bakabase.Modules.Federation.Security.FederationHttpContext.IsHandled(context))
            {
                await next(context);
                return;
            }

            var isLoopback = IsLoopback(context);
            var mode = remoteAccessService.GetEffectiveMode();

            // The host itself leaves here, before any of the identity work below. This
            // ordering is what makes the all-in-one provably unaffected.
            if (isLoopback)
            {
                context.SetRemoteAccessContext(new RemoteAccessContext {IsLoopback = true, Mode = mode});
                await next(context);
                return;
            }

            var auth = await AuthenticateAsync(context, authenticator);
            context.SetRemoteAccessContext(new RemoteAccessContext
            {
                IsLoopback = false,
                Mode = mode,
                Device = auth.Outcome == DeviceAuthOutcome.Authenticated ? auth.Device : null
            });

            // Checked before anything about pairing, so a switched-off server does not
            // reveal whether a device is known to it.
            if (mode == RemoteAccessMode.Disabled)
            {
                await WriteDenial(context, HttpStatusCode.Forbidden, RemoteAccessDenialReason.Disabled,
                    "Remote access is turned off. Enable it in Bakabase on the host machine.");
                return;
            }

            if (!auth.MayProceed)
            {
                // A caller that tried to sign and failed is told why. Falling through as
                // anonymous instead would hide a revoked device or a wrong clock behind
                // whatever generic refusal came later.
                var (reason, message) = Describe(auth.Outcome);
                await WriteDenial(context, HttpStatusCode.Unauthorized, reason, message);
                return;
            }

            var path = context.Request.Path.Value ?? string.Empty;

            if (mode != RemoteAccessMode.Unrestricted &&
                remoteAccessService.GetRequirePairing() &&
                auth.Outcome != DeviceAuthOutcome.Authenticated &&
                !AnonymousPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await WriteDenial(context, HttpStatusCode.Unauthorized, RemoteAccessDenialReason.Unauthenticated,
                    "This server only serves paired devices. Pair this one first.");
                return;
            }

            if (auth.Outcome == DeviceAuthOutcome.Authenticated)
            {
                await deviceService.TouchAsync(auth.Device!.Id, context.RequestAborted);
            }

            if (mode == RemoteAccessMode.Unrestricted)
            {
                await next(context);
                return;
            }

            if (HostOnlyPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            {
                await WriteDenial(context, HttpStatusCode.Forbidden, RemoteAccessDenialReason.HostOnly,
                    "This page is only available on the machine running Bakabase.");
                return;
            }

            await next(context);
        }

        /// <summary>
        /// Verifies the signature, hashing the body only when the caller was expected to
        /// hash it too.
        /// </summary>
        /// <remarks>
        /// Both sides decide that from the same observable — a non-GET request whose
        /// declared length fits the threshold — so they cannot disagree about what was
        /// signed. Anything else (a streamed upload, a chunked request with no declared
        /// length) is signed with an empty digest, which leaves its body outside the
        /// signature; the method, path, query, timestamp and nonce still are not.
        /// <para>
        /// Buffering happens only for requests that actually carry our scheme, so an
        /// anonymous caller never pays for it and a large upload is never copied.
        /// </para>
        /// </remarks>
        private static async Task<DeviceAuthResult> AuthenticateAsync(HttpContext context,
            RemoteDeviceAuthenticator authenticator)
        {
            var request = context.Request;
            var rawQuery = request.QueryString.HasValue ? request.QueryString.Value![1..] : string.Empty;

            var header = request.Headers.Authorization.ToString();
            if (RemoteRequestSignature.TryParseHeader(header) == null)
            {
                // No header. A URL token is the other way in, for a caller that cannot
                // set one at all — a native player handed a link.
                var token = request.Query[SignedMediaUrl.QueryKey].ToString();
                return string.IsNullOrEmpty(token)
                    ? DeviceAuthResult.Anonymous
                    : authenticator.AuthenticateSignedUrl(token, request.Method, request.Path.Value ?? string.Empty,
                        rawQuery);
            }

            var bodyDigest = string.Empty;

            var hashable = !HttpMethods.IsGet(request.Method) &&
                           !HttpMethods.IsHead(request.Method) &&
                           request.ContentLength is > 0 and <= RemoteRequestSignature.MaxHashedBodyBytes;

            if (hashable)
            {
                request.EnableBuffering();
                using var buffer = new MemoryStream((int) request.ContentLength!.Value);
                await request.Body.CopyToAsync(buffer, context.RequestAborted);
                request.Body.Position = 0;
                bodyDigest = RemoteRequestSignature.HashBody(buffer.GetBuffer().AsSpan(0, (int) buffer.Length));
            }

            return authenticator.Authenticate(header, request.Method, request.Path.Value ?? string.Empty,
                rawQuery, bodyDigest);
        }

        private static (RemoteAccessDenialReason Reason, string Message) Describe(DeviceAuthOutcome outcome) =>
            outcome switch
            {
                DeviceAuthOutcome.UnknownDevice => (RemoteAccessDenialReason.DeviceRevoked,
                    "This device is no longer paired with this server. Pair it again."),
                DeviceAuthOutcome.Expired => (RemoteAccessDenialReason.SignatureExpired,
                    "This device's clock is too far from the server's. Check the time on both."),
                _ => (RemoteAccessDenialReason.Unauthenticated,
                    "This request could not be verified. Pair this device again.")
            };

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
