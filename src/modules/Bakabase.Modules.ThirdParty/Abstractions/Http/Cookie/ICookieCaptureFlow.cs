using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

/// <summary>
/// Returned by <see cref="ICookieCaptureFlow.OnTick"/> to direct the capture window's behavior.
/// </summary>
public record CookieCaptureStep
{
    /// <summary>Keep waiting for the user to log in.</summary>
    public static CookieCaptureStep Wait { get; } = new();

    /// <summary>Login detected, extract cookies and close the window.</summary>
    public static CookieCaptureStep Done { get; } = new() { IsDone = true };

    /// <summary>Navigate to the specified URL (e.g. to collect additional cookies from another subdomain).</summary>
    public static CookieCaptureStep NavigateTo(string url) => new() { NavigateToUrl = url };

    public bool IsDone { get; init; }
    public string? NavigateToUrl { get; init; }
}

/// <summary>
/// Defines the cookie capture flow for a specific third-party platform.
/// Each platform implements its own login detection and post-login navigation logic.
/// </summary>
public interface ICookieCaptureFlow
{
    CookieValidatorTarget Target { get; }

    /// <summary>The initial URL to navigate to. This should be the target service page
    /// (e.g. play.dlsite.com) rather than the login page, so that already-logged-in
    /// users can be detected automatically.</summary>
    string StartUrl { get; }

    /// <summary>Window title shown during capture.</summary>
    string Title { get; }

    /// <summary>URLs to extract cookies from after login is complete.
    /// Ordered by priority (most specific/important first). When cookies with the
    /// same name exist across multiple URLs, the first one wins.</summary>
    string[] CookieUrls { get; }

    /// <summary>
    /// Called every polling tick (~1 second) with the current WebView URL.
    /// May be called multiple times with the same URL. Implementations can use
    /// tick counting for timeout-based login detection (e.g. if URL stays stable
    /// for N ticks, assume user is already logged in).
    /// </summary>
    CookieCaptureStep OnTick(string currentUrl);
}
