using System;
using System.Net;
using System.Diagnostics;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.CloudTools;

/// <summary>Only the app's resolved per-source proxy enters the child, through private stdin.</summary>
public static class CloudToolProxy
{
    public sealed record Settings(string Url, string? Username, string? Password)
    {
        public override string ToString() => "Cloud tool proxy settings";
    }

    public static void ApplyToEnvironment(ProcessStartInfo process, IWebProxy proxy, Uri destination)
    {
        // These variables belong only to this child; do not inherit ambient routing or credentials.
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY", "http_proxy", "https_proxy", "all_proxy", "no_proxy" })
            process.Environment.Remove(key);
        var settings = Resolve(proxy, destination);
        if (settings == null) return;
        var endpoint = new UriBuilder(settings.Url);
        if (settings.Username != null)
        {
            endpoint.UserName = Uri.EscapeDataString(settings.Username);
            endpoint.Password = Uri.EscapeDataString(settings.Password ?? "");
        }
        process.Environment["HTTP_PROXY"] = endpoint.Uri.AbsoluteUri;
        process.Environment["HTTPS_PROXY"] = endpoint.Uri.AbsoluteUri;
        process.Environment["ALL_PROXY"] = endpoint.Uri.AbsoluteUri;
    }

    public static Settings? Resolve(IWebProxy proxy, Uri destination)
    {
        var endpoint = proxy.GetProxy(destination);
        if (endpoint == null || endpoint == destination) return null;
        if (endpoint.Scheme is not ("http" or "https" or "socks4" or "socks5" or "socks5h"))
            throw new InvalidOperationException("The selected proxy scheme is not supported by cloud download tools.");
        string? username = null, password = null;
        if (!string.IsNullOrWhiteSpace(endpoint.UserInfo))
        {
            var parts = endpoint.UserInfo.Split(':', 2);
            username = Uri.UnescapeDataString(parts[0]);
            password = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : "";
        }
        var credential = proxy.Credentials?.GetCredential(endpoint, "Basic");
        if (!string.IsNullOrEmpty(credential?.UserName))
        {
            username = string.IsNullOrWhiteSpace(credential.Domain) ? credential.UserName : credential.Domain + "\\" + credential.UserName;
            password = credential.Password;
        }
        var clean = new UriBuilder(endpoint) {UserName = "", Password = ""};
        return new Settings(clean.Uri.GetLeftPart(UriPartial.Authority), username, password);
    }
}
