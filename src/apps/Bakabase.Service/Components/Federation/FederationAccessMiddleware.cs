using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Infrastructures.Components.App;
using Bootstrap.Models.Constants;
using Bakabase.Modules.Federation;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.Federation;

/// <summary>Runs before the legacy remote-access middleware, including its loopback bypass.</summary>
public sealed class FederationAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, FederationStateStore store,
        NodeGrantAuthenticator authenticator, IRemoteAccessService remoteAccess, GrantLeaseRegistry leases)
    {
        var path = context.Request.Path.Value ?? "";
        var authorization = context.Request.Headers.Authorization.ToString();
        var isNodeSignature = NodeRequestSignature.HasScheme(authorization);
        var kind = FederationRoutePolicy.Classify(path);
        if (!isNodeSignature && kind == null && !FederationRoutePolicy.IsFederationPath(path))
        {
            await next(context);
            return;
        }
        if (kind == FederationEndpointKind.Local && !isNodeSignature && IsDevPreflight(context))
        {
            // Answered by the CORS middleware further down; a preflight carries no request of its own.
            await next(context);
            return;
        }
        try
        {
            if (kind == null || isNodeSignature && kind != FederationEndpointKind.Export ||
                !FederationRoutePolicy.Allows(kind.Value, context.Request.Method, path))
                throw new FederationAccessException("NodeRouteForbidden", 403, "This route is outside the node sharing protocol.");

            NodePrincipal? principal = null;
            if (kind == FederationEndpointKind.Local)
            {
                if (!IsLocalCaller(context))
                    throw new FederationAccessException("LocalInterfaceOnly", 403, "This interface is only available on the computer you are using.");
                await BoundBodyAsync(context, hash: false);
            }
            else
            {
                // This is deliberately evaluated here: accepted federation requests
                // bypass the legacy identity gate, never its Disabled setting.
                if (remoteAccess.GetEffectiveMode() == RemoteAccessMode.Disabled)
                    throw new FederationAccessException("RemoteAccessDisabled", 403, "Remote access is disabled on this node.");
                // Each route needs its own sharing switch, read once and checked before any signature work, so a
                // switched-off scope costs nothing and one switch never opens the other's routes (§7.3).
                var required = FederationRoutePolicy.RequiredSharing(kind.Value, context.Request.Method, path) ??
                               throw new FederationAccessException("NodeRouteForbidden", 403, "This route is outside the node sharing protocol.");
                RequireSharing(required, await store.GetSharingSwitchesAsync(context.RequestAborted));
                if (kind == FederationEndpointKind.Export)
                {
                    if (!isNodeSignature)
                        throw new FederationAccessException("NodeAuthenticationRequired", 401, "A direct node read authorization is required.");
                    var digest = await BoundBodyAsync(context, hash: true);
                    principal = await authenticator.AuthenticateAsync(authorization, context.Request.Method, path,
                        context.Request.QueryString.HasValue ? context.Request.QueryString.Value![1..] : "", digest, context.RequestAborted);
                    // A grant reaches only the routes of its own scope; the handshake takes either.
                    if (!FederationRoutePolicy.ScopeMatches(required, principal.Scope))
                        throw NodeGrantService.ScopeNotGranted();
                }
                else await BoundBodyAsync(context, hash: false);
            }

            FederationHttpContext.MarkHandled(context, kind.Value, principal);
            context.Response.Headers.CacheControl = "no-store";
            var originalAborted = context.RequestAborted;
            using var grantLifetime = principal == null ? null : CancellationTokenSource.CreateLinkedTokenSource(
                originalAborted, leases.GetCancellationToken(principal.GrantId));
            try
            {
                // Covers every export operation, including detail/location/root
                // reads which do not create a query snapshot or an asset stream.
                // Keep the link through MVC result execution and streaming writes.
                if (grantLifetime != null) context.RequestAborted = grantLifetime.Token;
                await next(context);
            }
            finally { context.RequestAborted = originalAborted; }
        }
        catch (FederationAccessException e)
        {
            if (context.Response.HasStarted) { context.Abort(); return; }
            context.Response.Clear();
            context.Response.StatusCode = e.StatusCode;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
                { code = e.ErrorCode, message = e.Message, retryable = e.StatusCode is 429 or 503 }, FederationJson.Options),
                context.RequestAborted);
        }
    }

    /// <summary>
    /// The switch check of §7.3. <c>info</c> and the handshake answer while either switch is on: <c>info</c> so a
    /// device sharing only its definitions can be found, the handshake because the grant's own scope decides (its
    /// switch is checked while authenticating).
    /// </summary>
    private static void RequireSharing(FederationSharingRequirement required, FederationSharingSwitches switches)
    {
        switch (required)
        {
            case FederationSharingRequirement.Library when !switches.Library:
            case FederationSharingRequirement.Either or FederationSharingRequirement.GrantScope
                when !switches.Library && !switches.DataSync:
                throw new FederationAccessException("SharingDisabled", 403, "Resource sharing is disabled on this node.");
            case FederationSharingRequirement.DataSync when !switches.DataSync:
                throw NodeGrantService.DataSyncSharingDisabled();
        }
    }

    public static bool IsLocalCaller(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address == null || !IPAddress.IsLoopback(address)) return false;
        var host = context.Request.Host;
        if (!IsLoopbackHost(host.Host)) return false;
        var port = host.Port ?? (context.Request.IsHttps ? 443 : 80);
        if (context.Connection.LocalPort > 0 && port != context.Connection.LocalPort) return false;
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true; // Native players and local scripts have no Origin.
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsLoopbackHost(uri.Host) &&
               (uri.Port == port && uri.Scheme == context.Request.Scheme || IsDevOrigin(uri));
    }

    /// <summary>
    /// <c>yarn dev</c> serves the UI from the Vite server's own loopback origin (the dev origin CORS
    /// already allows). Only an unpublished development build trusts it; packaged apps never do.
    /// </summary>
    private static bool IsDevOrigin(Uri origin) =>
        AppService.RuntimeMode == RuntimeMode.Dev && origin.Scheme == "http" && IsLoopbackHost(origin.Host) &&
        origin.Port == DevServerPort;

    private const int DevServerPort = 3000;

    private static bool IsDevPreflight(HttpContext context) =>
        HttpMethods.IsOptions(context.Request.Method) &&
        context.Request.Headers.ContainsKey("Access-Control-Request-Method") &&
        Uri.TryCreate(context.Request.Headers.Origin.ToString(), UriKind.Absolute, out var origin) &&
        IsDevOrigin(origin) && context.Connection.RemoteIpAddress is { } address &&
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        IPAddress.TryParse(host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip);

    private static async Task<string> BoundBodyAsync(HttpContext context, bool hash)
    {
        const int max = NodeRequestSignature.MaxControlBodyBytes;
        if (context.Request.ContentLength > max)
            throw new FederationAccessException("RequestTooLarge", 413, "The node control request is too large.");
        context.Request.EnableBuffering(bufferThreshold: 30 * 1024, bufferLimit: max + 1L);
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
                if (read == 0) break;
                if (body.Length + read > max)
                    throw new FederationAccessException("RequestTooLarge", 413, "The node control request is too large.");
                body.Write(buffer, 0, read);
            }
        }
        catch (IOException)
        {
            throw new FederationAccessException("RequestTooLarge", 413, "The node control request could not be buffered within its limit.");
        }
        finally { context.Request.Body.Position = 0; }
        return hash ? NodeRequestSignature.Hash(body.GetBuffer().AsSpan(0, (int)body.Length)) : "";
    }
}
