namespace Bakabase.Abstractions.Components.Telemetry;

/// <summary>Desktop-local analytics identity, independent of imported libraries and server pairing.</summary>
public interface IDesktopAnalyticsContextProvider
{
    DesktopAnalyticsContext Read();
}

public sealed record DesktopAnalyticsContext(string? ClientId, string ClientVersion,
    bool EnableAnonymousDataTracking, string ClientSurface = "desktop_webview");

/// <summary>The user-agent is only a presentation hint. Loopback and same-origin checks are mandatory.</summary>
public static class DesktopAnalyticsRequest
{
    public static bool IsAllowed(bool isLoopback, string userAgent, string scheme, string host,
        string? origin, string? fetchSite)
    {
        if (!isLoopback || !userAgent.Split(' ').Contains("BakabaseDesktop/1.0") ||
            fetchSite is not (null or "" or "none" or "same-origin")) return false;
        if (!Uri.TryCreate($"{scheme}://{host}", UriKind.Absolute, out var own) ||
            !(own.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
              System.Net.IPAddress.TryParse(own.Host.Trim('[', ']'), out var address) &&
              System.Net.IPAddress.IsLoopback(address))) return false;
        return string.IsNullOrEmpty(origin) ||
               Uri.TryCreate(origin, UriKind.Absolute, out var caller) &&
               caller.Scheme == own.Scheme && caller.Host == own.Host && caller.Port == own.Port;
    }
}
