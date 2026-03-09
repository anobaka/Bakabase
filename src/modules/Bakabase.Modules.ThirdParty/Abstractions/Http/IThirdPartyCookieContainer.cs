using System.Net;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http;

/// <summary>
/// Manages per-account CookieContainers for third-party sources.
/// Handles cookie seeding from static strings, automatic Set-Cookie accumulation,
/// and container reset when users update their cookies.
/// </summary>
public interface IThirdPartyCookieContainer
{
    /// <summary>
    /// Gets or creates a CookieContainer for a specific key (e.g. "DLsite:account1").
    /// On first access, seeds the container with cookies parsed from the static cookie string.
    /// </summary>
    CookieContainer GetOrCreate(string key, string? staticCookie, Uri seedUri);

    /// <summary>
    /// Resets (clears) the CookieContainer for a specific key.
    /// Next GetOrCreate call will re-seed from the current static cookie.
    /// </summary>
    void Reset(string key);

    /// <summary>
    /// Resets all CookieContainers whose keys start with the given prefix.
    /// Useful when a source's options change and all account containers should be invalidated.
    /// </summary>
    void ResetByPrefix(string keyPrefix);

    /// <summary>
    /// Gets the cookie header string from the container for a given URI.
    /// Ensures the container is initialized with the static cookie first.
    /// </summary>
    string? GetCookieHeader(string key, string? staticCookie, Uri uri);

    /// <summary>
    /// Processes Set-Cookie headers from a response and stores them in the container.
    /// </summary>
    void ProcessResponse(string key, string? staticCookie, Uri requestUri, HttpResponseMessage response);
}
