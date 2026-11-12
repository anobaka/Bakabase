using System;
using System.Linq;
using System.Text.RegularExpressions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.RemoteAccess;

/// <summary>
/// Credential administration belongs to this server's UI, independently of owner
/// authentication. General userscript CORS permission does not authorize it.
/// </summary>
public static class OwnerUiOriginGuard
{
    private static readonly Regex OriginShape = new(@"\Ahttps?://[^/?#\\\s]+/?\z", RegexOptions.CultureInvariant);

    public static bool IsAllowed(HttpContext context)
    {
        var fetchValues = context.Request.Headers["Sec-Fetch-Site"];
        if (fetchValues.Count > 1) return false;
        var fetchSite = fetchValues.ToString();
        if (fetchSite is not ("" or "same-origin" or "same-site" or "none")) return false;
        var origins = context.Request.Headers.Origin;
        if (origins.Count > 1) return false;
        var origin = origins.ToString();
        // Native owner CLI and admitted signed relays do not send Origin. They
        // still require normal remote-access/administration authorization.
        if (origin.Length == 0) return true;
        if (!TryOrigin(origin, out var page)) return false;
        if (ServiceCorsOrigins.UserscriptSites.Any(site => SameOrigin(page!, site))) return false;
        var own = context.Request.Scheme + "://" + context.Request.Host;
        if (SameOrigin(page!, own)) return true;
        // An HTTPS reverse proxy can terminate TLS and forward plain HTTP while
        // preserving the external Host. Browser-controlled same-origin metadata
        // proves the UI relationship; never infer it from Forwarded headers.
        if (fetchSite == "same-origin" && context.Request.Scheme == "http" && page!.Scheme == "https" &&
            SameOrigin(page, "https://" + context.Request.Host)) return true;
        var policy = context.RequestServices?.GetService<ServiceCorsOrigins>() ?? ServiceCorsOrigins.ForThisBuild;
        return policy.TrustsDevServer && ServiceCorsOrigins.DevServerOrigins.Any(site => SameOrigin(page!, site));
    }

    private static bool SameOrigin(Uri actual, string expected) => TryOrigin(expected, out var target) &&
        actual.Scheme == target!.Scheme && actual.IdnHost.Equals(target.IdnHost, StringComparison.OrdinalIgnoreCase) && actual.Port == target.Port;

    private static bool TryOrigin(string value, out Uri? uri)
    {
        uri = null;
        return value.Length <= 4096 && OriginShape.IsMatch(value) && Uri.TryCreate(value, UriKind.Absolute, out uri) &&
            uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0;
    }
}

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class OwnerUiOriginAttribute(string rejectionCode) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!OwnerUiOriginGuard.IsAllowed(context.HttpContext))
            context.Result = new ObjectResult(BaseResponseBuilder.Build(ResponseCode.Unauthorized,
                rejectionCode)) {StatusCode = StatusCodes.Status403Forbidden};
    }
}
