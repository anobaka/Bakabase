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
        var baseDomain = GetBaseDomain(requestUri);

        foreach (var setCookie in setCookieHeaders)
        {
            try
            {
                // Parse the cookie name from the Set-Cookie header
                var cookieName = ExtractCookieName(setCookie);
                if (cookieName != null)
                {
                    // Remove any existing cookies with the same name from domains that have
                    // a containment relationship with the request domain.
                    RemoveCookiesByNameFromRelatedDomains(container, cookieName, requestUri, baseDomain);
                }

                container.SetCookies(requestUri, setCookie);
            }
            catch
            {
                // Skip malformed Set-Cookie headers
            }
        }
    }

    /// <summary>
    /// Extracts the cookie name from a Set-Cookie header value.
    /// </summary>
    private static string? ExtractCookieName(string setCookieHeader)
    {
        var eqIndex = setCookieHeader.IndexOf('=');
        if (eqIndex <= 0) return null;
        return setCookieHeader[..eqIndex].Trim();
    }

    /// <summary>
    /// Removes cookies with the given name from domains that have a containment relationship
    /// with the request domain.
    /// </summary>
    private static void RemoveCookiesByNameFromRelatedDomains(
        CookieContainer container, string cookieName, Uri requestUri, string baseDomain)
    {
        try
        {
            var baseDomainUri = new Uri($"{requestUri.Scheme}://{baseDomain}");
            ExpireCookieByName(container, baseDomainUri, cookieName);
            ExpireCookieByName(container, requestUri, cookieName);
        }
        catch
        {
            // Best effort
        }
    }

    /// <summary>
    /// Expires (removes) a cookie by name from a specific URI in the container.
    /// </summary>
    private static void ExpireCookieByName(CookieContainer container, Uri uri, string cookieName)
    {
        var cookies = container.GetCookies(uri);
        foreach (System.Net.Cookie cookie in cookies)
        {
            if (string.Equals(cookie.Name, cookieName, StringComparison.OrdinalIgnoreCase))
            {
                cookie.Expired = true;
            }
        }
    }

    /// <summary>
    /// Creates a seeded CookieContainer from a static cookie string.
    /// Uses SetCookies with proper Set-Cookie format for maximum compatibility,
    /// since the Cookie() constructor rejects values with commas, spaces, etc.
    /// Seeds to the base domain so cookies are available across all subdomains.
    /// </summary>
    private static CookieContainer CreateSeeded(string? staticCookie, Uri seedUri)
    {
        var container = new CookieContainer();
        if (string.IsNullOrWhiteSpace(staticCookie)) return container;

        var baseDomain = GetBaseDomain(seedUri);
        var seedBaseUri = new Uri($"{seedUri.Scheme}://{baseDomain}");

        foreach (var part in staticCookie.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex <= 0) continue;

            try
            {
                // Use SetCookies with Set-Cookie format: "name=value; domain=.baseDomain; path=/"
                // This is more lenient than the Cookie constructor with special characters.
                container.SetCookies(seedBaseUri, $"{trimmed}; domain=.{baseDomain}; path=/");
            }
            catch
            {
                // Fallback: try adding via Cookie constructor with the raw value
                try
                {
                    var name = trimmed[..eqIndex].Trim();
                    var value = trimmed[(eqIndex + 1)..].Trim();
                    container.Add(new System.Net.Cookie(name, value, "/", $".{baseDomain}"));
                }
                catch
                {
                    // Skip truly malformed cookies
                }
            }
        }

        return container;
    }

    /// <summary>
    /// Extracts the base domain (e.g. "dlsite.com" from "play.dlsite.com").
    /// </summary>
    private static string GetBaseDomain(Uri uri)
    {
        var host = uri.Host;
        var parts = host.Split('.');
        return parts.Length > 2 ? string.Join('.', parts[^2..]) : host;
    }
}
