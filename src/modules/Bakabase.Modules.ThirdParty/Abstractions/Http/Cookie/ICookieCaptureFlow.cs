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

    /// <summary>
    /// Optional. Called after each navigation completes; returns the next URL to navigate to,
    /// or null to stop chaining. Used to walk through multi-domain login flows
    /// (e.g. ExHentai's forums.e-hentai.org → e-hentai.org → exhentai.org chain) so the right
    /// cross-domain cookies get set before extraction. The host page dedupes by target URL,
    /// so each unique target is auto-navigated at most once per window.
    /// </summary>
    string? GetNextUrl(string currentUrl) => null;

    /// <summary>
    /// Optional. Cookies to delete from the WebView before the first navigation.
    /// Use this to wipe stale "blocked" markers that would otherwise short-circuit the
    /// auth flow even when fresh credentials are present — e.g. exhentai.org's
    /// `yay=louder` Sad Panda cookie, which makes the server return a blank page on sight
    /// regardless of `igneous`/`ipb_*`. Each entry pairs the URL scope with the cookie name.
    /// </summary>
    (string Url, string Name)[] StaleCookiesToClear => [];

    /// <summary>
    /// Optional. Cross-domain cookie mirrors run before each chain navigation. For each
    /// rule, the named cookies are copied from <see cref="CookieMirror.SourceUrl"/>'s cookie
    /// store onto <see cref="CookieMirror.TargetDomain"/>. Used when a site refuses to
    /// authenticate because the auth cookies got set on a sibling domain that the browser
    /// won't share — e.g. ExHentai's exhentai.org and e-hentai.org are different top-level
    /// domains, so `ipb_*`/`igneous` set during forum login on `.e-hentai.org` never reach
    /// `.exhentai.org` automatically. Mirroring forces the target server to see them.
    /// Mirrors silently skip cookies that don't exist yet on the source.
    /// </summary>
    CookieMirror[] CookieMirrors => [];
}

/// <summary>
/// Declarative rule for mirroring cookies from one domain's cookie store to another.
/// </summary>
/// <param name="SourceUrl">URL whose cookie store is read (e.g. https://e-hentai.org/).</param>
/// <param name="TargetDomain">Cookie domain to write to, including any leading dot
/// (e.g. ".exhentai.org" to cover the apex + all subdomains).</param>
/// <param name="CookieNames">Names of cookies to copy. Cookies not present on the source are skipped.</param>
public record CookieMirror(string SourceUrl, string TargetDomain, string[] CookieNames);
