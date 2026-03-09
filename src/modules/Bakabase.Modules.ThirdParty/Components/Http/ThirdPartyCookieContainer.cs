using System.Collections.Concurrent;
using System.Net;
using Bakabase.Modules.ThirdParty.Abstractions.Http;

namespace Bakabase.Modules.ThirdParty.Components.Http;

public class ThirdPartyCookieContainer : IThirdPartyCookieContainer
{
    private readonly ConcurrentDictionary<string, CookieContainer> _containers = new();

    public CookieContainer GetOrCreate(string key, string? staticCookie, Uri seedUri)
    {
        return _containers.GetOrAdd(key, _ => CreateSeeded(staticCookie, seedUri));
    }

    public void Reset(string key)
    {
        _containers.TryRemove(key, out _);
    }

    public void ResetByPrefix(string keyPrefix)
    {
        foreach (var k in _containers.Keys)
        {
            if (k.StartsWith(keyPrefix, StringComparison.Ordinal))
            {
                _containers.TryRemove(k, out _);
            }
        }
    }

    public string? GetCookieHeader(string key, string? staticCookie, Uri uri)
    {
        var container = GetOrCreate(key, staticCookie, uri);
        var header = container.GetCookieHeader(uri);
        return string.IsNullOrEmpty(header) ? null : header;
    }

    public void ProcessResponse(string key, string? staticCookie, Uri requestUri, HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders)) return;

        var container = GetOrCreate(key, staticCookie, requestUri);
        foreach (var setCookie in setCookieHeaders)
        {
            try
            {
                container.SetCookies(requestUri, setCookie);
            }
            catch
            {
                // Skip malformed Set-Cookie headers
            }
        }
    }

    private static CookieContainer CreateSeeded(string? staticCookie, Uri seedUri)
    {
        var container = new CookieContainer();
        if (string.IsNullOrWhiteSpace(staticCookie)) return container;

        var baseDomain = GetBaseDomain(seedUri);

        foreach (var part in staticCookie.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            var name = trimmed[..eqIndex].Trim();
            var value = trimmed[(eqIndex + 1)..].Trim();

            try
            {
                container.Add(new System.Net.Cookie(name, value, "/", $".{baseDomain}"));
            }
            catch
            {
                // Skip malformed cookies
            }
        }

        return container;
    }

    /// <summary>
    /// Extracts the base domain (e.g. "dlsite.com" from "www.dlsite.com" or "play.dlsite.com").
    /// </summary>
    private static string GetBaseDomain(Uri uri)
    {
        var host = uri.Host;
        var parts = host.Split('.');
        return parts.Length > 2 ? string.Join('.', parts[^2..]) : host;
    }
}
