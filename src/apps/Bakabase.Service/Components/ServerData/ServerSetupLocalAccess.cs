using System;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Service.Components.ServerData;

/// <summary>
/// Identifies a direct local caller during first-run setup, without initializing
/// AppService or trusting proxy headers. Containers require their logged capability.
/// The caller must additionally restrict claims to an uncommitted first-run session.
/// </summary>
public static class ServerSetupLocalAccess
{
    public static bool CanClaim(HttpContext context, bool inContainer)
    {
        if (inContainer || context.Connection.RemoteIpAddress is not { } remote || !IsLoopback(remote))
            return false;

        var request = context.Request;
        if (request.Scheme != "http" && request.Scheme != "https") return false;
        var host = request.Host.Host.Trim('[', ']');
        if (!host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
            (!IPAddress.TryParse(host, out var address) || !IsLoopback(address)))
            return false;
        var port = request.Host.Port ?? (request.IsHttps ? 443 : 80);
        if (context.Connection.LocalPort <= 0 || port != context.Connection.LocalPort) return false;

        if (request.Headers.TryGetValue("Sec-Fetch-Site", out var site) &&
            !string.Equals(site.ToString(), "same-origin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(site.ToString(), "none", StringComparison.OrdinalIgnoreCase))
            return false;

        if (request.Headers.TryGetValue("Origin", out var origin))
        {
            if (origin.Count != 1 || !Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var page) ||
                page.UserInfo.Length != 0 || page.PathAndQuery != "/" || page.Fragment.Length != 0 ||
                !page.Scheme.Equals(request.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !page.Host.Trim('[', ']').Equals(host, StringComparison.OrdinalIgnoreCase) || page.Port != port)
                return false;
        }

        return true;
    }

    private static bool IsLoopback(IPAddress address) =>
        IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
