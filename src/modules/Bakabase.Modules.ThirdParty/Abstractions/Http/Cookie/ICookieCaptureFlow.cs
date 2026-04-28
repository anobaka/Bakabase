using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http.Cookie;

/// <summary>
/// Defines the cookie capture flow for a specific third-party platform.
/// The user logs in manually in the WebView and clicks Confirm when ready.
/// </summary>
public interface ICookieCaptureFlow
{
    CookieValidatorTarget Target { get; }

    /// <summary>The initial URL to navigate to.</summary>
    string StartUrl { get; }

    /// <summary>Platform display name used to compose the window title (e.g. "Patreon").</summary>
    string PlatformName { get; }

    /// <summary>URLs to extract cookies from after the user confirms.
    /// Ordered by priority (most specific/important first). When cookies with the
    /// same name exist across multiple URLs, the first one wins.</summary>
    string[] CookieUrls { get; }
}
