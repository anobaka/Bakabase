using Bakabase.Remoting.Components.Forwarding;

namespace Bakabase.Remoting.Components.Console;

/// <summary>
/// The URLs the server switcher hands a window: a relay with a navigation ticket, or this
/// device's own origin.
/// </summary>
public static class RelayUrls
{
    /// <summary>
    /// A path on a server's UI, made safe to append to an origin.
    /// </summary>
    /// <remarks>
    /// The caller is a page, so the path is treated as untrusted: it can only ever name a
    /// place under the origin it is appended to. Leading slashes are collapsed (so it cannot
    /// read as a network-path reference to another host), control characters are dropped,
    /// and a bare fragment such as <c>#/resource</c> — the UI routes by hash — lands at the
    /// root.
    /// </remarks>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var cleaned = new string(path.Trim().Where(c => !char.IsControl(c)).ToArray()).Replace('\\', '/');

        if (cleaned.Contains("://", StringComparison.Ordinal))
        {
            // Absolute URLs are not paths. Refusing to guess which part was meant keeps
            // the answer boring: the root.
            return "/";
        }

        return "/" + cleaned.TrimStart('/');
    }

    /// <summary>
    /// <c>http://127.0.0.1:{port}{path}</c> carrying <paramref name="token"/>, placed in the
    /// query ahead of any fragment so the browser actually sends it.
    /// </summary>
    public static string BuildRelayUrl(int port, string? path, string token)
    {
        var target = NormalizePath(path);
        var hash = target.IndexOf('#');
        var beforeFragment = hash >= 0 ? target[..hash] : target;
        var fragment = hash >= 0 ? target[hash..] : string.Empty;
        var separator = beforeFragment.Contains('?') ? "&" : "?";

        return $"http://127.0.0.1:{port}{beforeFragment}{separator}{RelayNavigationTokens.QueryName}=" +
               $"{Uri.EscapeDataString(token)}{fragment}";
    }
}
