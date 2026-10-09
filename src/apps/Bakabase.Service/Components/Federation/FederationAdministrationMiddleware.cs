using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Federation;

/// <summary>
/// Headless administration uses the ordinary remote-access gate before entering the local
/// federation API. Desktop hosts retain their loopback-only boundary; node grants never enter here.
/// </summary>
public sealed class FederationAdministrationMiddleware(RequestDelegate next)
{
    private static readonly object PendingKey = new();
    internal static void RequireAuthorization(HttpContext context) => context.Items[PendingKey] = true;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Items.ContainsKey(PendingKey))
        {
            if (!CanAdminister(context))
                throw new FederationAccessException("LocalInterfaceOnly", 403,
                    "This interface requires permission to administer this server.");
            FederationHttpContext.MarkHandled(context, FederationEndpointKind.Local);
            context.Response.Headers.CacheControl = "no-store";
        }
        await next(context);
    }

    internal static bool IsHeadless(HttpContext context) =>
        context.RequestServices?.GetService<IServerSelfDescription>()?.Kind == ServerKind.Headless;

    public static bool CanAdminister(HttpContext context) =>
        IsHeadless(context) && IsOwnOrigin(context) &&
        context.GetRemoteAccessContext() is { IsLoopback: true } or
            { Mode: not RemoteAccessMode.Disabled, IsPaired: true } or { IsUnrestricted: true };

    internal static bool IsOwnOrigin(HttpContext context)
    {
        // A LAN browser may administer an unrestricted server, but another website may not
        // borrow that browser to change sharing or issue grants. Native signed clients omit Origin.
        var fetchSite = context.Request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite is "cross-site" or "same-site") return false;
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        var requestOrigin = $"{context.Request.Scheme}://{context.Request.Host}";
        return Uri.TryCreate(requestOrigin, UriKind.Absolute, out var expected) &&
               uri.Scheme == expected.Scheme && uri.Host.Equals(expected.Host, StringComparison.OrdinalIgnoreCase) &&
               uri.Port == expected.Port && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) &&
               string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);
    }
}
